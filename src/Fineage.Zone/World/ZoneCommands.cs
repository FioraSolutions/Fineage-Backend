using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// A player input to the simulation. <see cref="ZoneWorld.ExecuteAsync"/> applies it under the world gate with one
/// pinned time sample and records it in the command log; until command ingress drains them at the start of a tick,
/// commands apply immediately, so they must complete synchronously.
/// </summary>
public abstract record ZoneCommand : ISimulationCommand
{
    public abstract string Name { get; }

    internal abstract Task<string?> ApplyAsync(ZoneWorld world, ZoneSession session, CancellationToken ct);
}

public sealed record MoveCommand(int DestX, int DestY, int DestZ) : ZoneCommand
{
    public override string Name => "move";

    internal override Task<string?> ApplyAsync(ZoneWorld world, ZoneSession session, CancellationToken ct) =>
        world.TryMoveAsync(session, DestX, DestY, DestZ, ct);
}

public sealed record StopMoveCommand : ZoneCommand
{
    public override string Name => "stop-move";

    internal override Task<string?> ApplyAsync(ZoneWorld world, ZoneSession session, CancellationToken ct)
    {
        world.StopMove(session);
        return Task.FromResult<string?>(null);
    }
}

public sealed record AttackCommand(int TargetObjectId, bool Forced) : ZoneCommand
{
    public override string Name => "attack";

    internal override Task<string?> ApplyAsync(ZoneWorld world, ZoneSession session, CancellationToken ct) =>
        world.TryAttackAsync(session, TargetObjectId, ct, Forced);
}

public sealed record SkillUseCommand(int SkillId, int TargetObjectId, bool CtrlPressed) : ZoneCommand
{
    public override string Name => "skill-use";

    internal override Task<string?> ApplyAsync(ZoneWorld world, ZoneSession session, CancellationToken ct) =>
        world.TrySkillUseAsync(session, SkillId, TargetObjectId, ct, CtrlPressed);
}

public sealed record SelectTargetCommand(int TargetObjectId) : ZoneCommand
{
    public override string Name => "select-target";

    internal override Task<string?> ApplyAsync(ZoneWorld world, ZoneSession session, CancellationToken ct) =>
        world.TrySelectTargetAsync(session, TargetObjectId, ct);
}

public sealed record CancelTargetCommand : ZoneCommand
{
    public override string Name => "cancel-target";

    internal override Task<string?> ApplyAsync(ZoneWorld world, ZoneSession session, CancellationToken ct) =>
        world.TryCancelTargetAsync(session, ct);
}
