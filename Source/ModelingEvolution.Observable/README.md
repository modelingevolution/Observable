# ModelingEvolution.Observable

Minimal MVVM primitives for .NET.

## ObservableCollection\<T\> with Subscriber Tracking

`ModelingEvolution.Observable.ObservableCollection<T>` is a drop-in replacement for
`System.Collections.ObjectModel.ObservableCollection<T>` that tracks how many handlers
are attached to `CollectionChanged` and raises a `SubscribersAvailable` event on
transitions:

- **0 to 1** subscriber: `SubscribersAvailable(true)` — someone started watching
- **1 to 0** subscribers: `SubscribersAvailable(false)` — nobody is watching anymore

### The Problem

Read models in event-sourced systems often back expensive background services:
ping monitors, streaming status trackers, polling loops. These services should
only run while a UI is actually displaying the data. Without subscriber tracking,
every page must manually call `Subscribe()` / `Unsubscribe()` in its lifecycle
methods, coupling the view to service management:

```csharp
// Every page that shows devices must do this:
protected override void OnInitialized()
{
    _pingMonitor.Subscribe();
    _cameraStatus.Subscribe();
}

public void Dispose()
{
    _pingMonitor.Unsubscribe();
    _cameraStatus.Unsubscribe();
}
```

This is error-prone (forget to unsubscribe = resource leak) and forces the view
to know about services it shouldn't care about.

### The Solution

The read model uses `ModelingEvolution.Observable.ObservableCollection<T>` for its
`Items` collection and wires `SubscribersAvailable` in the constructor:

```csharp
public class DevicesReadModel
{
    private readonly PingMonitor _pingMonitor;

    public ObservableCollection<DeviceItem> Items { get; }

    public DevicesReadModel(PingMonitor pingMonitor)
    {
        _pingMonitor = pingMonitor;
        Items = new ObservableCollection<DeviceItem>();
        Items.SubscribersAvailable += OnSubscribersAvailable;
    }

    private void OnSubscribersAvailable(bool available)
    {
        if (available)
            _pingMonitor.Subscribe();
        else
            _pingMonitor.Unsubscribe();
    }
}
```

The Blazor view simply binds to the collection. When `<ObservableForEach>` (or
`<Observable>`) subscribes to `Items.CollectionChanged`, the read model detects it
and starts its services. When the component disposes, it unsubscribes, and the
read model stops its services. The view has zero knowledge of the background
services:

```razor
@* No OnInitialized, no Dispose, no IDisposable. *@
@* ObservableForEach subscribes to CollectionChanged => services start automatically. *@

<ObservableForEach ItemSource="@ReadModel.Items"
                   IsNotifyPropertyChangedEnabled="true"
                   Context="device">
    <tr>
        <td>@device.Name</td>
        <td>@device.PingMs ms</td>
    </tr>
</ObservableForEach>
```

### When to Use

Use `ModelingEvolution.Observable.ObservableCollection<T>` instead of the system
`ObservableCollection<T>` when:

- A collection backs expensive background work (polling, pinging, event hooks)
  that should only run while the data is being displayed
- You want the read model to own its service lifecycle instead of leaking it to
  every consuming view
- Multiple views may observe the same collection and the services should
  ref-count correctly (start on first viewer, stop when last viewer leaves)

### API

```csharp
namespace ModelingEvolution.Observable;

public class ObservableCollection<T> : Collection<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    // Raised on 0->1 (true) and 1->0 (false) CollectionChanged subscriber transitions.
    public event Action<bool>? SubscribersAvailable;

    // Everything else is identical to System.Collections.ObjectModel.ObservableCollection<T>.
}
```

### Sorted Insertion

`ObservableCollection<T>` supports maintaining sorted order via binary search:

```csharp
// Insert maintaining sorted order — uses IComparable<T> by default.
var items = new ObservableCollection<DeviceTypeInfo>();
items.InsertSorted(newItem);

// Or provide a custom comparer.
items.InsertSorted(newItem, Comparer<DeviceTypeInfo>.Create((a, b) => a.Name.CompareTo(b.Name)));
```

`BinarySearch` follows the same convention as `List<T>.BinarySearch`: it returns
the zero-based index of the item if found, or the bitwise complement (`~index`)
of the insertion point if not found.

```csharp
int index = items.BinarySearch(target);
if (index < 0)
{
    int insertionPoint = ~index;
    // target not found — insertionPoint is where it would go
}
```

```csharp
// Full signatures
public int BinarySearch(T item, IComparer<T>? comparer = null)
public void InsertSorted(T item, IComparer<T>? comparer = null)
```

## ObservableCollectionView\<T\>

Filtered view over an `IList<T>` that implements `INotifyCollectionChanged`.
Supports dynamic `Filter` predicate with automatic `Merge()` on filter change.

## ObservableCollectionView\<TDst, TSrc\>

Transforming + filtering view. Maps `TSrc` to `TDst` (via `IViewFor<TSrc>`)
with optional filtering. Propagates collection change notifications.
