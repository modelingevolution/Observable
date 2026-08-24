using System.Collections.Specialized;
using FluentAssertions;
using Xunit;

namespace ModelingEvolution.Observable.Tests;

/// <summary>
/// Regression tests for <see cref="ObservableCollectionView{T}"/>'s handling of
/// <see cref="NotifyCollectionChangedAction.Replace"/> on an UNSORTED view.
///
/// <para>The defect these pin: the unsorted branch located the row to update with
/// <c>_filtered.IndexOf(newItem)</c> — it searched the view for the NEW value, which by construction is
/// not in the view yet, so the lookup missed and the update was silently dropped. The sorted branch
/// always did the right thing (<c>IndexOf(oldItem)</c>), so the failure only ever showed on views with
/// no comparer, and only as "the UI stopped updating" — never as an exception.</para>
///
/// <para>An in-place update of an immutable-record-shaped row (<c>list[i] = row with { Field = x }</c>)
/// is the ONLY way such a row can change, so on an unsorted view bound through <c>ObservableForEach</c>
/// every one of those updates was lost. Found during the erp Fleet bench-job review (2026-08-24);
/// affects the erp pages ProductTypes.razor, ProductTypeDetail.razor and WebsiteSkus.razor, all of
/// which bind unsorted views over record rows.</para>
/// </summary>
public class ObservableCollectionViewReplaceTests
{
    /// <summary>A row shaped the way a read model's projection rows are: an immutable record, updated by
    /// replacing it in the source list. Value equality is what makes the old lookup miss.</summary>
    private sealed record Row(int Id, string Name, bool Active);

    private static ObservableCollection<Row> SourceOf(params Row[] rows) => new(rows.ToList());

    // ── Unfiltered, unsorted: the plain "a row changed" case ──────────────────────────────────

    [Fact]
    public void Replace_on_an_unsorted_view_updates_the_row()
    {
        var src = SourceOf(new Row(1, "a", true), new Row(2, "b", true), new Row(3, "c", true));
        using var view = new ObservableCollectionView<Row>(src);

        src[1] = src[1] with { Name = "CHANGED" };

        view.Should().HaveCount(3, "a replace swaps a row, it does not add or remove one");
        view[1].Name.Should().Be("CHANGED",
            "the view looked the row up by the NEW value — which is not in the view yet — so the update was dropped");
        view.Select(r => r.Id).Should().Equal(new[] { 1, 2, 3 }, "a replace must not reorder an unsorted view");
    }

    [Fact]
    public void Replace_on_an_unsorted_view_raises_CollectionChanged()
    {
        var src = SourceOf(new Row(1, "a", true));
        using var view = new ObservableCollectionView<Row>(src);

        var raised = 0;
        ((INotifyCollectionChanged)view).CollectionChanged += (_, _) => raised++;

        src[0] = src[0] with { Name = "CHANGED" };

        raised.Should().BeGreaterThan(0,
            "a bound ObservableForEach re-renders off this notification — no notification is a frozen UI");
    }

    [Fact]
    public void Replace_of_the_first_and_last_rows_lands_on_the_right_rows()
    {
        var src = SourceOf(new Row(1, "a", true), new Row(2, "b", true), new Row(3, "c", true));
        using var view = new ObservableCollectionView<Row>(src);

        src[0] = src[0] with { Name = "FIRST" };
        src[2] = src[2] with { Name = "LAST" };

        view.Select(r => r.Name).Should().Equal("FIRST", "b", "LAST");
    }

    // ── Filtered, unsorted: position must be preserved, and filter membership respected ────────

    [Fact]
    public void Replace_on_a_filtered_unsorted_view_updates_in_place_and_keeps_position()
    {
        var src = SourceOf(
            new Row(1, "a", true),
            new Row(2, "hidden", false),
            new Row(3, "c", true));
        using var view = new ObservableCollectionView<Row>(src) { Filter = r => r.Active };
        view.Select(r => r.Id).Should().Equal(1, 3);

        src[2] = src[2] with { Name = "CHANGED" };

        view.Select(r => r.Id).Should().Equal(new[] { 1, 3 },
            "the view index (1) and the source index (2) differ under a filter, so the row must be found by "
            + "identity rather than by the source's index");
        view[1].Name.Should().Be("CHANGED");
    }

    [Fact]
    public void A_replaced_row_that_no_longer_passes_the_filter_leaves_the_view()
    {
        var src = SourceOf(new Row(1, "a", true), new Row(2, "b", true));
        using var view = new ObservableCollectionView<Row>(src) { Filter = r => r.Active };
        view.Should().HaveCount(2);

        src[0] = src[0] with { Active = false };

        view.Select(r => r.Id).Should().Equal(new[] { 2 },
            "leaving a row that no longer matches on screen is the same class of staleness as dropping an update");
    }

    [Fact]
    public void A_replaced_row_that_newly_passes_the_filter_enters_the_view()
    {
        var src = SourceOf(new Row(1, "a", false), new Row(2, "b", true));
        using var view = new ObservableCollectionView<Row>(src) { Filter = r => r.Active };
        view.Select(r => r.Id).Should().Equal(2);

        src[0] = src[0] with { Active = true };

        view.Select(r => r.Id).Should().BeEquivalentTo(new[] { 1, 2 },
            "a row that starts matching the filter has to appear, exactly as the sorted branch makes it appear");
    }

    // ── Reference-typed rows: identity, not value equality ────────────────────────────────────

    [Fact]
    public void Replace_of_a_reference_typed_row_with_a_fresh_instance_updates_the_view()
    {
        var a = new Mutable { Id = 1, Name = "a" };
        var b = new Mutable { Id = 2, Name = "b" };
        var src = new ObservableCollection<Mutable>(new List<Mutable> { a, b });
        using var view = new ObservableCollectionView<Mutable>(src);

        var replacement = new Mutable { Id = 2, Name = "CHANGED" };
        src[1] = replacement;

        view.Should().HaveCount(2);
        ReferenceEquals(view[1], replacement).Should().BeTrue(
            "reference-typed rows have no value equality at all, so IndexOf(newItem) could never have found one");
    }

    private sealed class Mutable
    {
        public int Id { get; init; }
        public string Name { get; set; } = "";
    }

    // ── The sorted branch was always correct — pin it so the fix does not disturb it ───────────

    [Fact]
    public void Replace_on_a_sorted_view_still_repositions_the_row()
    {
        var src = SourceOf(new Row(1, "a", true), new Row(2, "b", true), new Row(3, "c", true));
        using var view = new ObservableCollectionView<Row>(src);
        view.SortBy(r => r.Name);
        view.Select(r => r.Name).Should().Equal("a", "b", "c");

        src[0] = src[0] with { Name = "z" };

        view.Select(r => r.Name).Should().Equal(new[] { "b", "c", "z" },
            "the sorted branch removes the old row and re-inserts the new one at its sorted position");
    }

    [Fact]
    public void Replace_on_a_sorted_filtered_view_still_honours_the_filter()
    {
        var src = SourceOf(new Row(1, "a", true), new Row(2, "b", true));
        using var view = new ObservableCollectionView<Row>(src) { Filter = r => r.Active };
        view.SortBy(r => r.Name);

        src[0] = src[0] with { Active = false };

        view.Select(r => r.Id).Should().Equal(new[] { 2 });
    }
}
