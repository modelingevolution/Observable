// Based on Microsoft .NET Runtime ObservableCollection<T>
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Modified by ModelingEvolution: added CollectionChanged subscriber tracking
// with SubscribersAvailable event for lifecycle management.
// Added ReaderWriterLockSlim for thread safety.

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModelingEvolution.Observable;

/// <summary>
/// Drop-in replacement for <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>
/// that tracks <see cref="CollectionChanged"/> subscriber count and raises
/// <see cref="SubscribersAvailable"/> on 0→1 and 1→0 transitions.
/// Thread-safe via <see cref="ReaderWriterLockSlim"/>.
/// </summary>
public class ObservableCollection<T> : Collection<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private int _blockReentrancyCount;
    private int _subscriberCount;
    private NotifyCollectionChangedEventHandler? _collectionChanged;

    /// <summary>
    /// Raised when the first subscriber attaches (true) or the last subscriber detaches (false)
    /// from <see cref="CollectionChanged"/>.
    /// </summary>
    public event Action<bool>? SubscribersAvailable;

    public ObservableCollection() { }

    public ObservableCollection(IEnumerable<T> collection)
        : base(new List<T>(collection ?? throw new ArgumentNullException(nameof(collection)))) { }

    public ObservableCollection(List<T> list)
        : base(new List<T>(list ?? throw new ArgumentNullException(nameof(list)))) { }

    /// <summary>
    /// Move item at oldIndex to newIndex.
    /// </summary>
    public void Move(int oldIndex, int newIndex) => MoveItem(oldIndex, newIndex);

    /// <summary>
    /// Returns an enumerator that acquires a read lock per element access,
    /// releasing it before yielding so the lock is not held across the enumeration.
    /// </summary>
    public new IEnumerator<T> GetEnumerator()
    {
        int i = 0;
        while (true)
        {
            T item;
            _lock.EnterReadLock();
            try
            {
                if (i >= Count) yield break;
                item = Items[i];
            }
            finally
            {
                _lock.ExitReadLock();
            }
            i++;
            yield return item;
        }
    }

    /// <summary>
    /// Performs a binary search on a sorted collection.
    /// The collection must already be sorted according to <paramref name="comparer"/>
    /// (or <see cref="Comparer{T}.Default"/> when <c>null</c>).
    /// </summary>
    /// <param name="item">The item to search for.</param>
    /// <param name="comparer">
    /// The comparer to use. When <c>null</c>, <see cref="Comparer{T}.Default"/> is used.
    /// </param>
    /// <returns>
    /// The zero-based index of <paramref name="item"/> if found;
    /// otherwise, the bitwise complement (<c>~index</c>) of the insertion point.
    /// </returns>
    public int BinarySearch(T item, IComparer<T>? comparer = null)
    {
        _lock.EnterReadLock();
        try
        {
            comparer ??= Comparer<T>.Default;
            int lo = 0, hi = Count - 1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                int cmp = comparer.Compare(Items[mid], item);
                if (cmp == 0) return mid;
                if (cmp < 0) lo = mid + 1;
                else hi = mid - 1;
            }
            return ~lo;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Inserts an item into the collection maintaining sorted order.
    /// Uses <see cref="BinarySearch"/> to find the correct insertion point
    /// and raises <see cref="CollectionChanged"/>.
    /// </summary>
    /// <param name="item">The item to insert.</param>
    /// <param name="comparer">
    /// The comparer to use. When <c>null</c>, <see cref="Comparer{T}.Default"/> is used.
    /// </param>
    public void InsertSorted(T item, IComparer<T>? comparer = null)
    {
        _lock.EnterWriteLock();
        try
        {
            comparer ??= Comparer<T>.Default;
            int lo = 0, hi = Count - 1;
            while (lo <= hi)
            {
                int mid = lo + (hi - lo) / 2;
                int cmp = comparer.Compare(Items[mid], item);
                if (cmp == 0) { lo = mid; break; }
                if (cmp < 0) lo = mid + 1;
                else hi = mid - 1;
            }
            InsertItem(lo, item);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add => PropertyChanged += value;
        remove => PropertyChanged -= value;
    }

    /// <summary>
    /// Occurs when the collection changes. Tracks subscriber count and raises
    /// <see cref="SubscribersAvailable"/> on transitions.
    /// </summary>
    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add
        {
            _collectionChanged += value;
            if (Interlocked.Increment(ref _subscriberCount) == 1)
                SubscribersAvailable?.Invoke(true);
        }
        remove
        {
            _collectionChanged -= value;
            if (Interlocked.Decrement(ref _subscriberCount) == 0)
                SubscribersAvailable?.Invoke(false);
        }
    }

    protected override void ClearItems()
    {
        _lock.EnterWriteLock();
        try
        {
            CheckReentrancy();
            base.ClearItems();
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionReset();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    protected override void RemoveItem(int index)
    {
        _lock.EnterWriteLock();
        try
        {
            CheckReentrancy();
            if (index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            T removedItem = this[index];
            base.RemoveItem(index);
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, removedItem, index));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    protected override void InsertItem(int index, T item)
    {
        _lock.EnterWriteLock();
        try
        {
            CheckReentrancy();
            // Clamp index — Collection<T>.Add reads Count before we acquire the lock,
            // so concurrent removes can make the index stale.
            if (index > Count) index = Count;
            base.InsertItem(index, item);
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    protected override void SetItem(int index, T item)
    {
        _lock.EnterWriteLock();
        try
        {
            CheckReentrancy();
            T originalItem = this[index];
            base.SetItem(index, item);
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, originalItem, index));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    protected virtual void MoveItem(int oldIndex, int newIndex)
    {
        _lock.EnterWriteLock();
        try
        {
            CheckReentrancy();
            T removedItem = this[oldIndex];
            base.RemoveItem(oldIndex);
            base.InsertItem(newIndex, removedItem);
            OnIndexerPropertyChanged();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Move, removedItem, newIndex, oldIndex));
        }
        finally
        {
            _lock.ExitWriteLock();
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
}
