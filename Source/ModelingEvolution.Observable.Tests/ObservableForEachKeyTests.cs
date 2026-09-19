using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using ModelingEvolution.Observable.Blazor;

namespace ModelingEvolution.Observable.Tests;

/// <summary>
/// Identity of rendered items across collection changes.
///
/// The defect: ObservableForEach renders its loop body unkeyed, so Blazor diffs the items
/// POSITIONALLY. When a sliding window drops N items off the front and appends N at the back,
/// every surviving item lands at a new index, so every component instance is recycled onto a
/// DIFFERENT item instead of being matched to the item it belongs to. In a browser this is what
/// tears down and rebuilds the whole list (selection lost, focus dropped to BODY).
///
/// A consumer cannot fix this from the outside: a @key placed inside ChildContent is proved
/// ineffective by <see cref="ConsumerKeyInsideChildContent_DoesNotPreserveInstances"/>.
/// </summary>
public class ObservableForEachKeyTests
{
    // ---------- helpers ----------

    private static ObservableCollection<CardModel> Window(int from, int count)
    {
        var c = new ObservableCollection<CardModel>();
        for (var i = from; i < from + count; i++)
            c.Add(new CardModel($"m{i}", $"message {i}"));
        return c;
    }

    private static IRenderedComponent<ObservableForEach<CardModel>> RenderList(
        BunitContext ctx,
        ObservableCollection<CardModel> source,
        bool inpc,
        RenderFragment<CardModel>? childContent = null,
        Func<CardModel, object?>? key = null,
        Predicate<CardModel>? filter = null)
        => ctx.Render<ObservableForEach<CardModel>>(ps =>
        {
            ps.Add(p => p.ItemSource, source)
              .Add(p => p.IsNotifyPropertyChangedEnabled, inpc)
              .Add(p => p.ChildContent, childContent ?? CardFragments.Plain);
            if (key is not null) ps.Add(p => p.Key, key);
            if (filter is not null) ps.Add(p => p.Filter, filter);
        });

    /// <summary>item id -&gt; the Card component instance id currently rendering it.</summary>
    private static Dictionary<string, string> InstanceByItem(IRenderedComponent<ObservableForEach<CardModel>> cut)
        => cut.FindAll("div.card")
              .ToDictionary(e => e.GetAttribute("data-id")!, e => e.GetAttribute("data-instance")!);

    private static List<string> ItemOrder(IRenderedComponent<ObservableForEach<CardModel>> cut)
        => cut.FindAll("div.card").Select(e => e.GetAttribute("data-id")!).ToList();

    /// <summary>Slide the window: drop <paramref name="n"/> from the front, append <paramref name="n"/> at the back.</summary>
    private static void Slide(ObservableCollection<CardModel> c, int n, int nextIndex)
    {
        for (var i = 0; i < n; i++) c.RemoveAt(0);
        for (var i = 0; i < n; i++) c.Add(new CardModel($"m{nextIndex + i}", $"message {nextIndex + i}"));
    }

    public static TheoryData<bool> Branches => new() { true, false };

    // ---------- RED-FIRST: the defect ----------

