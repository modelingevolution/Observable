using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace ModelingEvolution.Observable;

public interface IObservableCollectionView<TDst, TSrc> :
    INotifyCollectionChanged,
    INotifyPropertyChanged,
    IList<TDst>, IReadOnlyList<TDst>
    where TDst : IViewFor<TSrc>, IEquatable<TDst>
{
    new TDst this[int index]
    {
        get { return ((IReadOnlyList<TDst>)this)[index]; }
        set { ((IList<TDst>)this)[index] = value; }
    }

    new int Count
    {
        get { return ((IReadOnlyList<TDst>)this).Count; }
    }
}