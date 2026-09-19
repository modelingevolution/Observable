using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using ModelingEvolution.Observable.Blazor;
using Xunit.Abstractions;

namespace ModelingEvolution.Observable.Tests;

/// <summary>
/// What `Key` costs in allocation, and specifically whether a VALUE-TYPE key boxes.
///
/// `Key` is `Func&lt;TItem, object?&gt;`, so an int, a Guid or a tuple key is boxed once per item per
/// render. Nobody had measured that, so these tests measure it rather than reason about it.
///
/// Measurement notes, so the numbers are read for what they are:
///  - allocation is measured with GC.GetTotalAllocatedBytes(precise: true), which is process-wide,
///    so this class is serialised into its own non-parallel collection;
///  - every measurement is warmed up and repeated, and the SPREAD is reported, not one number;
///  - assertions are on the SHAPE of the result (value keys allocate more; the difference is on the
///    order of one box per item per render), never on an absolute byte count, which would make this
///    a machine-speed detector rather than a test;
///  - this is bUnit, not a browser. It measures the renderer and the diff, not layout or paint.
/// </summary>
[Collection(nameof(AllocationMeasurements))]
public class ObservableForEachKeyAllocationTests
{
    private const int Rows = 1000;
    private const int Runs = 5;

    private readonly ITestOutputHelper _out;

    public ObservableForEachKeyAllocationTests(ITestOutputHelper output) => _out = output;

    // ---------- measurement plumbing ----------

    private sealed record Measurement(string Name, long Min, long Max, long Median)
    {
        public override string ToString()
            => $"{Name,-34} median {Median,10:N0} B   min {Min,10:N0}   max {Max,10:N0}   spread {Max - Min,9:N0}";
    }

    private static Measurement Measure(string name, Action action)
    {
        action();               // warm-up: JIT, first-render caches, AngleSharp parser tables
        action();

        var samples = new List<long>();
        for (var i = 0; i < Runs; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetTotalAllocatedBytes(precise: true);
            action();
            samples.Add(GC.GetTotalAllocatedBytes(precise: true) - before);
        }
        samples.Sort();
        return new Measurement(name, samples[0], samples[^1], samples[Runs / 2]);
    }

    private static ObservableCollection<CardModel> Rowset(int count)
    {
        var c = new ObservableCollection<CardModel>();
        for (var i = 0; i < count; i++) c.Add(new CardModel($"m{i}", $"message {i}"));
        return c;
    }

    private static IRenderedComponent<ObservableForEach<CardModel>> Render(
        BunitContext ctx, ObservableCollection<CardModel> src, bool inpc, Func<CardModel, object?>? key)
        => ctx.Render<ObservableForEach<CardModel>>(ps =>
        {
            ps.Add(p => p.ItemSource, src)
              .Add(p => p.IsNotifyPropertyChangedEnabled, inpc)
              .Add(p => p.ChildContent, CardFragments.Plain);
            if (key is not null) ps.Add(p => p.Key, key);
        });

    /// <summary>Allocation of ONE re-render of an already-rendered list (the steady-state cost).</summary>
    private static Measurement ReRender(string name, bool inpc, Func<CardModel, object?>? key)
    {
        using var ctx = new BunitContext();
        var src = Rowset(Rows);
        var cut = Render(ctx, src, inpc, key);
        return Measure(name, () => cut.Render(ps => ps.Add(p => p.IsNotifyPropertyChangedEnabled, inpc)));
    }

    // Key selectors. The first two hand back a reference; the last two box a value type on every call.
    private static readonly Func<CardModel, object?> ReferenceKey = m => m;
    private static readonly Func<CardModel, object?> StringKey = m => m.Id;
    private static readonly Func<CardModel, object?> IntKey = m => m.Seq;
    private static readonly Func<CardModel, object?> TupleKey = m => (m.Seq, m.Seq);
    // The workaround someone will reach for to "avoid boxing": build a string key instead.
    private static readonly Func<CardModel, object?> ComposedStringKey = m => $"{m.Seq}-{m.Seq}";

    // ---------- does a value-type key box, and does it matter? ----------

