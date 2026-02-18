namespace ModelingEvolution.Observable;

/// <summary>
/// Marker interface for view-model items that wrap a source item of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The type of the underlying source item.</typeparam>
public interface IViewFor<out T>
{
    /// <summary>Gets the underlying source item.</summary>
    T Source { get; }
}