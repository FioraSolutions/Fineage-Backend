using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>Party, clan, pledge, friends, chat, block, and duel commands.</summary>
public sealed record PartyInviteCommand(int TargetObjectId, int ItemDistribution) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPartyInviteAsync(session, TargetObjectId, ItemDistribution, CancellationToken.None));
}

public sealed record PartyAnswerCommand(int RequestorObjectId, int Response) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPartyAnswerAsync(session, RequestorObjectId, Response, CancellationToken.None));
}

public sealed record PartyLeaveCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPartyLeaveAsync(session, CancellationToken.None));
}

public sealed record PartyKickCommand(int TargetObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPartyKickAsync(session, TargetObjectId, CancellationToken.None));
}

public sealed record PartyChangeLeaderCommand(int TargetObjectId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPartyChangeLeaderAsync(session, TargetObjectId, CancellationToken.None));
}

public sealed record CommandChannelAskCommand(string TargetName) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryCommandChannelAskAsync(session, TargetName, CancellationToken.None));
}

public sealed record CommandChannelAcceptCommand(int Response) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryCommandChannelAcceptAsync(session, Response, CancellationToken.None));
}

public sealed record CommandChannelOustCommand(string TargetName) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryCommandChannelOustAsync(session, TargetName, CancellationToken.None));
}

public sealed record FriendInviteCommand(FriendInviteMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryFriendInviteAsync(session, Request, CancellationToken.None));
}

public sealed record FriendAnswerCommand(FriendAnswerMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryFriendAnswerAsync(session, Request, CancellationToken.None));
}

public sealed record FriendListCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryFriendListAsync(session, CancellationToken.None));
}

public sealed record FriendDeleteCommand(FriendDeleteMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryFriendDeleteAsync(session, Request, CancellationToken.None));
}

public sealed record BlockRequestCommand(BlockRequestMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryBlockRequestAsync(session, Request, CancellationToken.None));
}

public sealed record ClanCreateCommand(ClanCreateRequestMsg Request) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryClanCreateAsync(session, Request, WallTime(tick), CancellationToken.None));
}

public sealed record ClanInviteCommand(ClanInviteRequestMsg Invite) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryClanInviteAsync(session, Invite, WallTime(tick), CancellationToken.None));
}

public sealed record ClanAnswerCommand(ClanAnswerJoinRequestMsg Answer) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryClanAnswerAsync(session, Answer, WallTime(tick), CancellationToken.None));
}

public sealed record ClanLeaveCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryClanLeaveAsync(session, WallTime(tick), CancellationToken.None));
}

public sealed record ClanKickCommand(ClanKickRequestMsg Kick) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryClanKickAsync(session, Kick, WallTime(tick), CancellationToken.None));
}

public sealed record ClanDissolveCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryClanDissolveAsync(session, WallTime(tick), CancellationToken.None));
}

public sealed record ClanCancelDissolveCommand : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryClanCancelDissolveAsync(session, WallTime(tick), CancellationToken.None));
}

public sealed record PledgeCommand(PledgeCommandNetMsg Command) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryPledgeCommandAsync(session, Command, WallTime(tick), CancellationToken.None));
}

public sealed record CastleCommand(CastleCommandNetMsg Command) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryCastleCommandAsync(session, Command, WallTime(tick), CancellationToken.None));
}

public sealed record ManorCommand(ManorCommandNetMsg Command) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryManorCommandAsync(session, Command, CancellationToken.None));
}

public sealed record SummonCommand(SummonCommandNetMsg Command) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySummonCommandAsync(session, Command, CancellationToken.None));
}

public sealed record DuelCommand(DuelCommandNetMsg Command) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryDuelCommandAsync(session, Command, CancellationToken.None));
}

public sealed record SayCommand(string? Text, int ChatType, string? Target) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TrySayAsync(session, Text, ChatType, TickTime(tick), CancellationToken.None, Target));
}

public sealed record WeddingEngageAnswerCommand(int Response) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        Completed(world.TryWeddingEngageAnswerAsync(session, Response, CancellationToken.None));
}

public sealed record UserCommandCommand(int CommandId) : ZoneCommand
{
    internal override string? Apply(ZoneWorld world, ZoneSession session, TickContext tick) =>
        world.ApplyUserCommand(session, CommandId);
}