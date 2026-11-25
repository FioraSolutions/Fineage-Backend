using System.Collections.Concurrent;
using System.Text;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// A player input to the simulation. Handlers submit it with <see cref="ZoneWorld.ExecuteAsync"/>; the
/// <c>commands</c> stage applies it on the simulation thread at the start of the next step, at the tick time, and
/// records it in the command log. <see cref="Name"/> is the type name in kebab case without the <c>Command</c> suffix.
/// </summary>
public abstract record ZoneCommand : ISimulationCommand
{
    static readonly ConcurrentDictionary<Type, string> Names = new();

    public virtual string Name => Names.GetOrAdd(GetType(), NameOf);

    /// <summary>Player input is logged and replayed; requests (<see cref="ZoneRequest{T}"/>) are neither.</summary>
    internal virtual bool Logged => true;

    internal virtual ZoneCommandClass IngressClass => ZoneCommandClass.Gameplay;

    /// <summary>
    /// Consecutive pending commands with this flag set collapse to the latest. A command without it is a boundary:
    /// movement on either side of it stays in the queue.
    /// </summary>
    internal virtual bool IsCoalesceableMovement => false;

    internal virtual int ApplyCost => 2;

    /// <summary>Applies the command for <paramref name="session"/>; returns the rejection reason, or null.</summary>
    internal abstract string? Apply(ZoneWorld world, ZoneSession session, TickContext tick);

    /// <summary>The result of a world rule that runs on the simulation thread, and so has completed when it returns.</summary>
    protected string? Completed(Task<string?> rule) =>
        rule.IsCompleted
            ? rule.GetAwaiter().GetResult()
            : throw new InvalidOperationException($"command {Name} did not complete synchronously on the simulation thread");

    /// <summary>Simulation time of the step applying the command: what rules compare against.</summary>
    protected static DateTimeOffset TickTime(TickContext tick) => DateTimeOffset.FromUnixTimeMilliseconds(tick.NowUnixMs);

    /// <summary>The step's wall sample: for calendar deadlines (clan penalties and dissolutions, castle schedules).</summary>
    protected static DateTimeOffset WallTime(TickContext tick) => DateTimeOffset.FromUnixTimeMilliseconds(tick.WallUnixMs);

    static string NameOf(Type type)
    {
        const string suffix = "Command";
        if (!type.Name.EndsWith(suffix, StringComparison.Ordinal) || type.Name.Length == suffix.Length)
            throw new InvalidOperationException($"zone command type {type.FullName} must be named <Action>{suffix}");
        var name = new StringBuilder();
        foreach (char c in type.Name.AsSpan(0, type.Name.Length - suffix.Length))
        {
            if (char.IsUpper(c) && name.Length > 0)
                name.Append('-');
            name.Append(char.ToLowerInvariant(c));
        }

        return name.ToString();
    }
}

public sealed record MoveCommand(int DestX, int DestY, int DestZ) : ZoneCommand
{
    internal override ZoneCommandClass IngressClass => ZoneCommandClass.Interactive;
    internal override bool IsCoalesceableMovement => true;
    internal override int ApplyCost => 1;
    internal int ResultX { get; private set; }
    internal int ResultY { get; private set; }
    internal int ResultZ { get; private set; }

    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick)
    {
        string? error = Completed(world.TryMoveAsync(session, DestX, DestY, DestZ, CancellationToken.None));
        ResultX = session.X;
        ResultY = session.Y;
        ResultZ = session.Z;
        return error;
    }
}

public sealed record StopMoveCommand : ZoneCommand
{
    internal override ZoneCommandClass IngressClass => ZoneCommandClass.Interactive;
    internal override int ApplyCost => 1;

    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick)
    {
        world.StopMove(session);
        return null;
    }
}

/// <summary>The client's reported position; refused (with the drift) when it is too far from the server's.</summary>
public sealed record ValidatePositionCommand(int ClientX, int ClientY, int ClientZ) : ZoneCommand
{
    internal override ZoneCommandClass IngressClass => ZoneCommandClass.Interactive;
    internal override int ApplyCost => 1;

    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        world.ValidateClientPosition(session, ClientX, ClientY, ClientZ, out int dxy, out int dz)
            ? $"position drift dxy={dxy} dz={dz}"
            : null;
}

public sealed record AttackCommand(int TargetObjectId, bool Forced) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAttackAsync(session, TargetObjectId, CancellationToken.None, Forced));
}

public sealed record SkillUseCommand(int SkillId, int TargetObjectId, bool CtrlPressed) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySkillUseAsync(session, SkillId, TargetObjectId, CancellationToken.None, CtrlPressed));
}

public sealed record SelectTargetCommand(int TargetObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySelectTargetAsync(session, TargetObjectId, CancellationToken.None));
}

/// <summary>Load-test relocation (<see cref="LoadTestCombatSettings.AllowsTeleport"/>); see <see cref="ZoneWorld.TryLoadTestTeleport"/>.</summary>
public sealed record LoadTestTeleportCommand(int X, int Y, int Z) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        world.TryLoadTestTeleport(session, X, Y, Z);
}

public sealed record CancelTargetCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryCancelTargetAsync(session, CancellationToken.None));
}
