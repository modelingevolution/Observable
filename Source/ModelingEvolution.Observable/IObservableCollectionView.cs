using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModelingEvolution.Observable;

/// <summary>
/// A read-only observable collection view that maps and filters items from <typeparamref name="TSrc"/> to <typeparamref name="TDst"/>.
/// </summary>
/// <typeparam name="TDst">The destination (view-model) item type.</typeparam>
/// <typeparam name="TSrc">The source item type.</typeparam>
public interface IObservableCollectionView<TDst, TSrc> :
    INotifyCollectionChanged,
    INotifyPropertyChanged,
    IList<TDst>, IReadOnlyList<TDst>
    where TDst : IViewFor<TSrc>, IEquatable<TDst>
{
    /// <summary>Gets or sets the element at the specified index.</summary>
    new TDst this[int index]
    {
        get { return ((IReadOnlyList<TDst>)this)[index]; }
        set { ((IList<TDst>)this)[index] = value; }
    }

    /// <summary>Gets the number of elements in the view.</summary>
    new int Count
    {
        get { return ((IReadOnlyList<TDst>)this).Count; }
    }
}