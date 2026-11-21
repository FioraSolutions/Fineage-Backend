using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>Npc talk, bypass, quests, skill acquire, revive, and character action commands.</summary>
public sealed record NpcTalkCommand(int NpcObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryNpcTalkAsync(session, NpcObjectId, CancellationToken.None));
}

public sealed record QuestListCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryQuestListAsync(session, CancellationToken.None));
}

public sealed record QuestAbortCommand(int QuestId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryQuestAbortAsync(session, QuestId, CancellationToken.None));
}

public sealed record AcquireSkillCommand(int SkillId, int Level, int SkillType, bool Informing) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAcquireSkillAsync(session, SkillId, Level, SkillType, Informing, CancellationToken.None));
}

public sealed record CharacterActionCommand(int ActionId, int TargetObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryCharacterActionAsync(session, ActionId, TargetObjectId, CancellationToken.None));
}

public sealed record ReviveCommand(int Action) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryReviveAsync(session, Action, CancellationToken.None));
}

public sealed record BypassCommand(int NpcObjectId, string? Command) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryBypassAsync(session, NpcObjectId, Command, CancellationToken.None));
}