    [Theory]
    [MemberData(nameof(Branches))]
    public void Slide_WithoutKey_LosesEveryItemIdentity(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = Window(0, 12);
        var cut = RenderList(ctx, source, inpc);

        var before = InstanceByItem(cut);
        cut.InvokeAsync(() => Slide(source, 4, 12)).GetAwaiter().GetResult();
        var after = InstanceByItem(cut);

        var survivors = before.Keys.Intersect(after.Keys).ToList();
        survivors.Should().HaveCount(8, "the window slid by 4 of 12, so 8 items are still in the list");

        // THE DEFECT: not one surviving item is still rendered by the component instance it had.
        var preserved = survivors.Count(id => before[id] == after[id]);
        preserved.Should().Be(0, "without Key, Blazor matches positionally, so every survivor is re-hosted");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void ConsumerKeyInsideChildContent_DoesNotPreserveInstances(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = Window(0, 12);
        var cut = RenderList(ctx, source, inpc, CardFragments.WithConsumerKey);

        var before = InstanceByItem(cut);
        cut.InvokeAsync(() => Slide(source, 4, 12)).GetAwaiter().GetResult();
        var after = InstanceByItem(cut);

        var survivors = before.Keys.Intersect(after.Keys).ToList();
        var preserved = survivors.Count(id => before[id] == after[id]);

        preserved.Should().Be(0,
            "a @key the consumer puts inside ChildContent cannot match across a positional shift; " +
            "this is why the fix has to be inside ObservableForEach");
    }

    [Fact]
    public void Control_BareKeyedLoop_PreservesInstances()
    {
        // Proves the harness can observe preservation, so the "0 preserved" results above are a
        // property of ObservableForEach and not of the way these tests measure identity.
        using var ctx = new BunitContext();
        var items = new List<CardModel>();
        for (var i = 0; i < 12; i++) items.Add(new CardModel($"m{i}", $"message {i}"));

        var cut = ctx.Render<KeyedLoopControl>(ps => ps.Add(p => p.Items, items));
        var before = cut.FindAll("div.card").ToDictionary(e => e.GetAttribute("data-id")!, e => e.GetAttribute("data-instance")!);

        items = items.Skip(4).ToList();
        for (var i = 0; i < 4; i++) items.Add(new CardModel($"m{12 + i}", $"message {12 + i}"));
        cut.Render(ps => ps.Add(p => p.Items, items));

        var after = cut.FindAll("div.card").ToDictionary(e => e.GetAttribute("data-id")!, e => e.GetAttribute("data-instance")!);
        var survivors = before.Keys.Intersect(after.Keys).ToList();
        survivors.Should().HaveCount(8);
        foreach (var id in survivors)
            after[id].Should().Be(before[id], "a key on the loop's direct child preserves the instance");
    }

    // ---------- With Key: identity is preserved ----------

    [Theory]
    [MemberData(nameof(Branches))]
    public void Slide_WithKey_PreservesSurvivingInstances(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = Window(0, 12);
        var cut = RenderList(ctx, source, inpc, key: m => m.Id);

        var before = InstanceByItem(cut);
        cut.InvokeAsync(() => Slide(source, 4, 12)).GetAwaiter().GetResult();
        var after = InstanceByItem(cut);

        var survivors = before.Keys.Intersect(after.Keys).ToList();
        survivors.Should().HaveCount(8);
        foreach (var id in survivors)
            after[id].Should().Be(before[id], $"item {id} survived the slide and must keep its component instance");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void Slide_WithKey_InitialisesOnlyTheNewItems(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = Window(0, 12);
        var cut = RenderList(ctx, source, inpc, key: m => m.Id);

        Card.ResetCounters();
        cut.InvokeAsync(() => Slide(source, 4, 12)).GetAwaiter().GetResult();

        Card.TotalInitialized.Should().Be(4, "only the 4 appended items are new");
        Card.TotalDisposed.Should().Be(4, "only the 4 dropped items are gone");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void SourceReplacedByOverlappingSnapshot_WithKey_PreservesOverlap(bool inpc)
    {
        // The snapshot-swap shape: the view model hands the component a whole new list whose
        // contents overlap the old one. Every survivor also changes position.
        using var ctx = new BunitContext();
        var source = Window(0, 8);
        var cut = RenderList(ctx, source, inpc, key: m => m.Id);

        var before = InstanceByItem(cut);
        var keep = source.Skip(3).Take(5).ToList();          // m3..m7, the SAME item instances
        var next = new ObservableCollection<CardModel>();
        foreach (var m in keep) next.Add(m);
        next.Add(new CardModel("m8", "message 8"));
        next.Add(new CardModel("m9", "message 9"));

        cut.Render(ps => ps.Add(p => p.ItemSource, next));

        var after = InstanceByItem(cut);
        ItemOrder(cut).Should().Equal("m3", "m4", "m5", "m6", "m7", "m8", "m9");
        foreach (var m in keep)
            after[m.Id].Should().Be(before[m.Id], $"{m.Id} is in both the old and the new snapshot");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void ClearThenRefill_RebuildsEverything_EvenWithKey(bool inpc)
    {
        // Pinned as a fact, not a wish: ObservableCollection.Clear() raises Reset on its own, the
        // component re-renders an EMPTY list at that moment and disposes every item, and only then
        // does the refill arrive. No key can preserve an instance across a render in which the item
        // was not present. Callers who need identity across a wholesale refresh must swap the
        // ItemSource for a new snapshot (see SourceReplacedByOverlappingSnapshot_WithKey_...)
        // instead of clearing in place.
        using var ctx = new BunitContext();
        var source = Window(0, 8);
        var cut = RenderList(ctx, source, inpc, key: m => m.Id);

        var before = InstanceByItem(cut);
        var keep = source.Skip(3).Take(5).ToList();

        cut.InvokeAsync(() =>
        {
            source.Clear();
            foreach (var m in keep) source.Add(m);
        }).GetAwaiter().GetResult();

        var after = InstanceByItem(cut);
        ItemOrder(cut).Should().Equal("m3", "m4", "m5", "m6", "m7");
        after.Keys.Should().OnlyContain(id => after[id] != before[id],
            "the empty render in between tore every item down");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void Move_WithKey_PreservesInstances(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = Window(0, 5);
        var cut = RenderList(ctx, source, inpc, key: m => m.Id);

        var before = InstanceByItem(cut);
        cut.InvokeAsync(() => source.Move(0, 4)).GetAwaiter().GetResult();
        var after = InstanceByItem(cut);

        ItemOrder(cut).Should().Equal("m1", "m2", "m3", "m4", "m0");
        foreach (var id in before.Keys)
            after[id].Should().Be(before[id], $"{id} only moved");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void InsertInMiddle_WithKey_PreservesInstances(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = Window(0, 5);
        var cut = RenderList(ctx, source, inpc, key: m => m.Id);

        var before = InstanceByItem(cut);
        Card.ResetCounters();
        cut.InvokeAsync(() => source.Insert(2, new CardModel("mX", "inserted"))).GetAwaiter().GetResult();
        var after = InstanceByItem(cut);

        ItemOrder(cut).Should().Equal("m0", "m1", "mX", "m2", "m3", "m4");
        Card.TotalInitialized.Should().Be(1, "only the inserted item is new");
        foreach (var id in before.Keys)
            after[id].Should().Be(before[id], $"{id} was only shifted by the insert");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void FilterChange_WithKey_PreservesStillVisibleInstances(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = Window(0, 6);
        var cut = RenderList(ctx, source, inpc, key: m => m.Id, filter: _ => true);

        var before = InstanceByItem(cut);

        // Narrow the filter to the odd-numbered items: m1, m3, m5 stay visible but all shift position.
        cut.Render(ps => ps.Add(p => p.Filter, m => int.Parse(m.Id[1..]) % 2 == 1));

        var after = InstanceByItem(cut);
        ItemOrder(cut).Should().Equal("m1", "m3", "m5");
        foreach (var id in after.Keys)
            after[id].Should().Be(before[id], $"{id} is still visible, only at a different position");
    }

    // ---------- Key null: today's behaviour is pinned ----------

    [Theory]
    [MemberData(nameof(Branches))]
    public void KeyNull_RendersTheSameMarkupAsBefore(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = Window(0, 3);
        var cut = RenderList(ctx, source, inpc);

        // No wrapper element, no key-related attribute: exactly the three consumer divs.
        cut.FindAll("div").Should().HaveCount(3);
        ItemOrder(cut).Should().Equal("m0", "m1", "m2");
        cut.Markup.Should().NotContain("data-key");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void KeyNull_StillMatchesPositionally(bool inpc)
    {
        // The default must not silently become keyed: consumers depend on today's behaviour.
        using var ctx = new BunitContext();
        var source = Window(0, 5);
        var cut = RenderList(ctx, source, inpc);

        var before = InstanceByItem(cut);
        cut.InvokeAsync(() => source.RemoveAt(0)).GetAwaiter().GetResult();
        var after = InstanceByItem(cut);

        after["m1"].Should().Be(before["m0"],
            "with no Key, the instance that rendered m0 is recycled onto m1 — positional matching, unchanged");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void WithKey_ProducesByteIdenticalMarkupToWithoutKey(bool inpc)
    {
        // Answers the "a wrapper changes the DOM for every consumer" objection directly.
        // KeyedItem is a component whose whole body is @ChildContent: it emits no element and no
        // attribute, so turning Key on changes the render tree's KEYS, never its markup.
        using var ctx1 = new BunitContext();
        using var ctx2 = new BunitContext();
        var unkeyed = RenderList(ctx1, Window(0, 4), inpc).Markup;
        var keyed = RenderList(ctx2, Window(0, 4), inpc, key: m => m.Id).Markup;

        StripInstanceIds(keyed).Should().Be(StripInstanceIds(unkeyed),
            "setting Key must not add, remove or alter a single byte of rendered markup");
    }

    /// <summary>Component instance ids differ between two independent renders; everything else must not.</summary>
    private static string StripInstanceIds(string markup)
        => System.Text.RegularExpressions.Regex.Replace(markup, @"data-instance=""\d+""", @"data-instance=""#""");

    // ---------- Duplicate keys: documented, tested fact ----------

    [Theory]
    [MemberData(nameof(Branches))]
    public void DuplicateKeys_Throw(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = new ObservableCollection<CardModel>
        {
            new("dup", "a"),
            new("dup", "b"),
        };

        var act = () => RenderList(ctx, source, inpc, key: m => m.Id);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*more than one sibling*same key*",
                "Blazor refuses duplicate sibling keys — Key must be unique among the rendered items");
    }

    // ---------- Subscription behaviour is unchanged by Key ----------

    [Fact]
    public void WithKey_PerItemPropertyChangedStillRepaintsThatItem()
    {
        using var ctx = new BunitContext();
        var source = Window(0, 4);
        var cut = RenderList(ctx, source, inpc: true, key: m => m.Id);

        var before = InstanceByItem(cut);
        Card.ResetCounters();
        cut.InvokeAsync(() => source[2].Text = "edited").GetAwaiter().GetResult();

        cut.FindAll("div.card")[2].TextContent.Should().Be("edited");
        Card.TotalInitialized.Should().Be(0, "a per-item repaint must not rebuild any item");
        InstanceByItem(cut).Should().BeEquivalentTo(before, "no instance changed");
    }

    [Theory]
    [MemberData(nameof(Branches))]
    public void WithKey_RemovedItemsAreUnsubscribed(bool inpc)
    {
        using var ctx = new BunitContext();
        var source = Window(0, 6);
        var cut = RenderList(ctx, source, inpc, key: m => m.Id);

        var dropped = source[0];
        var kept = source[5];
        var expected = inpc ? 1 : 0;
        dropped.SubscriberCount.Should().Be(expected, "the INPC branch subscribes exactly one handler per item");

        cut.InvokeAsync(() => source.RemoveAt(0)).GetAwaiter().GetResult();

        dropped.SubscriberCount.Should().Be(0, "the removed item's subscription must be released");
        kept.SubscriberCount.Should().Be(expected, "items still in the list keep exactly one subscription");
    }

    [Fact]
    public void WithKey_SlideLeavesNoLeakedSubscriptions()
    {
        using var ctx = new BunitContext();
        var source = Window(0, 12);
        var cut = RenderList(ctx, source, inpc: true, key: m => m.Id);

        var dropped = source.Take(4).ToList();
        cut.InvokeAsync(() => Slide(source, 4, 12)).GetAwaiter().GetResult();

        dropped.Should().OnlyContain(m => m.SubscriberCount == 0, "every dropped item is unsubscribed");
        source.Should().OnlyContain(m => m.SubscriberCount == 1, "every live item has exactly one subscription");
    }
}
