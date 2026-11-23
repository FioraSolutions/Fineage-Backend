using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Chat;

/// <summary>
/// One Interlude user command. The registry looks the id up; it does not branch on the id.
/// </summary>
internal abstract class UserCommandHandler
{
    public abstract int Id { get; }

    public abstract string? Apply(ZoneWorld world, ZoneEntity player);
}

internal sealed class UserCommandRegistry
{
    readonly Dictionary<int, UserCommandHandler> _byId;

    public UserCommandRegistry(IEnumerable<UserCommandHandler> handlers)
    {
        _byId = new Dictionary<int, UserCommandHandler>();
        foreach (var handler in handlers)
        {
            if (!_byId.TryAdd(handler.Id, handler))
                throw new InvalidOperationException($"duplicate user command id {handler.Id}");
        }
    }

    public static UserCommandRegistry Default { get; } = new(
    [
        new LocCommand(),
        new UnstuckCommand(),
        new MountCommand(),
        new DismountCommand(),
        new TimeCommand(),
        new PartyInfoCommand(),
        new ClanWarsCommand(UserCommandProtocol.ClanWarDeclared),
        new ClanWarsCommand(UserCommandProtocol.ClanWarAgainst),
        new ClanWarsCommand(UserCommandProtocol.ClanWarList),
        new ChannelDeleteCommand(),
        new ChannelLeaveCommand(),
        new ChannelInfoCommand(),
        new SiegeStatusCommand(),
        new ClanPenaltyCommand(),
        new OlympiadStatCommand()
    ]);

    public string? Dispatch(ZoneWorld world, ZoneEntity player, int commandId)
    {
        if (!_byId.TryGetValue(commandId, out var handler))
        {
            ZoneLog.Error($"[user-command] unknown command id {commandId} obj={player.ObjectId} — not applied");
            return UserCommandProtocol.Unknown;
        }

        return handler.Apply(world, player);
    }
}

internal sealed class LocCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.Loc;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserLoc(player);
}

internal sealed class UnstuckCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.Unstuck;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserUnstuck(player);
}

internal sealed class MountCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.Mount;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserMount(player);
}

internal sealed class DismountCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.Dismount;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserDismount(player);
}

internal sealed class TimeCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.Time;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserTime(player);
}

internal sealed class PartyInfoCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.PartyInfo;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserPartyInfo(player);
}

internal sealed class ClanWarsCommand : UserCommandHandler
{
    public ClanWarsCommand(int id) => Id = id;
    public override int Id { get; }
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserClanWars(player, Id);
}

internal sealed class ChannelDeleteCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.ChannelDelete;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserChannelDelete(player);
}

internal sealed class ChannelLeaveCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.ChannelLeave;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserChannelLeave(player);
}

internal sealed class ChannelInfoCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.ChannelInfo;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserChannelInfo(player);
}

internal sealed class SiegeStatusCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.SiegeStatus;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserSiegeStatus(player);
}

internal sealed class ClanPenaltyCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.ClanPenalty;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserClanPenalty(player);
}

internal sealed class OlympiadStatCommand : UserCommandHandler
{
    public override int Id => UserCommandProtocol.OlympiadStat;
    public override string? Apply(ZoneWorld world, ZoneEntity player) => world.UserOlympiadStat(player);
}
