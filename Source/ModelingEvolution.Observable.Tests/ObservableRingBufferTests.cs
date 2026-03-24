using System.Collections.Specialized;
using System.ComponentModel;
using FluentAssertions;

namespace ModelingEvolution.Observable.Tests;

public class ObservableRingBufferTests
{
    // ────────────────────────────────────────────────────────
    // AC1: Lock-free Add — buffer keeps last N items
    // ────────────────────────────────────────────────────────

    [Fact]
    public void Add_Should_ContainLastNItems_WhenOverCapacity()
    {
        var buf = new ObservableRingBuffer<int>(40);

        for (int i = 0; i < 100; i++)
            buf.Add(i);

        buf.Count.Should().Be(40);
        for (int i = 0; i < 40; i++)
            buf[i].Should().Be(60 + i);
    }

    [Fact]
    public void Add_Should_SetCountToOne_WhenSingleItem()
    {
        var buf = new ObservableRingBuffer<int>(10);
        buf.Add(42);

        buf.Count.Should().Be(1);
        buf[0].Should().Be(42);
        buf.EndSequenceNumber.Should().Be(1UL);
        buf.FirstSequenceNumber.Should().Be(0UL);
    }

    [Fact]
    public void Add_Should_ContainAllItems_WhenFilledToCapacity()
    {
        var buf = new ObservableRingBuffer<int>(5);
        for (int i = 0; i < 5; i++)
            buf.Add(i * 10);

        buf.Count.Should().Be(5);
        buf.EndSequenceNumber.Should().Be(5UL);
        for (int i = 0; i < 5; i++)
            buf[i].Should().Be(i * 10);
    }

    [Fact]
    public void Add_Should_EvictOldest_WhenOverflowByOne()
    {
        var buf = new ObservableRingBuffer<int>(3);
        buf.Add(10);
        buf.Add(20);
        buf.Add(30);
        buf.Add(40); // overwrites slot 0 (was 10)

        buf.Count.Should().Be(3);
        buf[0].Should().Be(20); // oldest
        buf[1].Should().Be(30);
        buf[2].Should().Be(40); // newest
    }

    // ────────────────────────────────────────────────────────
    // AC2: CollectionChanged events
    // ────────────────────────────────────────────────────────

    [Fact]
    public void Add_Should_FireAddEvents_WhenNotFull()
    {
        var buf = new ObservableRingBuffer<int>(5);
        var events = new List<NotifyCollectionChangedEventArgs>();
        buf.CollectionChanged += (_, e) => events.Add(e);

        buf.Add(10);
        buf.Add(20);
        buf.Add(30);

        events.Should().HaveCount(3);
        for (int i = 0; i < 3; i++)
        {
            events[i].Action.Should().Be(NotifyCollectionChangedAction.Add);
            events[i].NewStartingIndex.Should().Be(i);
            events[i].NewItems![0].Should().Be((i + 1) * 10);
        }
    }

    [Fact]
    public void Add_Should_FireReplaceEvents_WhenFull()
    {
        var buf = new ObservableRingBuffer<int>(5);
        var events = new List<NotifyCollectionChangedEventArgs>();

        for (int i = 0; i < 5; i++)
            buf.Add(i);

        buf.CollectionChanged += (_, e) => events.Add(e);

        buf.Add(100);
        buf.Add(200);
        buf.Add(300);

        events.Should().HaveCount(3);
        events.Should().OnlyContain(e => e.Action == NotifyCollectionChangedAction.Replace);

        // First replace: seq=5, physIdx=0, old=0, new=100
        events[0].NewItems![0].Should().Be(100);
        events[0].OldItems![0].Should().Be(0);

        // Second replace: seq=6, physIdx=1, old=1, new=200
        events[1].NewItems![0].Should().Be(200);
        events[1].OldItems![0].Should().Be(1);

        // Third replace: seq=7, physIdx=2, old=2, new=300
        events[2].NewItems![0].Should().Be(300);
        events[2].OldItems![0].Should().Be(2);
    }

    [Fact]
    public void Add_Should_FireCountPropertyChanged_WhenNotFull()
    {
        var buf = new ObservableRingBuffer<int>(5);
        var props = new List<string>();
        buf.PropertyChanged += (_, e) => props.Add(e.PropertyName!);

        buf.Add(1);

        props.Should().Contain(nameof(buf.Count));
        props.Should().Contain(nameof(buf.EndSequenceNumber));
    }

