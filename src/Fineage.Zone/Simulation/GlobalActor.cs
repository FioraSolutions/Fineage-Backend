namespace Fineage.Zone.Simulation;

/// <summary>The clock an actor's schedule follows.</summary>
public enum ActorTime
{
    /// <summary>The tick time (<see cref="TickContext.NowUnixMs"/>): durations such as boss respawn windows.</summary>
    Simulation,

    /// <summary>The step's wall sample (<see cref="TickContext.WallUnixMs"/>): weekdays and hours of the calendar.</summary>
    Calendar,
}

/// <summary>
/// A world-wide system (olympiad, sieges, seven signs, ...) that owns its state and runs on the scheduler thread,
/// section 3.4 of <c>docs/architecture/zone-simulation.md</c>. It holds no clock: its scheduled step reads the time of
/// the step it is given (<see cref="Time"/>), and requests made inside a step pass the step's time. A roll draws from
/// the actor's own seeded stream. The step never touches entities, sessions or the network: it returns effects, and the
/// world applies them after the actor returned. Region bodies never reach an actor (<see cref="RequireActorThread"/>);
/// what they have to tell one is deferred to the stage barrier, where it runs on the scheduler thread in a fixed order.
/// </summary>
public abstract class GlobalActor<TEffect>
{
    protected GlobalActor(string name, ActorTime time)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A global actor needs a name.", nameof(name));
        Name = name;
        Time = time;
    }

    /// <summary>The actor's simulation stage.</summary>
    public string Name { get; }

    public ActorTime Time { get; }

    /// <summary>Runs the actor's scheduled work at the step's time and returns the effects for the world, in order.</summary>
    public IReadOnlyList<TEffect> Step(TickContext tick)
    {
        RequireActorThread();
        var effects = new List<TEffect>();
        OnStep(Time == ActorTime.Calendar ? tick.WallUnixMs : tick.NowUnixMs, effects);
        return effects;
    }

    protected abstract void OnStep(long nowUnixMs, List<TEffect> effects);

    /// <summary>Throws inside a region body: other regions may be running, and the actor is single-threaded.</summary>
    protected void RequireActorThread()
    {
        if (RegionTickScheduler.CurrentBody is { } region)
        {
            throw new InvalidOperationException(
                $"global actor '{Name}' reached from the body of region {region}; defer the request to the barrier");
        }
    }
}
