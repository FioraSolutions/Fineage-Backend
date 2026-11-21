namespace Fineage.Zone.Simulation;

/// <summary>
/// What one region body leaves for shared state (zone-simulation.md §3.4): its fan-out and its messages to entities.
/// The scheduler opens a scope per region on its own thread, runs the body inside it on whichever thread the executor
/// picks, and commits the scopes in ascending region order after the stage barrier, so what a stage leaves behind is
/// the same under every executor.
/// </summary>
public interface IRegionScope
{
    /// <summary>Makes the scope current on the calling thread until the result is disposed.</summary>
    IDisposable Enter();

    /// <summary>On the scheduler thread after the barrier, in ascending region order; also after a body that threw.</summary>
    void Commit();
}

/// <summary>Opens the <see cref="IRegionScope"/> a region body runs in.</summary>
public interface IRegionScopes
{
    IRegionScope Open(string stage, TickContext tick, RegionId region);
}

/// <summary>Region bodies write shared state directly: schedulers without a world (tests of the clock itself).</summary>
public sealed class DirectRegionScopes : IRegionScopes
{
    public static DirectRegionScopes Instance { get; } = new();

    public IRegionScope Open(string stage, TickContext tick, RegionId region) => Scope.Instance;

    sealed class Scope : IRegionScope, IDisposable
    {
        public static Scope Instance { get; } = new();

        public IDisposable Enter() => this;

        public void Commit()
        {
        }

        public void Dispose()
        {
        }
    }
}
