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
- The keys **must be unique among the rendered items** (after `Filter`) — see
  [Duplicate keys](#duplicate-keys-an-error-naming-a-type-you-never-wrote) below.
- Leaving `Key` unset renders exactly as before.
- Cost: see [What keying costs](#what-keying-costs) — free in the notify branch, not free in the
  plain one.
- A `@key` written **inside** the child content does not work: an invoked `RenderFragment<TItem>` is
  emitted inside a render-tree region, and Blazor only matches keys among direct siblings, so such a
  key never matches across a positional shift. The key has to be applied by `ObservableForEach`.

### Duplicate keys: an error naming a type you never wrote

If two rendered items produce the same key, Blazor throws `InvalidOperationException`, and the
message names a component from **this library** rather than anything in your code:

```
More than one sibling of component 'ModelingEvolution.Observable.Blazor.KeyedItem'
has the same key value, 'dup'. Key values must be unique.
```

```
More than one sibling of component
'ModelingEvolution.Observable.Blazor.Observable`1[YourApp.RowVm]'
has the same key value, 'dup'. Key values must be unique.
```

(The first is the plain branch, the second the `IsNotifyPropertyChangedEnabled` branch.) **It is not
a bug in the library.** It means two items in *your* `ItemSource` returned equal values from your
`Key`, and the quoted key value tells you which.

Picking a key for rows that are records or structs: `record` types have **value** equality, so
`Key="@(r => r)"` — or any key built only from the displayed fields — makes two rows that happen to
hold the same data collide, and a screen that renders fine today starts throwing. Key on something
guaranteed unique per row instead:

```razor
@* good: a real identity *@
<ObservableForEach ItemSource="@Vm.Invoices" Key="@(i => i.InvoiceId)"> ... </ObservableForEach>

@* also fine: a composite that is unique by construction *@
<ObservableForEach ItemSource="@Vm.Lines" Key="@(l => (l.DocumentId, l.LineNo))"> ... </ObservableForEach>

@* bad: two lines with the same amount now collide *@
<ObservableForEach ItemSource="@Vm.Lines" Key="@(l => l.Amount)"> ... </ObservableForEach>
```

If your rows genuinely have no unique identity, leave `Key` unset — unkeyed rendering never throws.

### Do not refresh by clearing in place

This is the commonest refresh idiom, and `Key` does nothing for it:

```csharp
_items.Clear();                       // raises Reset -> renders an EMPTY list, disposes every row
foreach (var x in fresh) _items.Add(x);   // rows are rebuilt from scratch
```

`ObservableCollection.Clear()` raises `Reset` by itself. The component re-renders at that moment with
an empty list and disposes every row; only then do the re-added items arrive. No key can preserve a
row across a render in which the row was not present — so a screen that adopts `Key` while clearing
in place gets no benefit and no error telling it why.

Either assign a new collection:

```csharp
var next = new ObservableCollection<RowVm>();
foreach (var x in fresh) next.Add(x);
Items = next;              // ItemSource changes once; rows present in both keep their identity
```

or mutate the existing collection in place (add, remove, move the individual rows that changed) so no
`Reset` is raised at all. Both are covered by tests in this repo.

### What keying costs

**With `Key` unset you pay nothing.** No `KeyedItem` is ever constructed, in either branch.

**In the notify branch (`IsNotifyPropertyChangedEnabled="true"`) keying is free.** The key goes on
the `<Observable>` wrapper the component already renders, so no component is added.

**In the plain branch it is not free.** Keying adds one `KeyedItem` per row, and at a thousand rows
that is visible on first render:

| 1000 rows, first render, plain branch | time | allocations |
|---|---|---|
| unkeyed | 15 ms | 3,362 KB |
| keyed | 46 ms | 5,864 KB |

Roughly 3× the time and 1.7× the allocations on first render. The wrapper costs about **64 bytes per
row per render**; in the notify branch it costs nothing, because no component is added there.

**What it buys is DOM identity, not fewer bytes.** An earlier version of this section claimed keying
"buys the opposite on every update". Measured, that is not true of allocation: sliding a 1000-row
window by 40 allocates 1.06× unkeyed in the notify branch and 0.91× in the plain one — a wash either
way. The real return is that surviving rows keep their DOM nodes, so a text selection and keyboard
focus survive the update. That is measurable in a browser and not in these tests. Key for that
reason, not for allocation.

So keying pays for lists that **change under the user** — a feed, a sliding window, a grid they
filter and sort while reading it — and is not worth it for a large table that renders once and sits
still.

### Value-type keys box — prefer a reference key

`Key` is `Func<TItem, object?>`, so returning an `int`, a `Guid` or a tuple **boxes once per row per
render**. Measured at 1000 rows:

| key | extra allocation per render | per row |
|---|---|---|
| `m => m.Id` (string) | 0 | 0 B |
| `m => m.Seq` (int) | +24 KB | 24 B |
| `m => (m.A, m.B)` (tuple) | +24 KB | 24 B |
| `m => $"{m.A}-{m.B}"` (composed string) | +48 to +88 KB | 48-88 B |

Practical guidance:

- **Prefer a key that is already a reference** — a `string` id, or the item itself when it has
  identity. It allocates nothing.
- **A value-type key is fine for ordinary lists.** 24 B/row is ~8-11% of a 1000-row re-render. At a
  few hundred rows it is noise; at thousands of rows re-rendering often, it is worth removing.
- **The composite-tuple advice above still stands** — a `(int, int)` tuple boxes into the same 24
  bytes a plain `int` does, so a composite key costs no more than a simple one.
- **Do not compose a string to dodge the box.** It allocates a fresh string per row per render — 2×
  to 3.7× the cost of the box it was meant to avoid. Accept the box, or key on something that is
  already a reference.

All of these numbers are one measurement, on one machine, under bUnit rather than a browser, and
they are pinned by tests in this repo (`ObservableForEachKeyAllocationTests`) so the guidance cannot
drift from the code. Treat them as the shape of the trade-off, not a guarantee; if it matters at
your row counts, measure your own screen.
