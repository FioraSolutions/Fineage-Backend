using System.Globalization;
using System.Text;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Clan;
using Fineage.Zone.Olympiad;
using Fineage.Zone.Punishment;
using Fineage.Zone.Siege;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>One clan-war list. The command id selects the row; the world does not switch on the id.</summary>
    sealed class ClanWarQuery
    {
        public ClanWarQuery(int id, int header, Func<ZoneClan, ZoneClan, bool> match)
        {
            Id = id;
            Header = header;
            Match = match;
        }

        public int Id { get; }
        public int Header { get; }
        public Func<ZoneClan, ZoneClan, bool> Match { get; }
    }

    static readonly ClanWarQuery[] ClanWarQueries =
    [
        new(UserCommandProtocol.ClanWarDeclared, UserCommandProtocol.WarsDeclared,
            (self, other) => self.D.WarTargets.Contains(other.Id) && !other.D.WarTargets.Contains(self.Id)),
        new(UserCommandProtocol.ClanWarAgainst, UserCommandProtocol.WarsAgainst,
            (self, other) => other.D.WarTargets.Contains(self.Id) && !self.D.WarTargets.Contains(other.Id)),
        new(UserCommandProtocol.ClanWarList, UserCommandProtocol.WarList,
            (self, other) => self.D.WarTargets.Contains(other.Id) && other.D.WarTargets.Contains(self.Id))
    ];

    static readonly Dictionary<int, int> PartyLootMessages = new()
    {
        [(int)PartyDistributionType.FindersKeepers] = UserCommandProtocol.LootFindersKeepers,
        [(int)PartyDistributionType.Random] = UserCommandProtocol.LootRandom,
        [(int)PartyDistributionType.RandomIncludingSpoil] = UserCommandProtocol.LootRandomSpoil,
        [(int)PartyDistributionType.ByTurn] = UserCommandProtocol.LootByTurn,
        [(int)PartyDistributionType.ByTurnIncludingSpoil] = UserCommandProtocol.LootByTurnSpoil
    };

    public Task<string?> TryUserCommandAsync(ZoneSession session, int commandId, CancellationToken ct)
    {
        _ = ct;
        using (_affinity.Step())
            return Task.FromResult(ApplyUserCommand(session, commandId));
    }

    internal string? ApplyUserCommand(ZoneSession session, int commandId)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        if (player.Session == null)
        {
            ZoneLog.Error($"[user-command] obj={player.ObjectId} has no session");
            return UserCommandProtocol.Refused;
        }

        return UserCommandRegistry.Default.Dispatch(this, player, commandId);
    }

    internal string? UserLoc(ZoneEntity player)
    {
        var session = player.Session!;
        int locId = RegionLocId(player);
        if (locId > 0)
        {
            SelfLine(session, locId, "Current location",
                player.X.ToString(CultureInfo.InvariantCulture),
                player.Y.ToString(CultureInfo.InvariantCulture),
                player.Z.ToString(CultureInfo.InvariantCulture));
            return null;
        }

        ZoneLog.Error($"[user-command] loc obj={player.ObjectId} has no map region at {player.X},{player.Y}");
        SelfLine(session, 0, $"Current location: {player.X}, {player.Y}, {player.Z}");
        return null;
    }

    internal string? UserUnstuck(ZoneEntity player)
    {
        var session = player.Session!;
        long now = NowUnixMs();
        if (PlayerZoneChecks.IsPlayerJailed(this, player.ObjectId))
            return RefuseUnstuck(player, "jailed");
        if (InOlympiadArena(player.ObjectId) || PlayerInOlympiad(player))
        {
            SelfLine(session, UserCommandProtocol.OlympiadSkillBlocked, "You cannot use that skill in a Grand Olympiad Games match.");
            return RefuseUnstuck(player, "olympiad");
        }
        if (player.Dead)
            return RefuseUnstuck(player, "dead");
        if (now < player.CastingUntilMs)
            return RefuseUnstuck(player, "casting");
        if (IsSilenced(player) || IsPhysicallyMuted(player) || IsActionBlocked(player))
            return RefuseUnstuck(player, "muted");
        if (OlympiadObserving(player.ObjectId))
            return RefuseUnstuck(player, "observer");
        if (player.Sitting)
            return RefuseUnstuck(player, "sitting");

        bool gm = ZoneAdmin.IsGmCharacter(player.Name);
        // Mobius Unstuck casts 2100 (1 s) for a GM and 2099 (5 min) otherwise; the hit time is the skill's.
        var skill = CatalogCastSkill(gm ? UserCommandProtocol.GmUnstuckSkillId : UserCommandProtocol.PlayerUnstuckSkillId, "/unstuck");
        if (!gm)
            SelfLine(session, UserCommandProtocol.UnstuckFiveMinutes,
                "You are stuck. You will be transported to the nearest village in five minutes.");

        var (x, y, z) = TownDestination(player);
        BeginTownTeleport(player, skill, 1, 0, now, () =>
        {
            if (player.Dead)
                return;
            Relocate(player, x, y, z);
            SelfUserInfo(player);
        });
        return null;
    }

    static string RefuseUnstuck(ZoneEntity player, string reason)
    {
        ZoneLog.Error($"[user-command] unstuck refused obj={player.ObjectId} reason={reason}");
        return UserCommandProtocol.Refused;
    }

    internal string? UserMount(ZoneEntity player)
    {
        var session = player.Session!;
        if (PetEntityOf(player) is not { Summon.Pet: { } pet } summon)
        {
            ZoneLog.Error($"[user-command] mount obj={player.ObjectId} has no summoned pet");
            return UserCommandProtocol.Refused;
        }

        if (!pet.Template.Mountable)
        {
            ZoneLog.Error($"[user-command] mount obj={player.ObjectId} pet npc={pet.Template.NpcId} is not mountable");
            return MountProtocol.Blocked;
        }

        return TryMountFromPet(session, player, summon, pet);
    }

    internal string? UserDismount(ZoneEntity player)
    {
        if (player.Mount == null)
        {
            ZoneLog.Error($"[user-command] dismount obj={player.ObjectId} is not mounted");
            return UserCommandProtocol.Refused;
        }

        Dismount(player);
        return null;
    }

    internal string? UserTime(ZoneEntity player)
    {
        long now = NowUnixMs();
        int minutes = _gameTime.GameMinutes(now);
        int hour = minutes / 60;
        int minute = minutes % 60;
        bool night = _gameTime.IsNight(now);
        int id = night ? UserCommandProtocol.TimeNight : UserCommandProtocol.TimeDay;
        string text = night
            ? $"The current time is {hour}:{minute:00} in the night."
            : $"The current time is {hour}:{minute:00} in the day.";
        SelfLine(player.Session!, id, text,
            hour.ToString(CultureInfo.InvariantCulture),
            minute.ToString("00", CultureInfo.InvariantCulture));
        return null;
    }

    internal string? UserPartyInfo(ZoneEntity player)
    {
        var session = player.Session!;
        SelfLine(session, UserCommandProtocol.PartyInformation, "<Party Information>");
        var party = _party.Of(player.ObjectId);
        if (party == null)
            return null;
        if (!PartyLootMessages.TryGetValue(party.ItemDistribution, out int lootId))
        {
            ZoneLog.Error($"[user-command] party {party.PartyId} has unknown loot mode {party.ItemDistribution}");
            return UserCommandProtocol.Refused;
        }

        SelfLine(session, lootId, "Looting method");
        if (party.LeaderObjectId != player.ObjectId)
            SelfLine(session, UserCommandProtocol.PartyLeader, "Party Leader: $s1", LiveNameOr(party.LeaderObjectId, ""));
        return null;
    }

    internal string? UserClanWars(ZoneEntity player, int commandId)
    {
        var session = player.Session!;
        var clan = ClanOf(player.ObjectId);
        if (clan == null)
        {
            SelfLine(session, UserCommandProtocol.NotInClan, "Not joined in any clan.");
            return UserCommandProtocol.Refused;
        }

        ClanWarQuery? query = null;
        foreach (var row in ClanWarQueries)
        {
            if (row.Id == commandId)
            {
                query = row;
                break;
            }
        }

        if (query == null)
        {
            ZoneLog.Error($"[user-command] clan war list id {commandId} has no query");
            return UserCommandProtocol.Unknown;
        }

        SelfLine(session, query.Header, "Clan war list");
        foreach (var other in _clan.All)
        {
            if (other.Id == clan.Id || !query.Match(clan, other))
                continue;
            SendClanWarName(session, other);
        }

        return null;
    }

    void SendClanWarName(ZoneSession session, ZoneClan clan)
    {
        if (clan.D.AllyId > 0)
        {
            if (string.IsNullOrWhiteSpace(clan.D.AllyName))
                ZoneLog.Error($"[user-command] clan {clan.Id} ally {clan.D.AllyId} has no ally name");
            else
            {
                SelfLine(session, UserCommandProtocol.ClanWithAlly, "= $s1 ($s2 Alliance)", clan.Name, clan.D.AllyName);
                return;
            }
        }

        SelfLine(session, UserCommandProtocol.ClanWithoutAlly, "= $s1 (No alliance exists)", clan.Name);
    }

    internal string? UserChannelDelete(ZoneEntity player)
    {
        var party = _party.Of(player.ObjectId);
        if (party == null || party.LeaderObjectId != player.ObjectId)
        {
            ZoneLog.Error($"[user-command] channel delete obj={player.ObjectId} is not a party leader");
            return UserCommandProtocol.Refused;
        }

        var channel = _commandChannel.OfParty(party.PartyId);
        if (channel == null || channel.LeaderObjectId != player.ObjectId)
        {
            ZoneLog.Error($"[user-command] channel delete obj={player.ObjectId} is not the command-channel leader");
            return UserCommandProtocol.Refused;
        }

        var watchers = CommandChannelOnlineMembers(channel);
        DisbandCommandChannel(channel);
        SelfLineMany(watchers, UserCommandProtocol.ChannelDisbanded, "The Command Channel has been disbanded.");
        return null;
    }

    internal string? UserChannelLeave(ZoneEntity player)
    {
        var session = player.Session!;
        var party = _party.Of(player.ObjectId);
        if (party == null || party.LeaderObjectId != player.ObjectId)
        {
            SelfLine(session, UserCommandProtocol.ChannelLeaveLeaderOnly, "Only a party leader can choose the option to leave a channel.");
            return UserCommandProtocol.Refused;
        }

        var channel = _commandChannel.OfParty(party.PartyId);
        if (channel == null)
        {
            ZoneLog.Error($"[user-command] channel leave obj={player.ObjectId} party={party.PartyId} is not in a command channel");
            return UserCommandProtocol.Refused;
        }

        var remaining = CommandChannelOnlineMembers(channel).Where(id => !party.MemberIds.Contains(id)).ToArray();
        RemovePartyFromCommandChannel(party, channel);
        SelfLine(session, UserCommandProtocol.ChannelQuit, "You have quit the Command Channel.");
        SelfLineMany(remaining, UserCommandProtocol.ChannelPartyLeft, "$s1's party has left the Command Channel.", player.Name);
        return null;
    }

    internal string? UserChannelInfo(ZoneEntity player)
    {
        var party = _party.Of(player.ObjectId);
        var channel = party == null ? null : _commandChannel.OfParty(party.PartyId);
        if (channel == null)
        {
            ZoneLog.Error($"[user-command] channel info obj={player.ObjectId} is not in a command channel");
            return UserCommandProtocol.Refused;
        }

        FanCommandChannelInfo([player.ObjectId], BuildCommandChannelInfo(channel));
        return null;
    }

    internal string? UserSiegeStatus(ZoneEntity player)
    {
        var session = player.Session!;
        var clan = ClanOf(player.ObjectId);
        if (clan == null || clan.LeaderId != player.ObjectId || !_olympiad.Board.IsNoble(player.ObjectId))
        {
            SelfLine(session, UserCommandProtocol.SiegeStatusNoblesse,
                "Only a clan leader that is a Noblesse can view the Siege War Status window during a siege war.");
            return UserCommandProtocol.Refused;
        }

        CastleSiege? siege = null;
        foreach (var castle in _siege.Board.Castles)
        {
            if (!castle.InProgress)
                continue;
            if (_siege.Board.SideClans(castle.Id, attackers: true).Contains(clan.Id) ||
                _siege.Board.SideClans(castle.Id, attackers: false).Contains(clan.Id))
            {
                siege = castle;
                break;
            }
        }

        if (siege == null)
        {
            SelfLine(session, UserCommandProtocol.SiegeStatusNoblesse,
                "Only a clan leader that is a Noblesse can view the Siege War Status window during a siege war.");
            ZoneLog.Error($"[user-command] siege status obj={player.ObjectId} clan={clan.Id} has no siege in progress");
            return UserCommandProtocol.Refused;
        }

        ZoneLog.Error($"[user-command] siege status clan={clan.Id} has no siege kill/death counters; member list only");
        var html = new StringBuilder();
        html.Append("<html><body><center><table width=270><tr><td width=170>Member</td><td width=100>Location</td></tr>");
        foreach (var member in clan.Members)
        {
            string where = "offline";
            if (_byId.TryGetValue(member.CharacterId, out var online) && online.Session is { InWorld: true })
                where = ActiveSiegeCastle(online.X, online.Y, online.Z) == siege.Id
                    ? "Castle Siege in Progress"
                    : "No Castle Siege Area";
            html.Append("<tr><td width=170>");
            html.Append(member.Name);
            html.Append("</td><td width=100>");
            html.Append(where);
            html.Append("</td></tr>");
        }

        html.Append("</table></center></body></html>");
        NetBridge?.FanoutNpcHtml([session.ObjectId], new NpcHtmlMsg { Html = html.ToString() });
        return null;
    }

    internal string? UserClanPenalty(ZoneEntity player)
    {
        var session = player.Session!;
        long now = NowUnixMs();
        long join = JoinExpiry(player.ObjectId);
        long create = CreateExpiry(player.ObjectId, session.Account);
        var clan = ClanOf(player.ObjectId);
        long invite = clan?.CharPenaltyExpiryUnixMs ?? 0;
        var html = new StringBuilder();
        html.Append("<html><body><center><table width=270 border=0><tr><td width=170>Penalty</td><td width=100 align=center>Expiration Date</td></tr>");
        bool any = false;
        any |= AppendPenalty(html, "Unable to join a clan.", join, now);
        any |= AppendPenalty(html, "Unable to create a clan.", create, now);
        any |= AppendPenalty(html, "Unable to invite a clan member.", invite, now);
        if (!any)
            html.Append("<tr><td width=170>No penalty is imposed.</td><td width=100 align=center></td></tr>");
        html.Append("</table></center></body></html>");
        NetBridge?.FanoutNpcHtml([session.ObjectId], new NpcHtmlMsg { Html = html.ToString() });
        return null;
    }

    static bool AppendPenalty(StringBuilder html, string label, long expiryUnixMs, long now)
    {
        if (expiryUnixMs <= now)
            return false;
        string date = DateTimeOffset.FromUnixTimeMilliseconds(expiryUnixMs).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        html.Append("<tr><td width=170>");
        html.Append(label);
        html.Append("</td><td width=100 align=center>");
        html.Append(date);
        html.Append("</td></tr>");
        return true;
    }

    internal string? UserOlympiadStat(ZoneEntity player)
    {
        var session = player.Session!;
        if (!_olympiad.Board.Settings.Enabled)
        {
            SelfLine(session, UserCommandProtocol.OlympiadNotRunning, "The Grand Olympiad Games are not currently in progress.");
            return UserCommandProtocol.Refused;
        }

        if (!_olympiad.Board.IsNoble(player.ObjectId))
        {
            SelfLine(session, UserCommandProtocol.NoblesseOnly, "This command can only be used by a Noblesse.");
            return UserCommandProtocol.Refused;
        }

        if (!_olympiad.Board.TryRecord(player.ObjectId, out OlympiadRules.Score score))
        {
            ZoneLog.Error($"[user-command] olympiad record missing for obj={player.ObjectId}");
            score = default;
        }

        SelfLine(session, UserCommandProtocol.OlympiadRecord,
            "Your current record for this Grand Olympiad is $s1 match(es), $s2 win(s) and $s3 defeat(s). You have earned $s4 Olympiad Point(s).",
            score.Done.ToString(CultureInfo.InvariantCulture),
            score.Won.ToString(CultureInfo.InvariantCulture),
            score.Lost.ToString(CultureInfo.InvariantCulture),
            score.Points.ToString(CultureInfo.InvariantCulture));
        return null;
    }

    void SelfLineMany(IReadOnlyList<int> watchers, int id, string text, params string[] args)
    {
        if (watchers.Count == 0)
            return;
        ClanLine(watchers, id, text, args);
    }
}
