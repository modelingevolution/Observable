using System.ComponentModel;
using System.Runtime.CompilerServices;
using ModelingEvolution.Observable;

namespace ModelingEvolution.Observable.MudBlazorSample;

/// <summary>A row that reports property changes (AutoReloadOnItemPropertyChanged).</summary>
public sealed class Row(int id) : INotifyPropertyChanged
{
    private string _name = $"row-{id}";
    private int _hits;

    public int Id { get; } = id;
    public string Name { get => _name; set { _name = value; OnChanged(); } }
    public int Hits { get => _hits; set { _hits = value; OnChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>The "read model": mutated from the fold thread, rendered by MudTable. Never reassigned.</summary>
public sealed class LiveRows
{
    public ObservableCollection<Row> Rows { get; } = new(Enumerable.Range(1, 20).Select(i => new Row(i)));
    public long Folded;
}

/// <summary>Simulates projections folding events on a non-UI thread: adds, removes, and in-place updates.</summary>
public sealed class Fold(LiveRows live, ILogger<Fold> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var rnd = new Random(1);
        var next = 21;
        while (!ct.IsCancellationRequested)
        {
            var rows = live.Rows;
            switch (rnd.Next(5))
            {
                case 0: rows.Add(new Row(next++)); break;
                case 1: if (rows.Count > 5) rows.RemoveAt(rnd.Next(rows.Count)); break;
                case 4: if (rows.Count > 60) rows.RemoveAt(rows.Count - 1); break;
                case 2: rows.Insert(0, new Row(next++)); break;
                default: if (rows.Count > 0) rows[rnd.Next(Math.Min(rows.Count, 10))].Hits++; break; // first page, so it is visible
            }
            Interlocked.Increment(ref live.Folded);
            if (live.Folded % 100 == 0) log.LogInformation("folded {Folded} events, {Count} rows", live.Folded, rows.Count);
            await Task.Delay(50, ct);
        }
    }
}
