using System.Collections.Specialized;
using FluentAssertions;
using Xunit;

namespace ModelingEvolution.Observable.Tests;

/// <summary>
/// Regression tests for the 2026-08-11 ERP UI-composition review findings F5.1–F5.3
/// (docs/reviews/ui-composition-review-2026-08-11.md in the erp repo). F1 (ObservableForEach
/// must dispose its CollectionChanged subscription) lives in the Blazor package and has no
/// render harness here — it is verified by the component's Dispose implementation and the
/// consuming app's page tests.
/// </summary>
public class ReviewFindingsRegressionTests
{
    // ── F5.1 — the concrete-type enumerator is a SNAPSHOT ────────────────────────────────

    [Fact]
    public void Enumeration_is_a_snapshot_a_concurrent_insert_cannot_duplicate_rows()
    {
        var sut = new ObservableCollection<int>(Enumerable.Range(0, 1000).ToList());

        // Start enumerating, then mutate mid-flight: a snapshot must yield EXACTLY the
        // 1000 original elements — the per-element-lock version duplicated the element at
        // the cursor when an insert landed before it.
        var seen = new List<int>();
        var e = sut.GetEnumerator();
        for (int i = 0; i < 500 && e.MoveNext(); i++) seen.Add(e.Current);
        sut.Insert(0, -1);                       // shifts everything right in the LIVE list
        while (e.MoveNext()) seen.Add(e.Current);

        seen.Should().HaveCount(1000);
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(Enumerable.Range(0, 1000));
    }

    [Fact]
    public void Enumeration_races_a_writer_without_tearing()
    {
        var sut = new ObservableCollection<int>(Enumerable.Range(0, 100).ToList());
        var stop = false;
        var writer = Task.Run(() =>
        {
            var n = 100;
            while (!Volatile.Read(ref stop))
            {
                sut.Insert(0, n++);
                if (sut.Count > 5000) sut.RemoveAt(0);
            }
        });

        for (int round = 0; round < 200; round++)
        {
            var copy = new List<int>();
            foreach (var x in sut) copy.Add(x);       // concrete foreach → snapshot path
            copy.Should().OnlyHaveUniqueItems("a snapshot can never read one slot twice");
        }
        Volatile.Write(ref stop, true);
        writer.Wait();
    }

    // ── F5.2 — For<T> no longer swallows arbitrary exceptions ────────────────────────────

    private sealed class ThrowingList : IReadOnlyList<int>
    {
        public int this[int index] => throw new InvalidOperationException("indexer bug");
        public int Count => 3;
        public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class ShrinkingList : IReadOnlyList<int>
    {
        // Reports 3 elements but only serves index 0 — models a concurrent shrink.
        public int this[int index] => index == 0 ? 42 : throw new ArgumentOutOfRangeException(nameof(index));
        public int Count => 3;
        public IEnumerator<int> GetEnumerator() => throw new NotSupportedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void For_propagates_a_real_indexer_failure_instead_of_truncating_silently()
    {
        var act = () => new ThrowingList().For(null).ToList();
        act.Should().Throw<InvalidOperationException>(
            "a genuine failure must surface, not render a shorter table with no error");
    }

    [Fact]
    public void For_still_stops_gracefully_on_the_concurrent_shrink_race()
    {
        var result = new ShrinkingList().For(null).ToList();
        result.Should().Equal(new[] { 42 }, "the documented graceful stop on the index race is preserved");
    }

    // ── F5.3 — CollectionChanged add/remove is atomic ────────────────────────────────────

    [Fact]
    public void Concurrent_subscribes_lose_no_handler()
    {
        var sut = new ObservableCollection<int>();
        var hits = 0;
        const int n = 64;
        var handlers = Enumerable.Range(0, n)
            .Select(_ => new NotifyCollectionChangedEventHandler((_, _) => Interlocked.Increment(ref hits)))
            .ToArray();

        Parallel.ForEach(handlers, h => sut.CollectionChanged += h);
        sut.Add(1);
        hits.Should().Be(n, "a lost delegate is a page that silently stops updating");

        hits = 0;
        Parallel.ForEach(handlers, h => sut.CollectionChanged -= h);
        sut.Add(2);
        hits.Should().Be(0, "an unsubscribe that never took effect compounds the F1 leak");
    }
}
