using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModelingEvolution.Observable;

/// <summary>
/// A fixed-capacity ring buffer that implements <see cref="INotifyCollectionChanged"/>
/// for efficient Blazor rendering. Lock-free for single-writer / multiple-reader scenarios.
/// <para>
/// When not full, <c>Add</c> fires <see cref="NotifyCollectionChangedAction.Add"/>.
/// When full, <c>Add</c> overwrites the oldest slot and fires
/// <see cref="NotifyCollectionChangedAction.Replace"/> — one DOM op, no index shifting.
/// </para>
/// <para>
/// Thread safety: single writer (projection thread) calls <c>Add</c>.
/// Multiple readers snapshot <c>(EndSequenceNumber, Count)</c> independently.
/// No locks — only <see cref="Volatile"/> and <see cref="Interlocked"/> operations.
/// </para>
/// </summary>
public class ObservableRingBuffer<T> : IReadOnlyList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly T[] _items;
    private readonly int _capacity;
    private readonly int _readMargin;

    private ulong _endSequenceNumber; // always increases, never wraps
    private int _count;               // 0..capacity

    /// <summary>
    /// Creates a new ring buffer with the specified capacity and read margin.
    /// </summary>
    /// <param name="capacity">Maximum number of items the buffer can hold. Must be positive.</param>
    /// <param name="readMargin">
    /// Safety gap between write head and reader position. Must be &gt;= 0 and &lt; capacity.
    /// When omitted, defaults to <c>Min(8, capacity - 1)</c> — safe for any capacity,
    /// including capacity=1 where readMargin=0 (no safety gap, single slot).
    /// </param>
    public ObservableRingBuffer(int capacity, int readMargin = -1)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");

        // Default: min(8, capacity - 1) — safe for any capacity including 1
        if (readMargin < 0)
            readMargin = Math.Min(8, capacity - 1);

        if (readMargin >= capacity)
            throw new ArgumentOutOfRangeException(nameof(readMargin),
                "ReadMargin must be < capacity.");

        _items = new T[capacity];
        _capacity = capacity;
        _readMargin = readMargin;
    }

    // ── Read properties (lock-free) ──

    public int Count => Volatile.Read(ref _count);
    public int Capacity => _capacity;
    public int ReadMargin => _readMargin;
    public ulong EndSequenceNumber => Volatile.Read(ref _endSequenceNumber);

    /// <summary>
    /// Returns 0 during initial fill when EndSeq &lt; Count (buffer not yet full).
    /// </summary>
    public ulong FirstSequenceNumber
    {
        get
        {
            var end = EndSequenceNumber;
            var count = (ulong)Count;
            return end >= count ? end - count : 0;
        }
    }

    // ── Indexed access ──

    /// <summary>
    /// Access by sequence number → physical slot.
    /// </summary>
    public ref readonly T ItemAt(ulong sequenceNumber)
        => ref _items[sequenceNumber % (ulong)_capacity];

    /// <summary>
    /// IReadOnlyList&lt;T&gt; — logical index: 0 = oldest, Count-1 = newest.
    /// </summary>
    public T this[int index]
    {
        get
        {
            var count = Count;
            if ((uint)index >= (uint)count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _items[(FirstSequenceNumber + (ulong)index) % (ulong)_capacity];
        }
    }

    /// <summary>
    /// Convert physical array index to logical index (0=oldest, Count-1=newest).
    /// </summary>
    public int PhysicalToLogical(int physicalIndex)
    {
        var first = (int)(FirstSequenceNumber % (ulong)_capacity);
        return (physicalIndex - first + _capacity) % _capacity;
    }

    // ── Write (single writer, lock-free) ──

    /// <summary>
    /// Adds an item to the buffer. When full, overwrites the oldest slot in-place.
    /// <para>Single writer only — do not call from multiple threads concurrently.</para>
    /// </summary>
    public void Add(T item)
    {
        var seq = _endSequenceNumber;
        var physicalIndex = (int)(seq % (ulong)_capacity);

        if (_count < _capacity)
        {
            // Not full: append
            _items[physicalIndex] = item;
            Thread.MemoryBarrier(); // store-release: item visible before seq update (ARM safety)
            Volatile.Write(ref _endSequenceNumber, seq + 1);
            var newCount = Interlocked.Increment(ref _count);

            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add, item, newCount - 1));
            OnPropertyChanged(nameof(Count));
        }
        else
        {
            // Full: overwrite oldest slot in-place
            var oldItem = _items[physicalIndex];
            _items[physicalIndex] = item;
            Thread.MemoryBarrier(); // store-release: item visible before seq update (ARM safety)
            Volatile.Write(ref _endSequenceNumber, seq + 1);

            // Replace at logical index — renderer updates one element
            var logicalIndex = PhysicalToLogical(physicalIndex);
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace, item, oldItem, logicalIndex));
            // Note: Count doesn't change on Replace — don't fire PropertyChanged(Count)
        }

        OnPropertyChanged(nameof(EndSequenceNumber));
    }

    // ── Iterator (snapshot for lock-free enumeration) ──

    /// <summary>
    /// Creates a snapshot iterator for lock-free enumeration.
    /// </summary>
    public RingIterator GetIterator()
    {
        var end = Volatile.Read(ref _endSequenceNumber);
        var count = Volatile.Read(ref _count);
        return new RingIterator(end, count, _capacity, _readMargin);
    }

    public readonly struct RingIterator
    {
        public readonly ulong EndSequenceNumber;
        public readonly int Count;
        public readonly int Capacity;
        public readonly int ReadMargin;

        public RingIterator(ulong endSequenceNumber, int count, int capacity, int readMargin)
        {
            EndSequenceNumber = endSequenceNumber;
            Count = count;
            Capacity = capacity;
            ReadMargin = readMargin;
        }

        public ulong FirstSequenceNumber =>
            EndSequenceNumber >= (ulong)Count
                ? EndSequenceNumber - (ulong)Count
                : 0;

        /// <summary>
        /// Map logical iteration index (0..Count-1) to physical array index.
        /// </summary>
        public int PhysicalIndex(int logicalIndex) =>
            (int)((FirstSequenceNumber + (ulong)logicalIndex) % (ulong)Capacity);
    }

    /// <summary>
    /// Check if a previously acquired iterator is still valid (not lapped by the writer).
    /// Returns false when the writer has advanced more than (capacity - readMargin) items
    /// since the iterator was created, meaning the reader's slots may have been overwritten.
    /// </summary>
    public bool IsValid(RingIterator iter)
        => EndSequenceNumber - iter.EndSequenceNumber <= (ulong)(_capacity - _readMargin);

    // ── IEnumerable<T> ──

    /// <summary>
    /// Iterates all Count items. For rendering, use <see cref="RingIterator"/>
    /// which respects ReadMargin. GetEnumerator is for debugging/testing only.
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        var iter = GetIterator();
        for (int i = 0; i < iter.Count; i++)
            yield return _items[iter.PhysicalIndex(i)];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ── Events ──

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        => CollectionChanged?.Invoke(this, e);

    private void OnPropertyChanged(string name)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
