using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;

namespace ModelingEvolution.Observable.Tests;

/// <summary>
/// An item that a consumer would put in an <see cref="ObservableCollection{T}"/>. It implements
/// INotifyPropertyChanged so it can be used with ObservableForEach's IsNotifyPropertyChangedEnabled
/// branch, and it counts its live PropertyChanged subscribers so tests can prove unsubscription.
/// </summary>
public sealed class CardModel : INotifyPropertyChanged
{
    private string _text;

    public CardModel(string id, string text)
    {
        Id = id;
        _text = text;
    }

    public string Id { get; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Number of live PropertyChanged handlers — proves subscribe/unsubscribe symmetry.</summary>
    public int SubscriberCount => _propertyChanged?.GetInvocationList().Length ?? 0;

    private PropertyChangedEventHandler? _propertyChanged;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _propertyChanged += value;
        remove => _propertyChanged -= value;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => _propertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public override string ToString() => Id;
}

/// <summary>
/// The component a consumer renders per item. It records how many times it was constructed and
/// initialised, and it stamps its own instance id into the DOM so a test can tell "same instance,
/// moved" from "torn down and rebuilt".
/// </summary>
public sealed class Card : ComponentBase, IDisposable
{
    private static int _nextInstanceId;

    public static int TotalInitialized;
    public static int TotalDisposed;

    public static void ResetCounters()
    {
        TotalInitialized = 0;
        TotalDisposed = 0;
    }

    public int InstanceId { get; } = Interlocked.Increment(ref _nextInstanceId);

    [Parameter] public CardModel Model { get; set; } = default!;

    protected override void OnInitialized() => Interlocked.Increment(ref TotalInitialized);

    public void Dispose() => Interlocked.Increment(ref TotalDisposed);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "card");
        builder.AddAttribute(2, "data-id", Model.Id);
        builder.AddAttribute(3, "data-instance", InstanceId.ToString());
        builder.AddContent(4, Model.Text);
        builder.CloseElement();
    }
}

/// <summary>Shapes of consumer ChildContent used across the keying tests.</summary>
public static class CardFragments
{
    /// <summary>A plain, unkeyed consumer fragment — one Card component per item.</summary>
    public static RenderFragment<CardModel> Plain { get; } = item => builder =>
    {
        builder.OpenComponent<Card>(0);
        builder.AddComponentParameter(1, nameof(Card.Model), item);
        builder.CloseComponent();
    };

    /// <summary>
    /// A consumer fragment that puts its OWN @key on its component — the thing a consumer would try
    /// first, and which the router measured does not preserve anything across a slide.
    /// </summary>
    public static RenderFragment<CardModel> WithConsumerKey { get; } = item => builder =>
    {
        builder.OpenComponent<Card>(0);
        builder.SetKey(item.Id);
        builder.AddComponentParameter(1, nameof(Card.Model), item);
        builder.CloseComponent();
    };
}

/// <summary>
/// CONTROL. Renders the same Cards in a bare keyed @foreach — no ObservableForEach, no
/// RenderFragment indirection, the key sits on the loop's direct child. If the test harness can
/// see identity preservation at all, it sees it here. Without this control, a "0 survivors
/// preserved" result could just mean the harness cannot observe preservation.
/// </summary>
public sealed class KeyedLoopControl : ComponentBase
{
    [Parameter] public IReadOnlyList<CardModel> Items { get; set; } = Array.Empty<CardModel>();

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        foreach (var item in Items)
        {
            builder.OpenComponent<Card>(0);
            builder.SetKey(item.Id);
            builder.AddComponentParameter(1, nameof(Card.Model), item);
            builder.CloseComponent();
        }
    }
}
