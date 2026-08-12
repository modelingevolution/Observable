using System.Collections.Specialized;
using FluentAssertions;

namespace ModelingEvolution.Observable.Tests;

public class ObservableCollectionViewSortTests
{
    private sealed class Person
    {
        public string Name { get; set; }
        public int Age { get; set; }

        public Person(string name, int age)
        {
            Name = name;
            Age = age;
        }
    }

    private sealed class PersonView : IViewFor<Person>, IEquatable<PersonView>
    {
        public Person Source { get; }
        public string Name => Source.Name;
        public int Age => Source.Age;

        public PersonView(Person source)
        {
            Source = source;
        }

        public bool Equals(PersonView other) => other != null && ReferenceEquals(Source, other.Source);
        public override bool Equals(object obj) => Equals(obj as PersonView);
        public override int GetHashCode() => Source.GetHashCode();
    }

    private static ObservableCollectionView<PersonView, Person> CreateView(ObservableCollection<Person> src)
        => new ObservableCollectionView<PersonView, Person>(p => new PersonView(p), src);

    [Fact]
    public void Add_WithComparerSet_InsertsAtSortedPosition()
    {
        var src = new ObservableCollection<Person>();
        using var view = CreateView(src);
        view.SortBy(p => p.Age);

        src.Add(new Person("Bob", 30));
        src.Add(new Person("Alice", 20));
        src.Add(new Person("Carl", 25));

        view.Select(v => v.Name).Should().Equal("Alice", "Carl", "Bob");
    }

    [Fact]
    public void Comparer_ChangedAfterItemsAdded_ResortsAndNotifies()
    {
        var src = new ObservableCollection<Person>();
        src.Add(new Person("Bob", 30));
        src.Add(new Person("Alice", 20));
        src.Add(new Person("Carl", 25));

        using var view = CreateView(src);
        view.Select(v => v.Name).Should().Equal("Bob", "Alice", "Carl");

        var raised = false;
        view.CollectionChanged += (_, _) => raised = true;

        view.SortBy(p => p.Age);

        raised.Should().BeTrue();
        view.Select(v => v.Name).Should().Equal("Alice", "Carl", "Bob");
    }

    [Fact]
    public void Descending_SortOrder_IsMaintained()
    {
        var src = new ObservableCollection<Person>();
        using var view = CreateView(src);
        view.SortBy(p => p.Age, descending: true);

        src.Add(new Person("Bob", 30));
        src.Add(new Person("Alice", 20));
        src.Add(new Person("Carl", 25));

        view.Select(v => v.Name).Should().Equal("Bob", "Carl", "Alice");
    }

    [Fact]
    public void ThenBy_BreaksTiesUsingSecondaryKey()
    {
        var src = new ObservableCollection<Person>();
        using var view = CreateView(src);
        view.SortBy(p => p.Age);
        view.ThenBy(p => p.Name);

        src.Add(new Person("Zed", 20));
        src.Add(new Person("Amy", 20));
        src.Add(new Person("Bob", 20));

        view.Select(v => v.Name).Should().Equal("Amy", "Bob", "Zed");
    }

    [Fact]
    public void FilterAndComparer_Together_FiltersThenSorts()
    {
        var src = new ObservableCollection<Person>();
        using var view = CreateView(src);
        view.Filter = p => p.Age >= 25;
        view.SortBy(p => p.Age);

        src.Add(new Person("Bob", 30));
        src.Add(new Person("Alice", 20));
        src.Add(new Person("Carl", 25));

        view.Select(v => v.Name).Should().Equal("Carl", "Bob");
    }

    [Fact]
    public void NullComparer_RestoresSourceInsertionOrder()
    {
        var src = new ObservableCollection<Person>();
        using var view = CreateView(src);
        view.SortBy(p => p.Age);

        src.Add(new Person("Bob", 30));
        src.Add(new Person("Alice", 20));
        src.Add(new Person("Carl", 25));

        view.Select(v => v.Name).Should().Equal("Alice", "Carl", "Bob");

        view.Comparer = null;

        view.IsSorted.Should().BeFalse();
        view.Select(v => v.Name).Should().Equal("Bob", "Alice", "Carl");
    }

    [Fact]
    public void Remove_WhileSorted_KeepsRemainingItemsSorted()
    {
        var src = new ObservableCollection<Person>();
        using var view = CreateView(src);
        view.SortBy(p => p.Age);

        var bob = new Person("Bob", 30);
        src.Add(bob);
        src.Add(new Person("Alice", 20));
        src.Add(new Person("Carl", 25));

        src.Remove(bob);

        view.Select(v => v.Name).Should().Equal("Alice", "Carl");
    }

    [Fact]
    public void Reset_WhileSorted_RebuildsInSortedOrder()
    {
        var src = new ObservableCollection<Person>();
        using var view = CreateView(src);
        view.SortBy(p => p.Age);

        src.Add(new Person("Bob", 30));
        src.Add(new Person("Alice", 20));
        src.Clear();

        src.Add(new Person("Zed", 5));
        src.Add(new Person("Amy", 1));

        view.Select(v => v.Name).Should().Equal("Amy", "Zed");
    }

    [Fact]
    public void Dispose_UnsubscribesFromSourceCollectionChanged()
    {
        var src = new ObservableCollection<Person>();
        var view = CreateView(src);
        view.SortBy(p => p.Age);
        view.Dispose();

        src.Add(new Person("Alice", 20));

        view.Should().BeEmpty();
    }

    // -- Single-generic ObservableCollectionView<T> --

    [Fact]
    public void SingleGeneric_Add_WithComparerSet_InsertsAtSortedPosition()
    {
        var src = new ObservableCollection<int>();
        using var view = new ObservableCollectionView<int>(src);
        view.SortBy(x => x);

        src.Add(30);
        src.Add(10);
        src.Add(20);

        view.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void SingleGeneric_Comparer_ChangedAfterItemsAdded_ResortsAndNotifies()
    {
        var src = new ObservableCollection<int>();
        src.Add(30);
        src.Add(10);
        src.Add(20);

        using var view = new ObservableCollectionView<int>(src);
        view.Should().Equal(30, 10, 20);

        var events = new List<NotifyCollectionChangedEventArgs>();
        view.CollectionChanged += (_, e) => events.Add(e);

        view.SortBy(x => x);

        events.Should().NotBeEmpty();
        view.Should().Equal(10, 20, 30);
    }

    [Fact]
    public void SingleGeneric_NullComparer_RestoresSourceOrder()
    {
        var src = new ObservableCollection<int>();
        using var view = new ObservableCollectionView<int>(src);
        view.SortBy(x => x);

        src.Add(30);
        src.Add(10);
        src.Add(20);
        view.Should().Equal(10, 20, 30);

        view.Comparer = null;

        view.Should().Equal(30, 10, 20);
    }

    [Fact]
    public void SingleGeneric_FilterAndComparer_Together()
    {
        var src = new ObservableCollection<int>();
        using var view = new ObservableCollectionView<int>(src);
        view.Filter = x => x % 2 == 0;
        view.SortBy(x => x, descending: true);

        src.Add(5);
        src.Add(4);
        src.Add(3);
        src.Add(2);

        view.Should().Equal(4, 2);
    }
}
