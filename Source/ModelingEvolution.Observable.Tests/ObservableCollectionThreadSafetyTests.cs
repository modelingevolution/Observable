using System.Collections.Specialized;

namespace ModelingEvolution.Observable.Tests;

public class ObservableCollectionThreadSafetyTests
{
    [Fact]
    public void ConcurrentInserts_ShouldNotThrow()
    {
        var collection = new ObservableCollection<int>();
        var exceptions = new List<Exception>();

        Parallel.For(0, 1000, i =>
        {
            try
            {
                collection.Add(i);
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(1000, collection.Count);
    }

    [Fact]
    public void ConcurrentInsertSorted_ShouldNotThrow()
    {
        var collection = new ObservableCollection<int>();
        var exceptions = new List<Exception>();

        Parallel.For(0, 1000, i =>
        {
            try
            {
                collection.InsertSorted(i);
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Equal(1000, collection.Count);

        // Verify sorted order
        for (int i = 1; i < collection.Count; i++)
            Assert.True(collection[i - 1] <= collection[i],
                $"Collection not sorted at index {i}: {collection[i - 1]} > {collection[i]}");
    }

    [Fact]
    public void ConcurrentInsertAndRemove_ShouldNotThrow()
    {
        var collection = new ObservableCollection<int>();
        // Pre-fill so removals have something to work with
        for (int i = 0; i < 500; i++) collection.Add(i);

        var exceptions = new List<Exception>();

        Parallel.For(0, 1000, i =>
        {
            try
            {
                if (i % 2 == 0)
                {
                    collection.Add(i);
                }
                else
                {
                    // RemoveAt can throw ArgumentOutOfRangeException if another
                    // thread removed the last item between our Count check and
                    // the actual removal. That's expected — just swallow it.
                    try
                    {
                        if (collection.Count > 0)
                            collection.RemoveAt(0);
                    }
                    catch (ArgumentOutOfRangeException) { }
                }
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task ConcurrentEnumeration_DuringInserts_ShouldNotThrow()
    {
        var collection = new ObservableCollection<int>();
        for (int i = 0; i < 100; i++) collection.Add(i);

        var exceptions = new List<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var writers = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { collection.Add(Random.Shared.Next()); }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        });

        var readers = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    foreach (var item in collection)
                    {
                        // Just read
                        _ = item;
                    }
                }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        });

        await Task.WhenAll(writers, readers);
        Assert.Empty(exceptions);
    }

    [Fact]
    public async Task ConcurrentBinarySearch_DuringInserts_ShouldNotThrow()
    {
        var collection = new ObservableCollection<int>();
        for (int i = 0; i < 100; i++) collection.InsertSorted(i * 2); // even numbers

        var exceptions = new List<Exception>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var writers = Task.Run(() =>
        {
            int val = 201;
            while (!cts.IsCancellationRequested)
            {
                try { collection.InsertSorted(val += 2); }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        });

        var readers = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { collection.BinarySearch(50); }
                catch (Exception ex) { lock (exceptions) exceptions.Add(ex); }
            }
        });

        await Task.WhenAll(writers, readers);
        Assert.Empty(exceptions);
    }

    [Fact]
    public void Enumerator_ReturnsAllItems()
    {
        var collection = new ObservableCollection<int> { 1, 2, 3, 4, 5 };
        var result = collection.ToList();
        Assert.Equal([1, 2, 3, 4, 5], result);
    }

    [Fact]
    public void Enumerator_EmptyCollection_ReturnsNothing()
    {
        var collection = new ObservableCollection<int>();
        Assert.Empty(collection.ToList());
    }

    [Fact]
    public void CollectionChanged_FiresOnInsert()
    {
        var collection = new ObservableCollection<int>();
        NotifyCollectionChangedEventArgs? args = null;
        collection.CollectionChanged += (_, e) => args = e;

        collection.Add(42);

        Assert.NotNull(args);
        Assert.Equal(NotifyCollectionChangedAction.Add, args.Action);
        Assert.Equal(42, args.NewItems![0]);
    }

    [Fact]
    public void CollectionChanged_FiresOnRemove()
    {
        var collection = new ObservableCollection<int> { 10, 20, 30 };
        NotifyCollectionChangedEventArgs? args = null;
        collection.CollectionChanged += (_, e) => args = e;

        collection.RemoveAt(1);

        Assert.NotNull(args);
        Assert.Equal(NotifyCollectionChangedAction.Remove, args.Action);
        Assert.Equal(20, args.OldItems![0]);
    }

    [Fact]
    public void CollectionChanged_FiresOnClear()
    {
        var collection = new ObservableCollection<int> { 1, 2, 3 };
        NotifyCollectionChangedEventArgs? args = null;
        collection.CollectionChanged += (_, e) => args = e;

        collection.Clear();

        Assert.NotNull(args);
        Assert.Equal(NotifyCollectionChangedAction.Reset, args.Action);
        Assert.Empty(collection);
    }

    [Fact]
    public void Move_FiresCollectionChanged()
    {
        var collection = new ObservableCollection<int> { 1, 2, 3 };
        NotifyCollectionChangedEventArgs? args = null;
        collection.CollectionChanged += (_, e) => args = e;

        collection.Move(0, 2);

        Assert.NotNull(args);
        Assert.Equal(NotifyCollectionChangedAction.Move, args.Action);
        Assert.Equal(1, args.NewItems![0]);
        Assert.Equal([2, 3, 1], collection.ToList());
    }

    [Fact]
    public void SetItem_FiresReplace()
    {
        var collection = new ObservableCollection<int> { 10, 20, 30 };
        NotifyCollectionChangedEventArgs? args = null;
        collection.CollectionChanged += (_, e) => args = e;

        collection[1] = 99;

        Assert.NotNull(args);
        Assert.Equal(NotifyCollectionChangedAction.Replace, args.Action);
        Assert.Equal(99, args.NewItems![0]);
        Assert.Equal(20, args.OldItems![0]);
    }

    [Fact]
    public void SubscribersAvailable_TracksSubscriberCount()
    {
        var collection = new ObservableCollection<int>();
        var transitions = new List<bool>();
        collection.SubscribersAvailable += v => transitions.Add(v);

        NotifyCollectionChangedEventHandler handler1 = (_, _) => { };
        NotifyCollectionChangedEventHandler handler2 = (_, _) => { };

        collection.CollectionChanged += handler1;  // 0→1: true
        collection.CollectionChanged += handler2;  // 1→2: no event
        collection.CollectionChanged -= handler2;  // 2→1: no event
        collection.CollectionChanged -= handler1;  // 1→0: false

        Assert.Equal([true, false], transitions);
    }

    [Fact]
    public void ConcurrentGetOrAdd_SimulatesPipelinesModelPattern()
    {
        // This reproduces the exact crash pattern from PipelinesModel.GetOrCreate:
        // ConcurrentDictionary.GetOrAdd calling factory that inserts into ObservableCollection
        var dict = new System.Collections.Concurrent.ConcurrentDictionary<int, string>();
        var collection = new ObservableCollection<string>();
        var exceptions = new List<Exception>();

        Parallel.For(0, 1000, i =>
        {
            try
            {
                dict.GetOrAdd(i % 100, key =>
                {
                    var value = $"item-{key}";
                    collection.Add(value);
                    return value;
                });
            }
            catch (Exception ex)
            {
                lock (exceptions) exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }
}
