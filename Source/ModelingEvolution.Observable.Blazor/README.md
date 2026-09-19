# Minimal MVVM for Blazor
- Observable
- ObservableForEach
## ObservableForEach: keeping item identity (`Key`)

By default `ObservableForEach` renders its loop body unkeyed, so Blazor matches items by
**position**. If the collection slides — items dropped at the front, appended at the back — every
surviving item lands at a new index and its subtree is re-hosted onto a different item. In a browser
that tears down and rebuilds the whole list: a text selection inside a surviving row is lost and
keyboard focus drops to the document body.

Set `Key` to match items by **identity** instead:

```razor
<ObservableForEach ItemSource="@Vm.Messages" Key="@(m => m.Id)" IsNotifyPropertyChangedEnabled="true">
    <MessageCard Model="@context" />
</ObservableForEach>
```

- Survivors keep their component instance and DOM nodes across inserts, removals, moves, a snapshot
  swap and a filter change; only items that actually entered or left are created or disposed.
- The keys **must be unique among the rendered items** (after `Filter`). Blazor throws
  `InvalidOperationException` if two rendered siblings have the same key — which is why `Key` is
  opt-in: a collection may legitimately hold the same instance, or equal value-type items, twice.
- Leaving `Key` unset renders exactly as before.
- A `@key` written **inside** the child content does not work: an invoked `RenderFragment<TItem>` is
  emitted inside a render-tree region, and Blazor only matches keys among direct siblings, so such a
  key never matches across a positional shift. The key has to be applied by `ObservableForEach`.
- `ObservableCollection.Clear()` raises `Reset`, which renders an empty list and disposes every item
  before the refill arrives; no key survives that. To refresh wholesale while keeping identity, hand
  `ItemSource` a new collection instead of clearing in place.
