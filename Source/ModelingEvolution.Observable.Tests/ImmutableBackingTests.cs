using System.Collections.Specialized;
using FluentAssertions;
using Xunit;

namespace ModelingEvolution.Observable.Tests;

/// <summary>
/// Epic 084 defect B: enumeration through ANY static type must never throw under concurrent
/// mutation. The Collection&lt;T&gt;-based predecessor only protected foreach over the concrete
/// type; MudTable/LINQ hold IEnumerable&lt;T&gt; and hit the unlocked List&lt;T&gt; enumerator.
/// </summary>
public class ImmutableBackingTests
{
    private static void HammerReaders(IEnumerable<int> view, Action mutate, int rounds = 300, bool linqConsistent = true)
    {
        var stop = false;
        var writer = Task.Run(() =>
        {
            while (!Volatile.Read(ref stop)) mutate();
        });
        try
        {
            for (int round = 0; round < rounds; round++)
            {
                var viaForeach = new List<int>();
                foreach (var x in view) viaForeach.Add(x);   // razor @foreach over IEnumerable<T>
                viaForeach.Should().OnlyHaveUniqueItems("a snapshot can never read one slot twice");

                var viaLinq = view.ToList();                 // ICollection<T>: Count + CopyTo protocol
                if (linqConsistent) viaLinq.Should().OnlyHaveUniqueItems();
                viaLinq.Should().NotContain(0, "a shrink race must not leave default(T) tails");
            }
        }
        finally
        {
            Volatile.Write(ref stop, true);
            writer.Wait();
        }
    }

    [Fact]
    public void Enumeration_via_IEnumerable_never_throws_under_concurrent_mutation()
    {
        var sut = new ObservableCollection<int>(Enumerable.Range(1, 100));
        int n = 101;
        HammerReaders(sut, () =>
        {
            sut.Insert(0, n++);
            if (sut.Count > 2000) sut.RemoveAt(sut.Count - 1);
        });
    }

    [Fact]
    public void Control_System_ObservableCollection_DOES_throw_on_the_same_race()
    {
        // Proves the harness detects the defect — if this stops throwing, the test above is not
        // evidence of anything.
        var sut = new System.Collections.ObjectModel.ObservableCollection<int>(Enumerable.Range(1, 100));
        int n = 101;
        var act = () => HammerReaders(sut, () =>
        {
            sut.Insert(0, n++);
            if (sut.Count > 2000) sut.RemoveAt(sut.Count - 1);
        }, rounds: 2000);
        // foreach → "Collection was modified"; ToList → ArgumentException from CopyTo or a torn copy.
        act.Should().Throw<Exception>();
    }

    // Reference-type source: the view's Merge() identifies items by source REFERENCE (as ERP read
    // model rows are); boxed value types would never match and re-insert on every sync.
    private sealed record Row(int Id);
    private sealed record Box(Row Source) : IViewFor<Row>;

    [Fact]
    public void View_enumeration_via_IEnumerable_never_throws_under_concurrent_mutation()
    {
        var source = new ObservableCollection<Row>(Enumerable.Range(1, 100).Select(i => new Row(i)));
        var view = new ObservableCollectionView<Box, Row>(x => new Box(x), source);
        int n = 101;
        // Select over an IList<T> uses the Count-then-indexer protocol: never throws, never yields
        // default tails, but MAY be torn across two snapshots until the next notification.
        HammerReaders(view.Select(b => b.Source.Id), () =>
        {
            source.Insert(0, new Row(n++));
            if (source.Count > 2000) source.RemoveAt(source.Count - 1);
        }, linqConsistent: false);
    }

    [Fact]
    public void Skip_Take_over_IList_never_throws_under_a_shrinking_writer()
    {
        // MudTable pages with Skip/Take; over IList<T> that is Count + indexer per MoveNext.
        var sut = new ObservableCollection<int>(Enumerable.Range(1, 500));
        var stop = false;
        var writer = Task.Run(() =>
        {
            int n = 501;
            while (!Volatile.Read(ref stop))
            {
                while (sut.Count > 5) sut.RemoveAt(sut.Count - 1);
                for (int i = 0; i < 500; i++) sut.Add(n++);
            }
        });
        try
        {
            for (int round = 0; round < 500; round++)
            {
                var page = sut.Skip(100).Take(50).ToList();
                page.Should().NotContain(0);
            }
        }
        finally { Volatile.Write(ref stop, true); writer.Wait(); }
    }

    [Fact]
    public void ToList_during_a_grow_race_returns_the_snapshot_Count_reported()
    {
        // Simulate LINQ's protocol by hand with a mutation wedged between Count and CopyTo.
        var sut = new ObservableCollection<int>(Enumerable.Range(1, 10));
        int n = sut.Count;
        sut.Insert(0, 99);
        var buf = new int[n];
        sut.CopyTo(buf, 0);
        buf.Should().Equal(Enumerable.Range(1, 10), "CopyTo serves the snapshot Count came from");
        sut.ToList().Should().Equal(new[] { 99 }.Concat(Enumerable.Range(1, 10)));
    }

    [Fact]
    public void Events_are_raised_in_the_order_trees_are_published()
    {
        var sut = new ObservableCollection<int>();
        var seenCounts = new List<int>();
        sut.CollectionChanged += (s, e) => seenCounts.Add(((ObservableCollection<int>)s!).Count);
        Parallel.For(0, 500, i => sut.Add(i));
        // Every handler observed the count as of its own mutation → strictly increasing.
        seenCounts.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        seenCounts.Should().HaveCount(500);
    }

    [Fact]
    public void Indexer_Count_IndexOf_Contains_CopyTo_read_one_consistent_snapshot()
    {
        var sut = new ObservableCollection<int>(Enumerable.Range(0, 10));
        sut[3].Should().Be(3);
        sut.IndexOf(7).Should().Be(7);
        sut.Contains(9).Should().BeTrue();
        var arr = new int[10];
        sut.CopyTo(arr, 0);
        arr.Should().Equal(Enumerable.Range(0, 10));
        sut[3] = 33;
        sut[3].Should().Be(33);
        sut.Move(0, 9);
        sut.Should().Equal(1, 2, 33, 4, 5, 6, 7, 8, 9, 0);
    }

    [Fact]
    public void Reset_Remove_Replace_Move_raise_correct_args()
    {
        var sut = new ObservableCollection<string>(["a", "b", "c"]);
        var actions = new List<NotifyCollectionChangedAction>();
        sut.CollectionChanged += (_, e) => actions.Add(e.Action);
        sut.Remove("b"); sut[0] = "z"; sut.Move(0, 1); sut.Add("q"); sut.Clear();
        actions.Should().Equal(
            NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Replace,
            NotifyCollectionChangedAction.Move, NotifyCollectionChangedAction.Add,
            NotifyCollectionChangedAction.Reset);
        sut.Should().BeEmpty();
    }
}
