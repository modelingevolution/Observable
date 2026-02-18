using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ModelingEvolution.Observable
{
    /// <summary>
    /// A filtered, observable view over an <see cref="IList{T}"/> that implements <see cref="INotifyCollectionChanged"/>.
    /// Setting the <see cref="Filter"/> predicate automatically re-evaluates which items are visible.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    public class ObservableCollectionView<T> :
    INotifyCollectionChanged,
    INotifyPropertyChanged,
    IList<T>,
    ICollection<T>,
    IEnumerable<T>,
    IEnumerable,
    IList,
    ICollection,
    IReadOnlyList<T>,
    IReadOnlyCollection<T>,
    IDisposable
    {
        private readonly IList<T> _internal;
        private readonly ObservableCollection<T> _filtered;
        private Predicate<T> _filter;

        /// <summary>
        /// Gets or sets the filter predicate. Setting a new value automatically calls <see cref="Merge"/> to update the view.
        /// A <c>null</c> value resets the filter to accept all items.
        /// </summary>
        public Predicate<T> Filter
        {
            get => this._filter;
            set
            {
                this._filter = value == null ? (Predicate<T>)(x => true) : value;
                this.Merge();
            }
        }

        private void Merge()
        {
            int index = 0;
            foreach (T obj in (IEnumerable<T>)this._internal)
            {
                if (index < this._filtered.Count)
                {
                    if (this._filter(obj))
                    {
                        if ((object)obj != (object)this._filtered[index])
                            this._filtered.Insert(index, obj);
                        ++index;
                    }
                    else if ((object)obj == (object)this._filtered[index])
                        this._filtered.RemoveAt(index);
                }
                else if (this._filter(obj))
                {
                    this._filtered.Add(obj);
                    ++index;
                }
            }
            while (index < this._filtered.Count)
                this._filtered.RemoveAt(index);
        }

        /// <summary>
        /// Gets the underlying source collection.
        /// </summary>
        public IList<T> Source => this._internal;

        /// <summary>
        /// Initializes a new instance of <see cref="ObservableCollectionView{T}"/> with the specified source collection.
        /// </summary>
        /// <param name="src">The source list to wrap. Must implement <see cref="INotifyCollectionChanged"/>. If <c>null</c>, a new <see cref="ObservableCollection{T}"/> is created.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="src"/> does not implement <see cref="INotifyCollectionChanged"/>.</exception>
        public ObservableCollectionView(IList<T> src = null)
        {
            this._internal = src ?? (IList<T>)new ObservableCollection<T>();
            this._filtered = new ObservableCollection<T>();
            this._filtered.AddRange<T>((IEnumerable<T>)this._internal);
            if (!(this._internal is INotifyCollectionChanged collectionChanged))
                throw new ArgumentException("Source collection must implement INotifyCollectionChanged");
            collectionChanged.CollectionChanged += new NotifyCollectionChangedEventHandler(this.SourceCollectionChanged);
            this._filtered.CollectionChanged += new NotifyCollectionChangedEventHandler(this.ViewCollectionChanged);
            ((INotifyPropertyChanged)this._filtered).PropertyChanged += new PropertyChangedEventHandler(this.ViewPropertyChanged);
            this._filter = (Predicate<T>)(x => true);
        }

        private void ViewPropertyChanged(object s, PropertyChangedEventArgs propertyChangedEventArgs)
        {
            PropertyChangedEventHandler propertyChanged = this.PropertyChanged;
            if (propertyChanged == null)
                return;
            propertyChanged((object)this, propertyChangedEventArgs);
        }

        private void ViewCollectionChanged(object s, NotifyCollectionChangedEventArgs args)
        {
            NotifyCollectionChangedEventHandler collectionChanged = this.CollectionChanged;
            if (collectionChanged == null)
                return;
            collectionChanged((object)this, args);
        }

        private void SourceCollectionChanged(object s, NotifyCollectionChangedEventArgs args)
        {
            if (args.Action == NotifyCollectionChangedAction.Add)
                this._filtered.AddRange<T>((IEnumerable<T>)args.NewItems.OfType<T>().Where<T>((Func<T, bool>)(x => this._filter(x))).ToArray<T>());
            else if (args.Action == NotifyCollectionChangedAction.Remove)
            {
                foreach (T obj in args.OldItems.OfType<T>().Where<T>((Func<T, bool>)(x => this._filter(x))))
                    this._filtered.Remove(obj);
            }
            else if (args.Action == NotifyCollectionChangedAction.Replace)
            {
                for (int index1 = 0; index1 < args.NewItems.Count; ++index1)
                {
                    T newItem = (T)args.NewItems[index1];
                    int index2 = this._filtered.IndexOf(newItem);
                    if (index2 >= 0)
                        this._filtered[index2] = newItem;
                }
            }
            else
            {
                if (args.Action != NotifyCollectionChangedAction.Reset)
                    return;
                this._filtered.Clear();
                this._filtered.AddRange<T>(this._internal.Where<T>((Func<T, bool>)(x => this._filter(x))));
            }
        }

        /// <summary>
        /// Copies the elements of the collection to an <see cref="Array"/>, starting at a particular index.
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="index">The zero-based index in <paramref name="array"/> at which copying begins.</param>
        public void CopyTo(Array array, int index) => ((ICollection)this._filtered).CopyTo(array, index);

        /// <summary>
        /// Gets a value indicating whether access to the collection is synchronized (thread safe).
        /// </summary>
        public bool IsSynchronized => ((ICollection)this._filtered).IsSynchronized;

        /// <summary>
        /// Gets an object that can be used to synchronize access to the collection.
        /// </summary>
        public object SyncRoot => ((ICollection)this._filtered).SyncRoot;

        /// <summary>
        /// Adds an item to the collection.
        /// </summary>
        /// <param name="value">The object to add.</param>
        /// <returns>The position into which the new element was inserted.</returns>
        public int Add(object value) => ((IList)this._filtered).Add(value);

        /// <summary>
        /// Determines whether the collection contains a specific value.
        /// </summary>
        /// <param name="value">The object to locate.</param>
        /// <returns><c>true</c> if <paramref name="value"/> is found; otherwise, <c>false</c>.</returns>
        public bool Contains(object value) => ((IList)this._filtered).Contains(value);

        /// <summary>
        /// Determines the index of a specific value in the collection.
        /// </summary>
        /// <param name="value">The object to locate.</param>
        /// <returns>The index of <paramref name="value"/> if found; otherwise, -1.</returns>
        public int IndexOf(object value) => ((IList)this._filtered).IndexOf(value);

        /// <summary>
        /// Inserts an item at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index at which <paramref name="value"/> should be inserted.</param>
        /// <param name="value">The object to insert.</param>
        public void Insert(int index, object value) => ((IList)this._filtered).Insert(index, value);

        /// <summary>
        /// Removes the first occurrence of a specific object from the collection.
        /// </summary>
        /// <param name="value">The object to remove.</param>
        public void Remove(object value) => ((IList)this._filtered).Remove(value);

        /// <summary>
        /// Gets a value indicating whether the collection has a fixed size.
        /// </summary>
        public bool IsFixedSize => ((IList)this._filtered).IsFixedSize;

        bool IList.IsReadOnly => false;

        object IList.this[int index]
        {
            get => (object)this[index];
            set => this[index] = (T)value;
        }

        /// <summary>
        /// Adds an item to the filtered collection.
        /// </summary>
        /// <param name="item">The item to add.</param>
        public void Add(T item) => this._filtered.Add(item);

        /// <summary>
        /// Removes all items from the filtered collection.
        /// </summary>
        public void Clear() => this._filtered.Clear();

        /// <summary>
        /// Determines whether the filtered collection contains a specific item.
        /// </summary>
        /// <param name="item">The item to locate.</param>
        /// <returns><c>true</c> if <paramref name="item"/> is found; otherwise, <c>false</c>.</returns>
        public bool Contains(T item) => this._filtered.Contains(item);

        /// <summary>
        /// Copies the elements of the filtered collection to an array, starting at a particular index.
        /// </summary>
        /// <param name="array">The destination array.</param>
        /// <param name="index">The zero-based index in <paramref name="array"/> at which copying begins.</param>
        public void CopyTo(T[] array, int index) => this._filtered.CopyTo(array, index);

        /// <summary>
        /// Returns an enumerator that iterates through the filtered collection.
        /// </summary>
        /// <returns>An enumerator for the filtered collection.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            foreach (T obj in (Collection<T>)this._filtered)
                yield return obj;
        }

        /// <summary>
        /// Determines the index of a specific item in the filtered collection.
        /// </summary>
        /// <param name="item">The item to locate.</param>
        /// <returns>The index of <paramref name="item"/> if found; otherwise, -1.</returns>
        public int IndexOf(T item) => this._filtered.IndexOf(item);

        /// <summary>
        /// Inserts an item at the specified index in the filtered collection.
        /// </summary>
        /// <param name="index">The zero-based index at which <paramref name="item"/> should be inserted.</param>
        /// <param name="item">The item to insert.</param>
        public void Insert(int index, T item) => this._filtered.Insert(index, item);

        /// <summary>
        /// Removes the first occurrence of a specific item from the filtered collection.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns><c>true</c> if the item was successfully removed; otherwise, <c>false</c>.</returns>
        public bool Remove(T item) => this._filtered.Remove(item);

        /// <summary>
        /// Removes the item at the specified index from the filtered collection.
        /// </summary>
        /// <param name="index">The zero-based index of the item to remove.</param>
        public void RemoveAt(int index) => this._filtered.RemoveAt(index);

        /// <summary>
        /// Gets the number of elements in the filtered collection.
        /// </summary>
        public int Count => this._filtered.Count;

        bool ICollection<T>.IsReadOnly => false;

        /// <summary>
        /// Gets or sets the element at the specified index in the filtered collection.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get or set.</param>
        /// <returns>The element at the specified index.</returns>
        public T this[int index]
        {
            get => this._filtered[index];
            set => this._filtered[index] = value;
        }

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Moves the item at the specified old index to the specified new index.
        /// </summary>
        /// <param name="oldIndex">The zero-based index of the item to move.</param>
        /// <param name="newIndex">The zero-based index to move the item to.</param>
        public void Move(int oldIndex, int newIndex) => this._filtered.Move(oldIndex, newIndex);

        /// <summary>
        /// Occurs when the collection changes.
        /// </summary>
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        IEnumerator IEnumerable.GetEnumerator() => (IEnumerator)this.GetEnumerator();

        /// <summary>
        /// Unsubscribes from the source collection's change notifications.
        /// </summary>
        public void Dispose()
        {
            if (!(this._internal is INotifyCollectionChanged collectionChanged))
                return;
            collectionChanged.CollectionChanged -= new NotifyCollectionChangedEventHandler(this.SourceCollectionChanged);
        }
    }

    /// <summary>
    /// Provides extension methods for collections and string formatting utilities.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Adds all elements from <paramref name="other"/> to the end of the list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The target list to add elements to.</param>
        /// <param name="other">The elements to add.</param>
        public static void AddRange<T>(this IList<T> list, IEnumerable<T> other)
        {
            if (list is List<T> objList)
            {
                objList.AddRange(other);
            }
            else
            {
                foreach (T obj in other)
                    list.Add(obj);
            }
        }

        /// <summary>
        /// Enumerates the list by index, gracefully stopping on index-out-of-range or concurrent modification errors.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to enumerate.</param>
        /// <returns>An enumerable sequence of elements from the list.</returns>
        public static IEnumerable<T> For<T>(this IReadOnlyList<T> list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    T item = default(T);
                    bool isOk = true;
                    try
                    {
                        item = list[i];
                    }
                    catch
                    {
                        isOk = false;
                    }

                    if (isOk)
                        yield return item;
                    else yield break;
                }
            }

        /// <summary>
        /// Enumerates the list by index with an optional filter, gracefully stopping on index-out-of-range or concurrent modification errors.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="list">The list to enumerate.</param>
        /// <param name="filter">An optional predicate to filter elements. If <c>null</c>, all elements are returned.</param>
        /// <returns>An enumerable sequence of elements from the list that match the filter.</returns>
        public static IEnumerable<T> For<T>(this IReadOnlyList<T> list, Predicate<T>? filter)
        {
            for (int i = 0; i < list.Count; i++)
            {
                T item = default(T);
                bool isOk = true;
                try
                {
                    item = list[i];
                }
                catch
                {
                    isOk = false;
                }

                if (isOk && (filter == null || filter(item)))
                    yield return item;
                else if (!isOk)
                    yield break;
            }
        }

        private static readonly CultureInfo EN_US = new CultureInfo("en-US");

        /// <summary>
        /// Returns a debug-friendly representation of the string with escaped newlines and tabs, or "null" if the string is <c>null</c>.
        /// </summary>
        /// <param name="s">The string to format.</param>
        /// <returns>A debug-friendly string representation.</returns>
        public static string ToDebugString(this string s)
        {
            return s == null ? "null" : s.Replace("\n", "\\n").Replace("\t", "\\t");
        }

        /// <summary>
        /// Formats the double value as a JavaScript-compatible string using en-US culture.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>The formatted string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string AsJs(this double value)
        {
            return $"{value.ToString(EN_US)}";
        }

        /// <summary>
        /// Formats the integer value as a CSS pixel string (e.g., "42px").
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>The formatted pixel string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string AsPx(this int value)
        {
            return $"{value}px";
        }

        /// <summary>
        /// Formats the double value as a CSS pixel string using en-US culture (e.g., "3.14px").
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>The formatted pixel string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string AsPx(this double value)
        {
            return $"{value.ToString(EN_US)}px";
        }

        /// <summary>
        /// Formats the nullable double value as a CSS pixel string using en-US culture, defaulting to 0 if <c>null</c>.
        /// </summary>
        /// <param name="value">The value to format.</param>
        /// <returns>The formatted pixel string.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string AsPx(this double? value)
        {
            return $"{(value??0).ToString(EN_US)}px";
        }
    }
}