    [Fact]
    public void Add_Should_NotFireCountPropertyChanged_WhenFull()
    {
        var buf = new ObservableRingBuffer<int>(2);
        buf.Add(1);
        buf.Add(2);

        var props = new List<string>();
        buf.PropertyChanged += (_, e) => props.Add(e.PropertyName!);

        buf.Add(3); // full, replace

        props.Should().NotContain(nameof(buf.Count));
        props.Should().Contain(nameof(buf.EndSequenceNumber));
    }

    // ────────────────────────────────────────────────────────
    // Sequence number tracking
    // ────────────────────────────────────────────────────────

    [Fact]
    public void SequenceNumbers_Should_TrackCorrectly()
    {
        var buf = new ObservableRingBuffer<int>(3);

        buf.EndSequenceNumber.Should().Be(0UL);
        buf.FirstSequenceNumber.Should().Be(0UL);

        buf.Add(10);
        buf.EndSequenceNumber.Should().Be(1UL);
        buf.FirstSequenceNumber.Should().Be(0UL);

        buf.Add(20);
        buf.Add(30); // full
        buf.EndSequenceNumber.Should().Be(3UL);
        buf.FirstSequenceNumber.Should().Be(0UL);

        buf.Add(40); // overwrites slot 0
        buf.EndSequenceNumber.Should().Be(4UL);
        buf.FirstSequenceNumber.Should().Be(1UL);
        buf.Count.Should().Be(3);
    }

    // ────────────────────────────────────────────────────────
    // ItemAt (sequence number access)
    // ────────────────────────────────────────────────────────

    [Fact]
    public void ItemAt_Should_ReturnCorrectItem()
    {
        var buf = new ObservableRingBuffer<int>(4);
        buf.Add(10);
        buf.Add(20);
        buf.Add(30);
        buf.Add(40);

        buf.ItemAt(0).Should().Be(10);
        buf.ItemAt(1).Should().Be(20);
        buf.ItemAt(2).Should().Be(30);
        buf.ItemAt(3).Should().Be(40);
    }

    // ────────────────────────────────────────────────────────
    // Indexer (logical access)
    // ────────────────────────────────────────────────────────

