using System.Collections.Concurrent;
using System.Text;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// A player input to the simulation. Handlers submit it with <see cref="ZoneWorld.ExecuteAsync"/>; the
/// <c>commands</c> stage applies it under the world gate at the start of the next step, at the tick time, and records
/// it in the command log. <see cref="Name"/> is the type name in kebab case without the <c>Command</c> suffix.
/// </summary>
public abstract record ZoneCommand : ISimulationCommand
{
    static readonly ConcurrentDictionary<Type, string> Names = new();

    public string Name => Names.GetOrAdd(GetType(), NameOf);

    /// <summary>Applies the command for <paramref name="session"/>; returns the rejection reason, or null.</summary>
    internal abstract string? Apply(ZoneWorld world, ZoneSession session, TickContext tick);

    /// <summary>The result of a world rule that runs under the gate, and so has completed when it returns.</summary>
    protected string? Completed(Task<string?> rule) =>
        rule.IsCompleted
            ? rule.GetAwaiter().GetResult()
            : throw new InvalidOperationException($"command {Name} did not complete synchronously under the world gate");

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
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryMoveAsync(session, DestX, DestY, DestZ, CancellationToken.None));
}

public sealed record StopMoveCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick)
    {
        world.StopMove(session);
        return null;
    }
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

public sealed record CancelTargetCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryCancelTargetAsync(session, CancellationToken.None));
}
