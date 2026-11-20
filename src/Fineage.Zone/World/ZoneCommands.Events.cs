using Fineage.Protocol;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>Siege, castle, olympiad, seven signs, boss status, and admin event commands.</summary>
public sealed record BossStatusCommand(int NpcId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryBossStatusAsync(session, NpcId, CancellationToken.None));
}

public sealed record AdminBossCommand(AdminBossScheduleMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAdminBossAsync(session, Request, CancellationToken.None));
}

public sealed record SiegeListCommand(int CastleId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySiegeListAsync(session, CastleId, CancellationToken.None));
}

public sealed record SiegeRegisterCommand(SiegeRegisterRequestMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySiegeRegisterAsync(session, Request, CancellationToken.None));
}

public sealed record SiegeAnswerCommand(SiegeDefenderAnswerRequestMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySiegeAnswerAsync(session, Request, CancellationToken.None));
}

public sealed record AdminCaptureCommand(int CastleId, int ClanId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAdminCaptureAsync(session, CastleId, ClanId, CancellationToken.None));
}

public sealed record OlympiadStatusCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryOlympiadStatusAsync(session, CancellationToken.None));
}

public sealed record OlympiadRegisterCommand(OlympiadRegisterRequestMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryOlympiadRegisterAsync(session, Request, CancellationToken.None));
}

public sealed record OlympiadUnregisterCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryOlympiadUnregisterAsync(session, CancellationToken.None));
}

public sealed record OlympiadLeaveCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryOlympiadLeaveAsync(session, CancellationToken.None));
}

public sealed record AdminReportOlympiadCommand(int MatchId, int WinnerObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryAdminReportOlympiadAsync(session, MatchId, WinnerObjectId, CancellationToken.None));
}

public sealed record SevenSignsStatusCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySevenSignsStatusAsync(session, CancellationToken.None));
}

public sealed record SevenSignsActionCommand(SevenSignsActionRequestMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySevenSignsActionAsync(session, Request, CancellationToken.None));
}

public sealed record DungeonCommand(DungeonEnterRequestMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryDungeonAsync(session, Request, CancellationToken.None));
}