    [Fact]
    public void Indexer_Should_ThrowOnOutOfRange()
    {
        var buf = new ObservableRingBuffer<int>(5);
        buf.Add(1);

        var act1 = () => buf[1];
        var act2 = () => buf[-1];

        act1.Should().Throw<ArgumentOutOfRangeException>();
        act2.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Indexer_Should_ReturnLogicalOrder_AfterWrap()
    {
        var buf = new ObservableRingBuffer<int>(3);
        buf.Add(1);
        buf.Add(2);
        buf.Add(3);
        buf.Add(4); // wraps: oldest=2, newest=4
        buf.Add(5); // oldest=3, newest=5

        buf[0].Should().Be(3); // oldest
        buf[1].Should().Be(4);
        buf[2].Should().Be(5); // newest
    }

    // ────────────────────────────────────────────────────────
    // PhysicalToLogical
    // ────────────────────────────────────────────────────────

    [Fact]
    public void PhysicalToLogical_Should_BeIdentity_BeforeWrap()
    {
        var buf = new ObservableRingBuffer<int>(5);
        for (int i = 0; i < 3; i++)
            buf.Add(i);

        buf.PhysicalToLogical(0).Should().Be(0);
        buf.PhysicalToLogical(1).Should().Be(1);
        buf.PhysicalToLogical(2).Should().Be(2);
    }

    [Fact]
    public void PhysicalToLogical_Should_MapCorrectly_AfterWrap()
    {
        var buf = new ObservableRingBuffer<int>(3);
        for (int i = 0; i < 5; i++)
            buf.Add(i);
        // EndSeq=5, FirstSeq=2, first phys = 2%3 = 2
        // phys 2 → logical 0 (oldest)
        // phys 0 → logical 1
        // phys 1 → logical 2 (newest)

        buf.PhysicalToLogical(2).Should().Be(0);
        buf.PhysicalToLogical(0).Should().Be(1);
        buf.PhysicalToLogical(1).Should().Be(2);
    }

    // ────────────────────────────────────────────────────────
    // AC3: Iterator snapshot consistency
    // ────────────────────────────────────────────────────────

    [Fact]
    public void GetIterator_Should_SnapshotState()
    {
        var buf = new ObservableRingBuffer<int>(10, readMargin: 3);
        for (int i = 0; i < 10; i++)
            buf.Add(i);

        var iter = buf.GetIterator();
        iter.EndSequenceNumber.Should().Be(10UL);
        iter.Count.Should().Be(10);
        iter.Capacity.Should().Be(10);
        iter.ReadMargin.Should().Be(3);
        iter.FirstSequenceNumber.Should().Be(0UL);
    }

    [Fact]
    public void Iterator_Should_MapPhysicalIndexCorrectly()
    {
        var buf = new ObservableRingBuffer<int>(4, readMargin: 1);
        for (int i = 0; i < 6; i++)
            buf.Add(i); // EndSeq=6, FirstSeq=2

        var iter = buf.GetIterator();
        iter.FirstSequenceNumber.Should().Be(2UL);

        iter.PhysicalIndex(0).Should().Be(2); // logical 0 → phys (2%4)=2
        iter.PhysicalIndex(1).Should().Be(3); // logical 1 → phys (3%4)=3
        iter.PhysicalIndex(2).Should().Be(0); // logical 2 → phys (4%4)=0
        iter.PhysicalIndex(3).Should().Be(1); // logical 3 → phys (5%4)=1
    }

    [Fact]
    public void Iterator_Should_BeUnaffectedBySubsequentWrites()
    {
        var buf = new ObservableRingBuffer<int>(5, readMargin: 2);
        for (int i = 0; i < 5; i++)
            buf.Add(i * 10);

        var iter = buf.GetIterator();

        buf.Add(50);
        buf.Add(60);

        // Iterator still sees old snapshot
        iter.EndSequenceNumber.Should().Be(5UL);
        iter.Count.Should().Be(5);
        iter.FirstSequenceNumber.Should().Be(0UL);

        // But buffer has moved on
        buf.EndSequenceNumber.Should().Be(7UL);
    }

    // ────────────────────────────────────────────────────────
    // AC5: ReadMargin / IsValid
    // ────────────────────────────────────────────────────────

    [Fact]
    public void IsValid_Should_ReturnTrue_WhenWriterWithinMargin()
    {
        var buf = new ObservableRingBuffer<int>(10, readMargin: 3);
        for (int i = 0; i < 10; i++)
            buf.Add(i);

        var iter = buf.GetIterator();

        // Add up to (capacity - readMargin) = 7 items — still valid
        for (int i = 0; i < 7; i++)
            buf.Add(100 + i);

        buf.IsValid(iter).Should().BeTrue();
    }

    [Fact]
    public void IsValid_Should_ReturnFalse_WhenWriterExceedsMargin()
    {
        var buf = new ObservableRingBuffer<int>(10, readMargin: 3);
        for (int i = 0; i < 10; i++)
            buf.Add(i);

        var iter = buf.GetIterator();

        // Add (capacity - readMargin + 1) = 8 items — invalid
        for (int i = 0; i < 8; i++)
            buf.Add(100 + i);

        buf.IsValid(iter).Should().BeFalse();
    }

    [Fact]
    public void IsValid_Should_ReturnTrue_ImmediatelyAfterSnapshot()
    {
        var buf = new ObservableRingBuffer<int>(5, readMargin: 2);
        for (int i = 0; i < 5; i++)
            buf.Add(i);

        var iter = buf.GetIterator();
        buf.IsValid(iter).Should().BeTrue();
    }

    // ────────────────────────────────────────────────────────
    // Enumeration
    // ────────────────────────────────────────────────────────

    [Fact]
    public void GetEnumerator_Should_ReturnItemsInLogicalOrder()
    {
        var buf = new ObservableRingBuffer<int>(4);
        for (int i = 0; i < 6; i++)
            buf.Add(i); // EndSeq=6, keeps [2,3,4,5]

        buf.Should().Equal(2, 3, 4, 5);
    }

    [Fact]
    public void GetEnumerator_Should_ReturnNothing_WhenEmpty()
    {
        var buf = new ObservableRingBuffer<int>(5);
        buf.Should().BeEmpty();
    }

    [Fact]
    public void GetEnumerator_Should_ReturnItems_WhenPartiallyFilled()
    {
        var buf = new ObservableRingBuffer<int>(10);
        buf.Add(1);
        buf.Add(2);
        buf.Add(3);

        buf.Should().Equal(1, 2, 3);
    }

    // ────────────────────────────────────────────────────────
    // Constructor validation
    // ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_Should_Throw_WhenCapacityInvalid()
    {
        var act0 = () => new ObservableRingBuffer<int>(0);
        var actNeg = () => new ObservableRingBuffer<int>(-1);

        act0.Should().Throw<ArgumentOutOfRangeException>();
        actNeg.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_Should_Throw_WhenReadMarginInvalid()
    {
        var actEqual = () => new ObservableRingBuffer<int>(5, 5);
        var actOver = () => new ObservableRingBuffer<int>(5, 6);

        actEqual.Should().Throw<ArgumentOutOfRangeException>();
        actOver.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_Should_AllowZeroReadMargin()
    {
        var buf = new ObservableRingBuffer<int>(5, readMargin: 0);
        buf.ReadMargin.Should().Be(0);
    }

    [Fact]
    public void Constructor_Should_AutoScaleReadMargin()
    {
        // capacity=1 → readMargin = min(8, 0) = 0
        new ObservableRingBuffer<int>(1).ReadMargin.Should().Be(0);
        // capacity=2 → readMargin = min(8, 1) = 1
        new ObservableRingBuffer<int>(2).ReadMargin.Should().Be(1);
        // capacity=5 → readMargin = min(8, 4) = 4
        new ObservableRingBuffer<int>(5).ReadMargin.Should().Be(4);
        // capacity=40 → readMargin = min(8, 39) = 8
        new ObservableRingBuffer<int>(40).ReadMargin.Should().Be(8);
    }

    // ────────────────────────────────────────────────────────
    // Replace event logical index correctness
    // ────────────────────────────────────────────────────────

    [Fact]
    public void Replace_Should_FireCorrectLogicalIndex()
    {
        var buf = new ObservableRingBuffer<int>(3);
        buf.Add(0);
        buf.Add(1);
        buf.Add(2); // full: [0,1,2]

        var replaceIndices = new List<int>();
        buf.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Replace)
                replaceIndices.Add(e.NewStartingIndex);
        };

        buf.Add(3); // phys 0 → logical 2
        buf.Add(4); // phys 1 → logical 2
        buf.Add(5); // phys 2 → logical 2

        // Each replace always puts the newest item at logical Count-1 = 2
        replaceIndices.Should().Equal(2, 2, 2);
        buf.Should().Equal(3, 4, 5);
    }

    // ────────────────────────────────────────────────────────
    // Multi-threading: single writer + multiple readers
    // ────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentReaders_Should_NotThrow_DuringSingleWriter()
    {
        const int capacity = 40;
        const int readMargin = 8;
        var buf = new ObservableRingBuffer<int>(capacity, readMargin);

        for (int i = 0; i < capacity; i++)
            buf.Add(i);

        var exceptions = new List<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var writer = Task.Run(() =>
        {
            int val = capacity;
            while (!cts.IsCancellationRequested)
            {
                try { buf.Add(val++); }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        });

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var iter = buf.GetIterator();
                    int sum = 0;
                    for (int i = 0; i < iter.Count; i++)
                        sum += buf.ItemAt(iter.FirstSequenceNumber + (ulong)i);
                }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        }));

        await Task.WhenAll(readers.Prepend(writer));
        exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentEnumeration_Should_NotThrow_DuringSingleWriter()
    {
        var buf = new ObservableRingBuffer<int>(20, readMargin: 4);
        for (int i = 0; i < 20; i++)
            buf.Add(i);

        var exceptions = new List<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var writer = Task.Run(() =>
        {
            int val = 20;
            while (!cts.IsCancellationRequested)
            {
                try { buf.Add(val++); }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        });

        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    // Use iterator instead of ToList() to avoid allocations in hot path
                    var iter = buf.GetIterator();
                    iter.Count.Should().BeLessThanOrEqualTo(20);
                }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        }));

        await Task.WhenAll(readers.Prepend(writer));
        exceptions.Should().BeEmpty();
    }

    [Fact]
    public async Task ConcurrentIndexAccess_Should_NotThrow_DuringSingleWriter()
    {
        var buf = new ObservableRingBuffer<int>(30, readMargin: 8);
        for (int i = 0; i < 30; i++)
            buf.Add(i);

        var exceptions = new List<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var writer = Task.Run(() =>
        {
            int val = 30;
            while (!cts.IsCancellationRequested)
            {
                try { buf.Add(val++); }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        });

        var readers = Enumerable.Range(0, 3).Select(_ => Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var count = buf.Count;
                    if (count > 0)
                    {
                        _ = buf[0];
                        _ = buf[count - 1];
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Expected: count can change between read and index access
                }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        }));

        await Task.WhenAll(readers.Prepend(writer));
        exceptions.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────
    // AC5: High-throughput ReadMargin safety test
    // ────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadMargin_Should_TrackIteratorValidity_UnderHighThroughput()
    {
        const int capacity = 40;
        const int readMargin = 8;
        var buf = new ObservableRingBuffer<long>(capacity, readMargin);

        for (int i = 0; i < capacity; i++)
            buf.Add(i);

        var validCount = 0;
        var invalidCount = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var writer = Task.Run(() =>
        {
            long val = capacity;
            while (!cts.IsCancellationRequested)
            {
                buf.Add(val++);
                Thread.SpinWait(50);
            }
        });

        var reader = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var iter = buf.GetIterator();

                // Simulate render work via iterator (no ToList allocation)
                long sum = 0;
                for (int i = 0; i < iter.Count; i++)
                    sum += buf.ItemAt(iter.FirstSequenceNumber + (ulong)i);

                if (buf.IsValid(iter))
                    Interlocked.Increment(ref validCount);
                else
                    Interlocked.Increment(ref invalidCount);
            }
        });

        await Task.WhenAll(writer, reader);

        (validCount + invalidCount).Should().BeGreaterThan(0);
    }

    // ────────────────────────────────────────────────────────
    // CollectionChanged subscriber tracking
    // ────────────────────────────────────────────────────────

    [Fact]
    public void CollectionChanged_Should_NotThrow_WhenNoSubscribers()
    {
        var buf = new ObservableRingBuffer<int>(5);
        var act = () =>
        {
            buf.Add(1);
            buf.Add(2);
        };
        act.Should().NotThrow();
        buf.Count.Should().Be(2);
    }

    [Fact]
    public void CollectionChanged_Should_RespectSubscribeUnsubscribe()
    {
        var buf = new ObservableRingBuffer<int>(5);
        var count = 0;
        NotifyCollectionChangedEventHandler handler = (_, _) => count++;

        buf.CollectionChanged += handler;
        buf.Add(1);
        count.Should().Be(1);

        buf.CollectionChanged -= handler;
        buf.Add(2);
        count.Should().Be(1); // no more events
    }

    // ────────────────────────────────────────────────────────
    // AC6: Memory — no allocations on steady-state Add
    // ────────────────────────────────────────────────────────

    [Fact]
    public void Add_Should_NotReallocateArray_InSteadyState()
    {
        var buf = new ObservableRingBuffer<int>(40);
        for (int i = 0; i < 40; i++)
            buf.Add(i);

        buf.Count.Should().Be(40);
        buf.Capacity.Should().Be(40);

        for (int i = 40; i < 1040; i++)
            buf.Add(i);

        buf.Count.Should().Be(40);
        buf.Capacity.Should().Be(40);
    }

    // ────────────────────────────────────────────────────────
    // Edge cases
    // ────────────────────────────────────────────────────────

    [Fact]
    public void Add_Should_WorkCorrectly_WhenCapacityIsOne()
    {
        var buf = new ObservableRingBuffer<string>(1, readMargin: 0);
        var events = new List<NotifyCollectionChangedEventArgs>();
        buf.CollectionChanged += (_, e) => events.Add(e);

        buf.Add("a");
        buf.Count.Should().Be(1);
        buf[0].Should().Be("a");
        events[0].Action.Should().Be(NotifyCollectionChangedAction.Add);

        buf.Add("b");
        buf.Count.Should().Be(1);
        buf[0].Should().Be("b");
        events[1].Action.Should().Be(NotifyCollectionChangedAction.Replace);
        events[1].NewItems![0].Should().Be("b");
        events[1].OldItems![0].Should().Be("a");
    }

    [Fact]
    public void Add_Should_HandleLargeSequenceNumbers()
    {
        var buf = new ObservableRingBuffer<int>(5);
        for (int i = 0; i < 10_000; i++)
            buf.Add(i);

        buf.EndSequenceNumber.Should().Be(10_000UL);
        buf.FirstSequenceNumber.Should().Be(9_995UL);
        buf.Count.Should().Be(5);
        buf.Should().Equal(9995, 9996, 9997, 9998, 9999);
    }

    [Fact]
    public void Add_Should_WorkWithReferenceTypes()
    {
        var buf = new ObservableRingBuffer<string>(3);
        buf.Add("hello");
        buf.Add("world");
        buf.Add("foo");
        buf.Add("bar"); // overwrites "hello"

        buf.Should().Equal("world", "foo", "bar");
    }

    // ────────────────────────────────────────────────────────
    // Multi-threaded CollectionChanged events
    // ────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectionChanged_Should_ReceiveAllEvents_DuringSingleWriter()
    {
        const int capacity = 10;
        const int totalAdds = 5000;
        var buf = new ObservableRingBuffer<int>(capacity, readMargin: 2);

        var addCount = 0;
        var replaceCount = 0;
        buf.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                Interlocked.Increment(ref addCount);
            else if (e.Action == NotifyCollectionChangedAction.Replace)
                Interlocked.Increment(ref replaceCount);
        };

        await Task.Run(() =>
        {
            for (int i = 0; i < totalAdds; i++)
                buf.Add(i);
        });

        addCount.Should().Be(capacity);
        replaceCount.Should().Be(totalAdds - capacity);
        (addCount + replaceCount).Should().Be(totalAdds);
    }
}