    [Theory]
    [MemberData(nameof(ObservableForEachKeyTests.Branches), MemberType = typeof(ObservableForEachKeyTests))]
    public void ValueTypeKeys_AllocateMoreThanReferenceKeys_PerReRender(bool inpc)
    {
        var reference = ReRender("reference key (the item)", inpc, ReferenceKey);
        var str = ReRender("string key (m.Id)", inpc, StringKey);
        var boxedInt = ReRender("int key (boxes)", inpc, IntKey);
        var boxedTuple = ReRender("(int,int) tuple key (boxes)", inpc, TupleKey);
        var unkeyed = ReRender("no key at all", inpc, null);
        var composed = ReRender("composed string key (workaround)", inpc, ComposedStringKey);

        foreach (var m in new[] { unkeyed, reference, str, boxedInt, boxedTuple, composed })
            _out.WriteLine(m.ToString());

        // Baseline is the STRING key, not the item key: it takes the identical code path and differs
        // in exactly one variable — whether the returned key is already a reference. (The item-key
        // measurement is reported too, but it is the noisiest of the five, so nothing is asserted
        // against it.)
        var intOverhead = boxedInt.Median - str.Median;
        var tupleOverhead = boxedTuple.Median - str.Median;
        var keyedItemOverhead = str.Median - unkeyed.Median;
        _out.WriteLine($"wrapper overhead   {keyedItemOverhead,10:N0} B over {Rows} rows = {keyedItemOverhead / (double)Rows:F1} B/row");
        _out.WriteLine($"int box overhead   {intOverhead,10:N0} B over {Rows} rows = {intOverhead / (double)Rows:F1} B/row");
        _out.WriteLine($"tuple box overhead {tupleOverhead,10:N0} B over {Rows} rows = {tupleOverhead / (double)Rows:F1} B/row");

        // Shape, not absolutes: a value-type key must cost at least one small object header per row
        // more than a reference key. 16 B/row is a deliberately conservative floor for a boxed int
        // on 64-bit (a real box is 24 B).
        intOverhead.Should().BeGreaterThan(Rows * 16,
            "an int key is boxed once per item per render");
        tupleOverhead.Should().BeGreaterThan(Rows * 16,
            "a (int,int) tuple key is boxed once per item per render");

        // A string key hands back an existing reference, so it must not carry the boxing penalty.
        // Pin the advice the README gives, so it cannot drift from the truth: composing a string key
        // to dodge the box allocates MORE than accepting the box, because it allocates a fresh
        // string per row per render instead of a 24-byte box.
        (composed.Median - str.Median).Should().BeGreaterThan(tupleOverhead,
            "building a string key per row costs more than the box it was meant to avoid");

        (boxedInt.Median - boxedTuple.Median).Should().BeInRange(-Rows, Rows,
            "an (int,int) tuple boxes into the same 24-byte object an int does — the README's " +
            "composite-key advice costs no more than a plain int key");
    }

    [Theory]
    [MemberData(nameof(ObservableForEachKeyTests.Branches), MemberType = typeof(ObservableForEachKeyTests))]
    public void TheUpdatePath_KeyedVersusUnkeyed_Slide(bool inpc)
    {
        // The README claims keying buys back its cost on updates. Measure the update it claims:
        // slide a 1000-row window by 40.
        var next = Rows;

        Measurement SlideOf(string name, Func<CardModel, object?>? key)
        {
            using var ctx = new BunitContext();
            var src = Rowset(Rows);
            var cut = Render(ctx, src, inpc, key);
            return Measure(name, () => cut.InvokeAsync(() =>
            {
                for (var i = 0; i < 40; i++) src.RemoveAt(0);
                for (var i = 0; i < 40; i++) { src.Add(new CardModel($"m{next}", $"message {next}")); next++; }
            }).GetAwaiter().GetResult());
        }

        var unkeyed = SlideOf("slide 40/1000 unkeyed", null);
        var keyedRef = SlideOf("slide 40/1000 keyed (reference)", ReferenceKey);
        var keyedInt = SlideOf("slide 40/1000 keyed (int, boxes)", IntKey);

        foreach (var m in new[] { unkeyed, keyedRef, keyedInt })
            _out.WriteLine(m.ToString());
        _out.WriteLine($"keyed(reference) / unkeyed = {keyedRef.Median / (double)unkeyed.Median:F2}x");
        _out.WriteLine($"keyed(int)       / unkeyed = {keyedInt.Median / (double)unkeyed.Median:F2}x");

        // Reported, not asserted as a ratio: an allocation ratio on the update path is exactly the
        // kind of number that differs between machines. The only thing asserted is that the boxing
        // penalty is present here too, which is a property of the code, not of the machine.
        (keyedInt.Median - keyedRef.Median).Should().BeGreaterThan(0,
            "the int key is boxed on every render, including the ones that update the list");
    }
}

[CollectionDefinition(nameof(AllocationMeasurements), DisableParallelization = true)]
public class AllocationMeasurements;
