using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Which thread may touch the world. Once a loop or a command pump drives the world (<see cref="Drive"/>), only the
/// thread running a step may, and the region bodies that step runs; anything else is a race and throws
/// (<see cref="Require"/>). A world nothing drives is touched from one thread by its host (tests, tools) and is not
/// checked. Steps never overlap: the loop runs one after another, and hand-driven hosts that step the world from more
/// than one thread (a pump and a test) take turns. Handlers never take a turn; they submit commands and requests.
/// </summary>
internal sealed class WorldAffinity
{
    readonly object _turns = new();
    int _driven;
    int _owner;
    int _depth;

    public bool Driven => Volatile.Read(ref _driven) != 0;

    /// <summary>True on the thread running the current step.</summary>
    public bool OnStep => Volatile.Read(ref _owner) == Environment.CurrentManagedThreadId;

    /// <summary>Whether the calling thread may touch world state outside a region body.</summary>
    public bool MayTouch => !Driven || OnStep;

    public void Drive() => Volatile.Write(ref _driven, 1);

    public void Require(string what)
    {
        if (!MayTouch)
            throw new InvalidOperationException(
                $"{what} off the simulation thread: submit a command or request (ZoneWorld.ExecuteAsync)");
    }

    /// <summary>Makes the calling thread the world's owner until disposed, after any step running elsewhere.</summary>
    public Turn Step()
    {
        // The scheduler thread holds the turn while a body runs on a worker: waiting for it there never ends.
        if (RegionTickScheduler.CurrentBody is { } body)
            throw new InvalidOperationException($"region {body}'s body cannot take a world turn");
        Monitor.Enter(_turns);
        if (_depth++ == 0)
            Volatile.Write(ref _owner, Environment.CurrentManagedThreadId);
        return new Turn(this);
    }

    public readonly struct Turn : IDisposable
    {
        readonly WorldAffinity? _affinity;

        internal Turn(WorldAffinity affinity) => _affinity = affinity;

        public void Dispose()
        {
            if (_affinity == null)
                return;
            if (--_affinity._depth == 0)
                Volatile.Write(ref _affinity._owner, 0);
            Monitor.Exit(_affinity._turns);
        }
    }
}
