using Fineage.Protocol;
using Fineage.Zone.Party;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly CommandChannelActor _commandChannel = new();

    public Task<string?> TryCommandChannelAskAsync(ZoneSession session, string targetName, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        if (string.IsNullOrWhiteSpace(targetName))
            return Task.FromResult<string?>("bad_request");
        return Task.FromResult<string?>(TryCommandChannelAsk(session, targetName.Trim()));
    }

    public Task<string?> TryCommandChannelAcceptAsync(ZoneSession session, int response, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        return Task.FromResult<string?>(TryCommandChannelAccept(session, response));
    }

    public Task<string?> TryCommandChannelOustAsync(ZoneSession session, string targetName, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        if (string.IsNullOrWhiteSpace(targetName))
            return Task.FromResult<string?>("bad_request");
        return Task.FromResult<string?>(TryCommandChannelOust(session, targetName.Trim()));
    }

    string? TryCommandChannelAsk(ZoneSession inviter, string targetName)
    {
        if (!IsLive(inviter) || !_byId.TryGetValue(inviter.ObjectId, out var inviterEntity))
            return "world";
        if (_party.Of(inviter.ObjectId) is not { } inviterParty)
            return "party";
        if (inviterParty.LeaderObjectId != inviter.ObjectId)
            return "command_channel_authority";

        var target = OnlinePlayerNamed(targetName);
        if (target == null)
        {
            ZoneLog.Warn($"command channel ask: target player {targetName} not online");
            return "target";
        }

        if (_party.Of(target.ObjectId) is not { } targetParty)
        {
            ZoneLog.Warn($"command channel ask: target {targetName} has no party");
            return "target_no_party";
        }

        if (ReferenceEquals(inviterParty, targetParty))
            return null;

        if (_commandChannel.InChannel(targetParty.PartyId))
            return "command_channel_member";

        if (_commandChannel.OfParty(inviterParty.PartyId) is { } existing)
        {
            if (existing.LeaderObjectId != inviter.ObjectId)
                return "command_channel_authority";
        }

        int targetLeaderId = targetParty.LeaderObjectId;
        if (_commandChannel.IsAsked(targetLeaderId) || _party.InInvite(targetLeaderId))
            return "busy";

        if (!CanFormCommandChannel(inviterEntity, out string gate))
        {
            ZoneLog.Warn($"command channel ask: formation gate failed for {inviter.ObjectId}: {gate}");
            return gate;
        }

        _commandChannel.Ask(targetLeaderId, inviter.ObjectId);
        FanCommandChannelAsk([targetLeaderId], new CommandChannelAskJoinMsg
        {
            RequestorName = inviter.Name,
            RequestorObjectId = inviter.ObjectId
        });
        return null;
    }

    string? TryCommandChannelAccept(ZoneSession acceptor, int response)
    {
        if (!IsLive(acceptor) || !_byId.TryGetValue(acceptor.ObjectId, out var acceptorEntity))
            return "world";
        if (_party.Of(acceptor.ObjectId) is not { } acceptorParty)
            return "party";
        if (acceptorParty.LeaderObjectId != acceptor.ObjectId)
            return "party_leader";

        if (!_commandChannel.TryTakeAsk(acceptor.ObjectId, out int requestorId))
            return "command_channel_invite";

        if (!_byId.TryGetValue(requestorId, out var requestorEntity) || requestorEntity.Session == null || !IsLive(requestorEntity.Session))
            return "command_channel_invite";

        if (response != 1)
            return null;

        if (_party.Of(requestorId) is not { } requestorParty)
        {
            ZoneLog.Warn($"command channel accept: requestor {requestorId} lost party");
            return "command_channel_invite";
        }

        if (_commandChannel.InChannel(acceptorParty.PartyId))
            return "command_channel_member";

        int requestorLevel = PartyMaxLevel(requestorParty);
        int acceptorLevel = PartyMaxLevel(acceptorParty);
        bool created = false;
        ZoneCommandChannel channel;
        if (_commandChannel.OfParty(requestorParty.PartyId) is not { } existing)
        {
            channel = _commandChannel.Create(requestorId, requestorParty.PartyId, requestorLevel);
            created = true;
        }
        else
        {
            channel = existing;
        }

        _commandChannel.AddParty(channel, acceptorParty.PartyId, acceptorLevel);
        BroadcastCommandChannelJoined(channel, created, acceptorParty);
        if (!created)
            FanCommandChannelInfoToChannel(channel);
        return null;
    }

    string? TryCommandChannelOust(ZoneSession ccLeader, string targetName)
    {
        if (!IsLive(ccLeader) || !_byId.TryGetValue(ccLeader.ObjectId, out _))
            return "world";
        if (_party.Of(ccLeader.ObjectId) is not { } leaderParty)
            return "party";
        if (_commandChannel.OfParty(leaderParty.PartyId) is not { } channel)
            return "command_channel";
        if (channel.LeaderObjectId != ccLeader.ObjectId)
            return "command_channel_authority";

        var target = OnlinePlayerNamed(targetName);
        if (target == null || _party.Of(target.ObjectId) is not { } targetParty)
            return "target";
        if (ReferenceEquals(leaderParty, targetParty))
            return "bad_request";
        if (_commandChannel.OfParty(targetParty.PartyId)?.ChannelId != channel.ChannelId)
            return "target";

        RemovePartyFromCommandChannel(targetParty);
        return null;
    }

    bool CanFormCommandChannel(ZoneEntity requestor, out string error)
    {
        int clanLevel = requestor.ClanId == 0 ? 0 : ClanOf(requestor.ObjectId)?.Level ?? 0;
        long guides = requestor.Inventory?.CountOf(CommandChannelProtocol.StrategyGuideItemId) ?? 0;
        int imperium = requestor.KnownSkills.GetValueOrDefault(CommandChannelProtocol.ClanImperiumSkillId);
        if (CommandChannelRules.CanForm(requestor.ClanLeader, clanLevel, requestor.PledgeClass, guides, imperium))
        {
            error = "";
            return true;
        }

        error = "command_channel_formation";
        return false;
    }

    void CommandChannelPlayerLeft(int objectId) => _commandChannel.DropAsksOf(objectId);

    void CommandChannelOnPartyEmpty(ZoneParty party)
    {
        if (_commandChannel.OfParty(party.PartyId) is not { } channel)
            return;
        RemovePartyFromCommandChannel(party, channel);
    }

    void CommandChannelOnPartyShrunkToOne(ZoneParty party)
    {
        if (_commandChannel.OfParty(party.PartyId) is not { } channel)
            return;
        if (channel.LeaderObjectId == party.LeaderObjectId)
            DisbandCommandChannel(channel);
        else
            RemovePartyFromCommandChannel(party, channel);
    }

    void RemovePartyFromCommandChannel(ZoneParty party, ZoneCommandChannel? channel = null)
    {
        channel ??= _commandChannel.OfParty(party.PartyId);
        if (channel == null)
            return;

        bool leaderParty = PartyContains(channel.LeaderObjectId, party);
        var otherPartyIds = channel.PartyIds.Where(id => id != party.PartyId).ToList();
        int maxLevel = MaxLevelAmongParties(channel, excludePartyId: party.PartyId);
        bool disbanded = _commandChannel.RemoveParty(channel, party.PartyId, maxLevel);

        FanCommandChannelClose(PartyOnlineMembers(party));
        if (disbanded)
        {
            foreach (int partyId in otherPartyIds)
            {
                if (_party.PartyById(partyId) is { } remaining)
                    FanCommandChannelClose(PartyOnlineMembers(remaining));
            }

            return;
        }

        if (leaderParty && channel.PartyIds.Count > 0)
        {
            int firstPartyId = channel.PartyIds[0];
            if (_party.PartyById(firstPartyId) is { } nextLeaderParty)
            {
                _commandChannel.SetLeader(channel, nextLeaderParty.LeaderObjectId, PartyMaxLevel(nextLeaderParty));
                FanCommandChannelInfoToChannel(channel);
            }
            else
                ZoneLog.Warn($"command channel: party {firstPartyId} missing after leader party removal");
        }
        else
            FanCommandChannelInfoToChannel(channel);
    }

    void DisbandCommandChannel(ZoneCommandChannel channel)
    {
        foreach (int partyId in channel.PartyIds.ToList())
        {
            if (_party.PartyById(partyId) is { } p)
                FanCommandChannelClose(PartyOnlineMembers(p));
        }

        _commandChannel.Disband(channel);
    }

    int MaxLevelAmongParties(ZoneCommandChannel channel, int excludePartyId)
    {
        int max = 0;
        foreach (int partyId in channel.PartyIds)
        {
            if (partyId == excludePartyId)
                continue;
            if (_party.PartyById(partyId) is { } p)
                max = Math.Max(max, PartyMaxLevel(p));
        }

        return max;
    }

    static bool PartyContains(int objectId, ZoneParty party) => party.MemberIds.Contains(objectId);

    void BroadcastCommandChannelJoined(ZoneCommandChannel channel, bool created, ZoneParty joinedParty)
    {
        foreach (int partyId in channel.PartyIds)
        {
            if (_party.PartyById(partyId) is not { } p)
            {
                ZoneLog.Warn($"command channel join: party {partyId} missing on roster");
                continue;
            }

            var watchers = PartyOnlineMembers(p);
            FanCommandChannelOpen(watchers);
            FanCommandChannelInfo(watchers, BuildCommandChannelInfo(channel));
        }

        _ = created;
        _ = joinedParty;
    }

    void FanCommandChannelInfoToChannel(ZoneCommandChannel channel)
    {
        var info = BuildCommandChannelInfo(channel);
        foreach (int partyId in channel.PartyIds)
        {
            if (_party.PartyById(partyId) is { } p)
                FanCommandChannelInfo(PartyOnlineMembers(p), info);
        }
    }

    CommandChannelInfoMsg BuildCommandChannelInfo(ZoneCommandChannel channel)
    {
        string leaderName = LiveNameOr(channel.LeaderObjectId, "");
        int members = 0;
        var parties = new List<CommandChannelPartyRowMsg>();
        foreach (int partyId in channel.PartyIds)
        {
            if (_party.PartyById(partyId) is not { } party)
            {
                ZoneLog.Warn($"command channel info: party {partyId} not found");
                continue;
            }

            members += party.MemberIds.Count;
            parties.Add(new CommandChannelPartyRowMsg
            {
                LeaderName = LiveNameOr(party.LeaderObjectId, ""),
                LeaderObjectId = party.LeaderObjectId,
                MemberCount = party.MemberIds.Count
            });
        }

        return new CommandChannelInfoMsg
        {
            LeaderName = leaderName,
            LootType = 0,
            MemberCount = members,
            Parties = parties.ToArray()
        };
    }

    IReadOnlyList<int> PartyOnlineMembers(ZoneParty party)
    {
        var list = new List<int>(party.MemberIds.Count);
        foreach (int id in party.MemberIds)
        {
            if (Online(id) != null)
                list.Add(id);
        }

        return list;
    }

    IReadOnlyList<int> CommandChannelOnlineMembers(ZoneCommandChannel channel)
    {
        var list = new List<int>();
        foreach (int partyId in channel.PartyIds)
        {
            if (_party.PartyById(partyId) is { } party)
            {
                foreach (int id in party.MemberIds)
                {
                    if (Online(id) != null)
                        list.Add(id);
                }
            }
        }

        return list;
    }

    void FanCommandChannelAsk(IReadOnlyList<int> watchers, CommandChannelAskJoinMsg ask) =>
        NetBridge?.FanoutCommandChannelAskJoin(watchers, ask);

    void FanCommandChannelOpen(IReadOnlyList<int> watchers) =>
        NetBridge?.FanoutCommandChannelOpen(watchers);

    void FanCommandChannelClose(IReadOnlyList<int> watchers) =>
        NetBridge?.FanoutCommandChannelClose(watchers);

    void FanCommandChannelInfo(IReadOnlyList<int> watchers, CommandChannelInfoMsg info) =>
        NetBridge?.FanoutCommandChannelInfo(watchers, info);
}