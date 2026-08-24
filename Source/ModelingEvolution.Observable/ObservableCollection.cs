// Originally based on Microsoft .NET Runtime ObservableCollection<T> (MIT).
//
// Rewritten by ModelingEvolution (2026-08-24) on top of System.Collections.Immutable.ImmutableList<T>:
//   - readers (Count, indexer, enumeration, IndexOf, Contains, CopyTo) take NO lock and allocate
//     NO copy — they read one reference to an immutable tree that is never mutated in place;
//   - writers are serialised by a single gate and publish a NEW tree per mutation, then raise
//     CollectionChanged inside the gate so event order matches the sequence of published trees;
//   - enumeration through ANY static type (concrete, IEnumerable<T>, LINQ) is a consistent snapshot
//     and can never throw "collection was modified" — the previous Collection<T>-based version only
//     protected foreach over the concrete type (its snapshot enumerator was `new`-hidden).
// Retains: CollectionChanged subscriber tracking with SubscribersAvailable, reentrancy guard,
// Move, BinarySearch, InsertSorted.

using System.Collections;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModelingEvolution.Observable;

/// <summary>
/// Drop-in replacement for <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>
/// backed by <see cref="ImmutableList{T}"/>. Reads are lock-free and never throw under concurrent
/// mutation; a reader observes a consistent snapshot that is at most one mutation stale.
/// Tracks <see cref="CollectionChanged"/> subscriber count and raises
/// <see cref="SubscribersAvailable"/> on 0→1 and 1→0 transitions.
/// </summary>
public class ObservableCollection<T> : IList<T>, IReadOnlyList<T>, IList,
    INotifyCollectionChanged, INotifyPropertyChanged
{
    private ImmutableList<T> _items;
    private readonly object _writeGate = new();
    private int _blockReentrancyCount;
    private int _subscriberCount;
    private NotifyCollectionChangedEventHandler? _collectionChanged;

    /// <summary>
    /// Raised when the first subscriber attaches (true) or the last subscriber detaches (false)
    /// from <see cref="CollectionChanged"/>.
    /// </summary>
    public event Action<bool>? SubscribersAvailable;

    public ObservableCollection() => _items = ImmutableList<T>.Empty;

    public ObservableCollection(IEnumerable<T> collection)
        => _items = ImmutableList.CreateRange(collection ?? throw new ArgumentNullException(nameof(collection)));

    public ObservableCollection(List<T> list)
        => _items = ImmutableList.CreateRange(list ?? throw new ArgumentNullException(nameof(list)));

    /// <summary>The current immutable snapshot. Any member that reads takes this reference once.</summary>
    protected ImmutableList<T> Items => Volatile.Read(ref _items);

    // List<T>(IEnumerable) / LINQ ToList() / ToArray() detect ICollection<T> and do TWO reads:
    // Count (to size the buffer) then CopyTo. Between them a writer can publish a new tree, and
    // CopyTo would either throw (grew) or leave default(T) tails (shrank). Remembering, per thread,
    // the snapshot that Count last reported lets CopyTo serve the SAME snapshot whenever the caller
    // sized its buffer from it — making the two-step protocol atomic without a lock.
    [ThreadStatic] private static (ObservableCollection<T> Owner, ImmutableList<T> Snapshot) _lastCounted;

    // ── reads: no lock, no copy ─────────────────────────────────────────────────────────────

    public int Count
    {
        get
        {
            var s = Items;
            _lastCounted = (this, s);
            return s.Count;
        }
    }

    private ImmutableList<T> SnapshotFor(int space)
    {
        var s = Items;
        if (s.Count == space) return s;
        var (owner, counted) = _lastCounted;
        return ReferenceEquals(owner, this) && counted.Count == space ? counted : s;
    }

    /// <summary>
    /// Reads the current snapshot. If a concurrent shrink made <paramref name="index"/> stale but it
    /// was valid for the snapshot <see cref="Count"/> last reported on this thread (the
    /// Count-then-indexer protocol of LINQ's Skip/Take/Select over <see cref="IList{T}"/>), the
    /// element from that snapshot is returned instead of throwing. Such a read can be torn across
    /// two snapshots; the next change notification settles it.
    /// </summary>
    public T this[int index]
    {
        get
        {
            var s = Items;
            if ((uint)index < (uint)s.Count) return s[index];
            var (owner, counted) = _lastCounted;
            if (ReferenceEquals(owner, this) && (uint)index < (uint)counted.Count) return counted[index];
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        set => SetItem(index, value);
    }

    /// <summary>
    /// Enumerates a consistent snapshot. Never throws under concurrent mutation, regardless of
    /// whether the caller holds the concrete type, <see cref="IEnumerable{T}"/> or LINQ.
    /// </summary>
    public ImmutableList<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();

    public bool Contains(T item) => Items.Contains(item);

    public int IndexOf(T item) => Items.IndexOf(item);

    /// <summary>
    /// Copies a consistent snapshot. Never throws because the collection changed after the caller
    /// read <see cref="Count"/>: the snapshot that <see cref="Count"/> reported on this thread is
    /// used when it fits the destination; otherwise as many items as fit are copied.
    /// </summary>
    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if ((uint)arrayIndex > (uint)array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        int space = array.Length - arrayIndex;
        var s = SnapshotFor(space);
        if (s.Count <= space) s.CopyTo(array, arrayIndex);
        else s.CopyTo(0, array, arrayIndex, space);
    }

    /// <summary>
    /// Performs a binary search on a sorted collection.
    /// The collection must already be sorted according to <paramref name="comparer"/>
    /// (or <see cref="Comparer{T}.Default"/> when <c>null</c>).
    /// </summary>
    /// <returns>
    /// The zero-based index of <paramref name="item"/> if found;
    /// otherwise, the bitwise complement (<c>~index</c>) of the insertion point.
    /// </returns>
    public int BinarySearch(T item, IComparer<T>? comparer = null)
        => BinarySearch(Items, item, comparer ?? Comparer<T>.Default);

    private static int BinarySearch(ImmutableList<T> items, T item, IComparer<T> comparer)
    {
        int lo = 0, hi = items.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            int cmp = comparer.Compare(items[mid], item);
            if (cmp == 0) return mid;
            if (cmp < 0) lo = mid + 1;
            else hi = mid - 1;
        }
        return ~lo;
    }

    // ── writes: serialised, publish a new tree, raise inside the gate ────────────────────────

    public void Add(T item) => InsertItem(Count, item);

    public void Insert(int index, T item) => InsertItem(index, item);

    public bool Remove(T item)
    {
        lock (_writeGate)
        {
            int index = _items.IndexOf(item);
            if (index < 0) return false;
            RemoveItem(index);
            return true;
        }
    }

    public void RemoveAt(int index) => RemoveItem(index);

    public void Clear() => ClearItems();

    /// <summary>Move item at oldIndex to newIndex.</summary>
    public void Move(int oldIndex, int newIndex) => MoveItem(oldIndex, newIndex);

    /// <summary>
    /// Inserts an item into the collection maintaining sorted order and raises
    /// <see cref="CollectionChanged"/>. Equal elements are inserted at the first match.
    /// </summary>
    public void InsertSorted(T item, IComparer<T>? comparer = null)
    {
        comparer ??= Comparer<T>.Default;
        lock (_writeGate)
        {
            int index = BinarySearch(_items, item, comparer);
            if (index < 0) index = ~index;
            InsertItem(index, item);
        }
    }

    protected virtual void ClearItems()
    {
        lock (_writeGate)
        {
            CheckReentrancy();
            Publish(ImmutableList<T>.Empty);
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionReset();
        }
    }

    protected virtual void RemoveItem(int index)
    {
        lock (_writeGate)
        {
            CheckReentrancy();
            if ((uint)index >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            T removedItem = _items[index];
            Publish(_items.RemoveAt(index));
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removedItem, index));
        }
    }

    protected virtual void InsertItem(int index, T item)
    {
        lock (_writeGate)
        {
            CheckReentrancy();
            // Clamp — Add reads Count before taking the gate, so concurrent removes can make it stale.
            if (index > _items.Count) index = _items.Count;
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            Publish(_items.Insert(index, item));
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
        }
    }

    protected virtual void SetItem(int index, T item)
    {
        lock (_writeGate)
        {
            CheckReentrancy();
            if ((uint)index >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            T originalItem = _items[index];
            Publish(_items.SetItem(index, item));
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, originalItem, index));
        }
    }

    protected virtual void MoveItem(int oldIndex, int newIndex)
    {
        lock (_writeGate)
        {
            CheckReentrancy();
            if ((uint)oldIndex >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(oldIndex));
            if ((uint)newIndex >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(newIndex));
            T removedItem = _items[oldIndex];
            Publish(_items.RemoveAt(oldIndex).Insert(newIndex, removedItem));
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, removedItem, newIndex, oldIndex));
        }
    }

    private void Publish(ImmutableList<T> next) => Volatile.Write(ref _items, next);

    // ── events ──────────────────────────────────────────────────────────────────────────────

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    /// <summary>
    /// Occurs when the collection changes. Tracks subscriber count and raises
    /// <see cref="SubscribersAvailable"/> on transitions. Add/remove is atomic — two circuits
    /// subscribing concurrently cannot lose a delegate (2026-08-11 UI review, F5.3).
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add
        {
            NotifyCollectionChangedEventHandler? current, updated;
            do
            {
                current = _collectionChanged;
                updated = (NotifyCollectionChangedEventHandler?)Delegate.Combine(current, value);
            } while (Interlocked.CompareExchange(ref _collectionChanged, updated, current) != current);
            if (Interlocked.Increment(ref _subscriberCount) == 1)
                SubscribersAvailable?.Invoke(true);
        }
        remove
        {
            NotifyCollectionChangedEventHandler? current, updated;
            do
            {
                current = _collectionChanged;
                updated = (NotifyCollectionChangedEventHandler?)Delegate.Remove(current, value);
            } while (Interlocked.CompareExchange(ref _collectionChanged, updated, current) != current);
            if (Interlocked.Decrement(ref _subscriberCount) == 0)
                SubscribersAvailable?.Invoke(false);
        }
    }

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        => PropertyChanged?.Invoke(this, e);

    protected virtual event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        var handler = _collectionChanged;
        if (handler != null)
        {
            _blockReentrancyCount++;
            try
            {
                handler(this, e);
            }
            finally
            {
                _blockReentrancyCount--;
            }
        }
    }

    protected IDisposable BlockReentrancy()
    {
        _blockReentrancyCount++;
        return new ReentrancyGuard(this);
    }

    protected void CheckReentrancy()
    {
        if (_blockReentrancyCount > 0)
        {
            var handler = _collectionChanged;
            if (handler != null && handler.GetInvocationList().Length > 1)
                throw new InvalidOperationException("ObservableCollection reentrancy not allowed.");
        }
    }

    private void OnCountPropertyChanged()
        => OnPropertyChanged(new PropertyChangedEventArgs("Count"));

    private void OnIndexerPropertyChanged()
        => OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));

    private void OnCollectionReset()
        => OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

    private sealed class ReentrancyGuard(ObservableCollection<T> collection) : IDisposable
    {
        public void Dispose() => collection._blockReentrancyCount--;
    }

    // ── ICollection<T> / IList (non-generic) plumbing ───────────────────────────────────────

    bool ICollection<T>.IsReadOnly => false;

    bool IList.IsReadOnly => false;
    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => _writeGate;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (T)value!;
    }

    int IList.Add(object? value)
    {
        lock (_writeGate)
        {
            int index = _items.Count;
            InsertItem(index, (T)value!);
            return index;
        }
    }

    bool IList.Contains(object? value) => value is T t && Contains(t);
    int IList.IndexOf(object? value) => value is T t ? IndexOf(t) : -1;
    void IList.Insert(int index, object? value) => Insert(index, (T)value!);
    void IList.Remove(object? value) { if (value is T t) Remove(t); }
    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        int space = array.Length - index;
        var s = SnapshotFor(space);
        int n = Math.Min(s.Count, space);
        for (int i = 0; i < n; i++) array.SetValue(s[i], index + i);
    }
}
