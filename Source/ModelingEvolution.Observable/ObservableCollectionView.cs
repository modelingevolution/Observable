using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModelingEvolution.Observable;

/// <summary>
/// Observable collection view that transforms items from <typeparamref name="TSrc"/> to <typeparamref name="TDst"/> with optional filtering.
/// Propagates collection change notifications from the source collection.
/// </summary>
/// <typeparam name="TDst">The destination (view-model) item type, must implement <see cref="IViewFor{TSrc}"/> and <see cref="IEquatable{TDst}"/>.</typeparam>
/// <typeparam name="TSrc">The source item type.</typeparam>
public class ObservableCollectionView<TDst, TSrc> :
    IObservableCollectionView<TDst, TSrc>,
    INotifyCollectionChanged,
    INotifyPropertyChanged,
    IList<TDst>,
    ICollection<TDst>,
    IEnumerable<TDst>,
    IEnumerable,
    IList,
    ICollection,
    IReadOnlyList<TDst>,
    IReadOnlyCollection<TDst>,
    IDisposable
    where TDst : IViewFor<TSrc>, IEquatable<TDst>
{
    private readonly Func<TSrc, TDst> _convertItem;
    private readonly IList<TSrc> _internal;
    private readonly ObservableCollection<TDst> _filtered;
    private Predicate<TDst> _filter;
    private static readonly Predicate<TDst> _trueFilter = (Predicate<TDst>)(x => true);

    /// <summary>Gets or sets the filter predicate. Returns <c>null</c> when no filter is active. Setting to <c>null</c> removes the filter.</summary>
    public Predicate<TDst> Filter
    {
        get => !(this._filter == ObservableCollectionView<TDst, TSrc>._trueFilter) ? this._filter : (Predicate<TDst>)null;
        set
        {
            this._filter = value == null ? ObservableCollectionView<TDst, TSrc>._trueFilter : value;
            this.Merge();
        }
    }

    private void Merge()
    {
        int index = 0;
        foreach (TSrc src in (IEnumerable<TSrc>)this._internal)
        {
            if (index < this._filtered.Count)
            {
                TDst dst;
                if (this._filter(dst = this._convertItem(src)))
                {
                    if ((object)src != (object)this._filtered[index].Source)
                        this._filtered.Insert(index, dst);
                    ++index;
                }
                else if ((object)src == (object)this._filtered[index].Source)
                    this._filtered.RemoveAt(index);
            }
            else
            {
                TDst dst;
                if (this._filter(dst = this._convertItem(src)))
                {
                    this._filtered.Add(dst);
                    ++index;
                }
            }
        }
        while (index < this._filtered.Count)
            this._filtered.RemoveAt(index);
    }

    /// <summary>Initializes a new instance that projects and observes the given source list.</summary>
    /// <param name="convertItem">Function that converts a source item to a destination item.</param>
    /// <param name="src">The source list, which must implement <see cref="INotifyCollectionChanged"/>.</param>
    public ObservableCollectionView(Func<TSrc, TDst> convertItem, IList<TSrc> src)
    {
        this._convertItem = convertItem;
        this._internal = src;
        this._filtered = new ObservableCollection<TDst>();
        this._filtered.AddRange<TDst>(this._internal.Select<TSrc, TDst>(this._convertItem));
        if (!(src is INotifyCollectionChanged collectionChanged))
            throw new ArgumentException("src must implement INotifyCollectionChanged");
        collectionChanged.CollectionChanged += new NotifyCollectionChangedEventHandler(this.OnSrcCollectionChangesOnCollectionChanged);
        this._filtered.CollectionChanged += (NotifyCollectionChangedEventHandler)((s, e) => this.ViewCollectionChanged(e));
        ((INotifyPropertyChanged)this._filtered).PropertyChanged += (PropertyChangedEventHandler)((s, e) => this.ViewPropertyChanged(e));
        this._filter = ObservableCollectionView<TDst, TSrc>._trueFilter;
    }

    private void OnSrcCollectionChangesOnCollectionChanged(
        object s,
        NotifyCollectionChangedEventArgs e)
    {
        this.SourceCollectionChanged(e);
    }

    private void ViewPropertyChanged(PropertyChangedEventArgs propertyChangedEventArgs)
    {
        PropertyChangedEventHandler propertyChanged = this.PropertyChanged;
        if (propertyChanged == null)
            return;
        propertyChanged((object)this, propertyChangedEventArgs);
    }

    private void ViewCollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        NotifyCollectionChangedEventHandler collectionChanged = this.CollectionChanged;
        if (collectionChanged == null)
            return;
        collectionChanged((object)this, args);
    }

    /// <summary>Gets a value indicating whether a filter is currently active.</summary>
    public bool IsFiltered => this.Filter != null;

    private void SourceCollectionChanged(NotifyCollectionChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Add)
        {
            TDst[] array = args.NewItems.OfType<TSrc>().Select<TSrc, TDst>(this._convertItem).Where<TDst>((Func<TDst, bool>)(x => this._filter(x))).ToArray<TDst>();
            if (!this.IsFiltered)
            {
                if (args.NewStartingIndex == this._filtered.Count)
                {
                    this._filtered.AddRange<TDst>((IEnumerable<TDst>)array);
                }
                else
                {
                    foreach (TDst dst in ((IEnumerable<TDst>)array).Reverse<TDst>())
                        this._filtered.Insert(args.NewStartingIndex, dst);
                }
            }
            else
                this._filtered.AddRange<TDst>((IEnumerable<TDst>)array);
        }
        else if (args.Action == NotifyCollectionChangedAction.Remove)
        {
            foreach (TDst dst in args.OldItems.OfType<TSrc>().Select<TSrc, TDst>(this._convertItem).Where<TDst>((Func<TDst, bool>)(x => this._filter(x))))
                this._filtered.Remove(dst);
        }
        else if (args.Action == NotifyCollectionChangedAction.Replace)
        {
            if (this.IsFiltered)
                throw new NotSupportedException();
            for (int index = 0; index < args.NewItems.Count; ++index)
                this._filtered[index + args.OldStartingIndex] = this._convertItem((TSrc)args.NewItems[index]);
        }
        else
        {
            if (args.Action != NotifyCollectionChangedAction.Reset)
                return;
            this._filtered.Clear();
            this._filtered.AddRange<TDst>(this._internal.Select<TSrc, TDst>(this._convertItem));
        }
    }

    /// <summary>Copies the elements to an array, starting at the specified index.</summary>
    /// <param name="array">The destination array.</param>
    /// <param name="index">The zero-based index in <paramref name="array"/> at which copying begins.</param>
    public void CopyTo(Array array, int index) => ((ICollection)this._filtered).CopyTo(array, index);

    /// <summary>Gets a value indicating whether access to the collection is synchronized.</summary>
    public bool IsSynchronized => ((ICollection)this._filtered).IsSynchronized;

    /// <summary>Gets an object that can be used to synchronize access to the collection.</summary>
    public object SyncRoot => ((ICollection)this._filtered).SyncRoot;

    /// <summary>Adds an item to the collection.</summary>
    /// <param name="value">The item to add.</param>
    /// <returns>The index at which the item was added.</returns>
    public int Add(object value) => ((IList)this._filtered).Add(value);

    /// <summary>Determines whether the collection contains the specified value.</summary>
    /// <param name="value">The value to locate.</param>
    /// <returns><c>true</c> if the value is found; otherwise, <c>false</c>.</returns>
    public bool Contains(object value) => ((IList)this._filtered).Contains(value);

    /// <summary>Returns the index of the specified value in the collection.</summary>
    /// <param name="value">The value to locate.</param>
    /// <returns>The zero-based index, or -1 if not found.</returns>
    public int IndexOf(object value) => ((IList)this._filtered).IndexOf(value);

    /// <summary>Inserts an item at the specified index.</summary>
    /// <param name="index">The zero-based index at which to insert.</param>
    /// <param name="value">The item to insert.</param>
    public void Insert(int index, object value) => ((IList)this._filtered).Insert(index, value);

    /// <summary>Removes the first occurrence of the specified value.</summary>
    /// <param name="value">The value to remove.</param>
    public void Remove(object value) => ((IList)this._filtered).Remove(value);

    /// <summary>Gets a value indicating whether the collection has a fixed size.</summary>
    public bool IsFixedSize => ((IList)this._filtered).IsFixedSize;

    bool IList.IsReadOnly => false;

    object IList.this[int index]
    {
        get => (object)this[index];
        set => this[index] = (TDst)value;
    }

    /// <summary>Adds an item to the view.</summary>
    /// <param name="item">The item to add.</param>
    public void Add(TDst item) => this._filtered.Add(item);

    /// <summary>Removes all items from the view.</summary>
    public void Clear() => this._filtered.Clear();

    /// <summary>Determines whether the view contains the specified item.</summary>
    /// <param name="item">The item to locate.</param>
    /// <returns><c>true</c> if the item is found; otherwise, <c>false</c>.</returns>
    public bool Contains(TDst item) => this._filtered.Contains(item);

    /// <summary>Copies the elements to a typed array, starting at the specified index.</summary>
    /// <param name="array">The destination array.</param>
    /// <param name="index">The zero-based index in <paramref name="array"/> at which copying begins.</param>
    public void CopyTo(TDst[] array, int index) => this._filtered.CopyTo(array, index);

    /// <summary>Returns an enumerator that iterates through the view.</summary>
    /// <returns>An enumerator for the view items.</returns>
    public IEnumerator<TDst> GetEnumerator()
    {
        foreach (TDst dst in (Collection<TDst>)this._filtered)
            yield return dst;
    }

    /// <summary>Returns the index of the specified item.</summary>
    /// <param name="item">The item to locate.</param>
    /// <returns>The zero-based index, or -1 if not found.</returns>
    public int IndexOf(TDst item) => this._filtered.IndexOf(item);

    /// <summary>Inserts an item at the specified index.</summary>
    /// <param name="index">The zero-based index at which to insert.</param>
    /// <param name="item">The item to insert.</param>
    public void Insert(int index, TDst item) => this._filtered.Insert(index, item);

    /// <summary>Removes the first occurrence of the specified item.</summary>
    /// <param name="item">The item to remove.</param>
    /// <returns><c>true</c> if the item was removed; otherwise, <c>false</c>.</returns>
    public bool Remove(TDst item) => this._filtered.Remove(item);

    /// <summary>Removes the item at the specified index.</summary>
    /// <param name="index">The zero-based index of the item to remove.</param>
    public void RemoveAt(int index) => this._filtered.RemoveAt(index);

    /// <summary>Gets the number of elements in the view.</summary>
    public int Count => this._filtered.Count;

    bool ICollection<TDst>.IsReadOnly => false;

    /// <summary>Gets or sets the element at the specified index.</summary>
    public TDst this[int index]
    {
        get => this._filtered[index];
        set => this._filtered[index] = value;
    }

    /// <summary>Occurs when a property value changes.</summary>
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>Moves an item from one index to another.</summary>
    /// <param name="oldIndex">The zero-based index of the item to move.</param>
    /// <param name="newIndex">The zero-based destination index.</param>
    public void Move(int oldIndex, int newIndex) => this._filtered.Move(oldIndex, newIndex);

    /// <summary>Occurs when the collection changes.</summary>
    public event NotifyCollectionChangedEventHandler CollectionChanged;

    IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)this.GetEnumerator();

    /// <summary>Unsubscribes from source collection change notifications.</summary>
    public void Dispose() => ((INotifyCollectionChanged)this._internal).CollectionChanged -= new NotifyCollectionChangedEventHandler(this.OnSrcCollectionChangesOnCollectionChanged);
}