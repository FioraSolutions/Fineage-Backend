namespace Fineage.Zone.World;

/// <summary>Detects synchronous account-store I/O on the simulation thread during enter and command drain.</summary>
sealed class SimulationSyncIoGuard
{
    readonly WorldAffinity _affinity;
    int _depth;

    public SimulationSyncIoGuard(WorldAffinity affinity) => _affinity = affinity;

    public bool Active => Volatile.Read(ref _depth) > 0;

    public IDisposable EnterScope() => new Scope(this);

    public IDisposable CommandScope() => new Scope(this);

    public void Report(string operation)
    {
        if (Active && _affinity.OnStep)
            Console.Error.WriteLine($"[ZONE] sync-io on simulation thread: {operation} during enter/command path");
    }

    sealed class Scope : IDisposable
    {
        readonly SimulationSyncIoGuard _owner;

        public Scope(SimulationSyncIoGuard owner)
        {
            _owner = owner;
            Interlocked.Increment(ref owner._depth);
        }

        public void Dispose() => Interlocked.Decrement(ref _owner._depth);
    }
}