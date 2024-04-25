namespace ModelingEvolution.Observable;

public interface IViewFor<out T>
{
    T Source { get; }
}