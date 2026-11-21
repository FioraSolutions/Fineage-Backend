namespace Fineage.Zone.Party;

internal sealed class ZoneCommandChannel
{
    public required int ChannelId { get; init; }
    public int LeaderObjectId { get; set; }
    public int ChannelLevel { get; set; }
    public List<int> PartyIds { get; } = [];
}

/// <summary>In-memory command channels keyed by party id, with pending MPCC invites.</summary>
internal sealed class CommandChannelActor
{
    readonly SortedDictionary<int, ZoneCommandChannel> _channels = [];
    readonly Dictionary<int, int> _partyToChannel = [];
    readonly Dictionary<int, int> _pendingAsk = [];
    int _nextChannelId = 1;

    public ZoneCommandChannel? OfParty(int partyId) =>
        _partyToChannel.TryGetValue(partyId, out int channelId) && _channels.TryGetValue(channelId, out var channel)
            ? channel
            : null;

    public bool InChannel(int partyId) => _partyToChannel.ContainsKey(partyId);

    public int? LeaderObjectIdOfParty(int partyId) => OfParty(partyId)?.LeaderObjectId;

    public void Ask(int inviteePartyLeaderId, int requestorObjectId) =>
        _pendingAsk[inviteePartyLeaderId] = requestorObjectId;

    public bool IsAsked(int partyLeaderObjectId) => _pendingAsk.ContainsKey(partyLeaderObjectId);

    public bool TryTakeAsk(int inviteePartyLeaderId, out int requestorObjectId)
    {
        if (_pendingAsk.TryGetValue(inviteePartyLeaderId, out requestorObjectId))
        {
            _pendingAsk.Remove(inviteePartyLeaderId);
            return true;
        }

        requestorObjectId = 0;
        return false;
    }

    public void DropAsksOf(int objectId)
    {
        _pendingAsk.Remove(objectId);
        foreach (var (leaderId, requestorId) in _pendingAsk.ToList())
        {
            if (requestorId == objectId)
                _pendingAsk.Remove(leaderId);
        }
    }

    public ZoneCommandChannel Create(int leaderObjectId, int leaderPartyId, int partyLevel)
    {
        var channel = new ZoneCommandChannel
        {
            ChannelId = _nextChannelId++,
            LeaderObjectId = leaderObjectId,
            ChannelLevel = partyLevel
        };
        channel.PartyIds.Add(leaderPartyId);
        _channels[channel.ChannelId] = channel;
        _partyToChannel[leaderPartyId] = channel.ChannelId;
        return channel;
    }

    public void AddParty(ZoneCommandChannel channel, int partyId, int partyLevel)
    {
        if (_partyToChannel.ContainsKey(partyId))
            throw new InvalidOperationException($"party {partyId} is already in command channel {_partyToChannel[partyId]}");
        channel.PartyIds.Add(partyId);
        _partyToChannel[partyId] = channel.ChannelId;
        if (partyLevel > channel.ChannelLevel)
            channel.ChannelLevel = partyLevel;
    }

    /// <summary>Removes a party; recalculates channel level; returns true if the channel was disbanded.</summary>
    public bool RemoveParty(ZoneCommandChannel channel, int partyId, int newMaxPartyLevel)
    {
        channel.PartyIds.Remove(partyId);
        _partyToChannel.Remove(partyId);
        channel.ChannelLevel = newMaxPartyLevel;
        if (channel.PartyIds.Count < 2)
        {
            Disband(channel);
            return true;
        }

        return false;
    }

    public void SetLeader(ZoneCommandChannel channel, int leaderObjectId, int leaderLevel)
    {
        channel.LeaderObjectId = leaderObjectId;
        if (leaderLevel > channel.ChannelLevel)
            channel.ChannelLevel = leaderLevel;
    }

    public void Disband(ZoneCommandChannel channel)
    {
        foreach (int partyId in channel.PartyIds.ToList())
            _partyToChannel.Remove(partyId);
        channel.PartyIds.Clear();
        _channels.Remove(channel.ChannelId);
    }
}