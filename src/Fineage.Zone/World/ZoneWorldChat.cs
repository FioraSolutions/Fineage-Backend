using Fineage.Protocol;
using Fineage.Zone.Chat;

namespace Fineage.Zone.World;

/// <summary>
/// Say2 channels (Mobius CT_0 chat handlers, reimplemented): who hears each line, and the refusals the speaker gets.
/// Audiences: All = visible players within 1250; Shout / Trade = same map region (MapRegion locId) and instance;
/// Tell = one online player by name; Party / Clan = online members; Hero = everyone; Alliance / Petition / party
/// rooms are refused or ignored until those systems exist. Every receiver drops lines from players it blocks
/// (block list or message refusal), as Mobius <c>BlockList.isBlocked(receiver, speaker)</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius GeneralConfig.BAN_CHAT_CHANNELS default: GENERAL;SHOUT;TRADE;HERO_VOICE;WHISPER.</summary>
    static bool BanCovers(int chatType) =>
        chatType is ChatProtocol.All or ChatProtocol.Shout or ChatProtocol.Trade or ChatProtocol.Hero or ChatProtocol.Tell;

    public Task<string?> TrySayAsync(
        ZoneSession session, string? text, int chatType, DateTimeOffset now, CancellationToken ct, string? target = null)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        if (!SayRules.TryNormalize(text, out string normalized))
            return Task.FromResult<string?>("bad_request");

        SystemMessageMsg? notice = null;
        lock (_gate)
        {
            if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var speaker) || speaker.Session == null)
                return Task.FromResult<string?>("world");

            long nowMs = now.ToUnixTimeMilliseconds();
            if (SayRules.IsFlood(session.LastChatUnixMs, nowMs))
            {
                notice = SayRules.Flood();
            }
            else if (!SayRules.IsDelivered(chatType))
            {
                session.LastChatUnixMs = nowMs;
                notice = SayRules.NotSupported(chatType);
            }
            else if (session.ChatBannedUntilUnixMs > nowMs && BanCovers(chatType))
            {
                // Mobius Say2.runImpl: a chat-banned player gets CHATTING_IS_CURRENTLY_PROHIBITED on banned channels.
                notice = new SystemMessageMsg { Id = ChatProtocol.ChattingProhibited, Params = [], Text = "Chatting is currently prohibited." };
            }
            else
            {
                session.LastChatUnixMs = nowMs;
                notice = Deliver(speaker, chatType, normalized, target?.Trim() ?? "", nowMs);
            }
        }

        if (notice != null)
        {
            NetBridge?.FanoutSystemMessage(
                [session.ObjectId],
                new SystemMessageMsg
                {
                    Id = notice.Id,
                    Text = notice.Text,
                    Params = notice.Params ?? []
                });

            return Task.FromResult<string?>(null);
        }

        ZoneLog.Info($"say object={session.ObjectId} name={session.Name} type={chatType} text={normalized}");
        return Task.FromResult<string?>(null);
    }

    static SystemMessageMsg Plain(string text) => new() { Id = ChatProtocol.SystemPlain, Params = [], Text = text };

    /// <summary>Routes one accepted line; returns the refusal for the speaker, or null when it went out.</summary>
    SystemMessageMsg? Deliver(ZoneEntity speaker, int chatType, string text, string target, long nowMs)
    {
        switch (chatType)
        {
            case ChatProtocol.All:
                SayLocal(speaker, text);
                return null;
            case ChatProtocol.Shout:
                SayRegion(speaker, chatType, text, speakerMustNotBlock: true);
                return null;
            case ChatProtocol.Trade:
                if (speaker.Level < ChatProtocol.TradeMinLevel)
                    return Plain($"Players can use Trade chat after Lv. {ChatProtocol.TradeMinLevel}.");
                SayRegion(speaker, chatType, text, speakerMustNotBlock: false);
                return null;
            case ChatProtocol.Tell:
                return Whisper(speaker, target, text, nowMs);
            case ChatProtocol.Party:
                if (!_partyByPlayer.TryGetValue(speaker.ObjectId, out int partyId) || !_parties.TryGetValue(partyId, out var party))
                    return Plain("You are not in a party.");
                SayToGroup(speaker, chatType, text, party.MemberIds);
                return null;
            case ChatProtocol.Clan:
                if (speaker.ClanId == 0)
                    return Plain("You are not in a clan.");
                SayToGroup(speaker, chatType, text,
                    Players().Where(p => p.ClanId == speaker.ClanId).Select(p => p.ObjectId));
                return null;
            case ChatProtocol.Alliance:
                // No alliances on this server yet: Mobius answers exactly this to a clan without an ally.
                return Plain("You are not in an alliance.");
            case ChatProtocol.Hero:
                if (!speaker.Hero)
                    return Plain("Only Heroes can enter the Hero channel.");
                if (speaker.Session!.LastHeroVoiceUnixMs > 0 && nowMs - speaker.Session.LastHeroVoiceUnixMs < ChatProtocol.HeroVoiceIntervalMs)
                    return Plain("Action failed. Heroes are only able to speak in the global channel once every 10 seconds.");
                speaker.Session.LastHeroVoiceUnixMs = nowMs;
                SayToGroup(speaker, chatType, text, Players().Select(p => p.ObjectId));
                return null;
            case ChatProtocol.PetitionPlayer:
            case ChatProtocol.PetitionGm:
                return new SystemMessageMsg { Id = ChatProtocol.NotInPetitionChat, Params = [], Text = "You are currently not in a petition chat." };
            default:
                // Party match room / command channel: Mobius returns silently when the speaker has none.
                return null;
        }
    }

    CreatureSayMsg Line(ZoneEntity speaker, int chatType, string text, string? name = null) => new()
    {
        ObjectId = speaker.ObjectId,
        Name = name ?? speaker.Name,
        Text = text,
        ChatType = chatType
    };

    /// <summary>Mobius ChatGeneral: visible players within 1250 that do not block the speaker, plus the speaker.</summary>
    void SayLocal(ZoneEntity speaker, string text)
    {
        List<int> netWatchers = [speaker.ObjectId];
        foreach (var entity in Players())
        {
            if (entity.Session == null || ReferenceEquals(entity, speaker))
                continue;
            if (HearsLocal(speaker, entity) && !IsBlocked(entity, speaker))
                netWatchers.Add(entity.ObjectId);
        }

        NetBridge?.FanoutCreatureSay(netWatchers, Line(speaker, ChatProtocol.All, text));
    }

    /// <summary>
    /// Mobius ChatShout / ChatTrade (DEFAULT_GLOBAL_CHAT / DEFAULT_TRADE_CHAT = ON): every player in the speaker's map
    /// region (<c>getMapRegionLocId</c>) and instance that does not block the speaker. Shout also skips players the
    /// speaker blocks. The speaker is one of the region's players, so the same rules decide its own echo.
    /// </summary>
    void SayRegion(ZoneEntity speaker, int chatType, string text, bool speakerMustNotBlock)
    {
        int region = RegionLocId(speaker);
        List<int>? netWatchers = null;
        foreach (var entity in Players())
        {
            if (entity.Session == null || RegionLocId(entity) != region || !SameInstance(speaker.ObjectId, entity.ObjectId))
                continue;
            if (IsBlocked(entity, speaker) || (speakerMustNotBlock && IsBlocked(speaker, entity)))
                continue;
            netWatchers ??= [];
            netWatchers.Add(entity.ObjectId);
        }

        if (netWatchers != null)
            NetBridge?.FanoutCreatureSay(netWatchers, Line(speaker, chatType, text));
    }

    int RegionLocId(ZoneEntity entity) => _mapRegions.At(entity.X, entity.Y)?.LocId ?? 0;

    /// <summary>Mobius AbstractPlayerGroup.broadcastCreatureSay / Clan.broadcastCSToOnlineMembers.</summary>
    void SayToGroup(ZoneEntity speaker, int chatType, string text, IEnumerable<int> memberIds)
    {
        List<int>? netWatchers = null;
        foreach (int id in memberIds)
        {
            if (!_byId.TryGetValue(id, out var member) || member.Kind != ZoneEntityKind.Player || member.Session == null)
                continue;
            if (IsBlocked(member, speaker))
                continue;
            netWatchers ??= [];
            netWatchers.Add(member.ObjectId);
        }

        if (netWatchers != null)
            NetBridge?.FanoutCreatureSay(netWatchers, Line(speaker, chatType, text));
    }

    /// <summary>
    /// Mobius ChatWhisper: online receiver by name (case-insensitive). Offline / unknown → 145; receiver chat-banned,
    /// blocking the speaker or in message refusal → 176. Receiver sees the speaker's name, the speaker sees
    /// <c>-&gt;Receiver</c>.
    /// </summary>
    SystemMessageMsg? Whisper(ZoneEntity speaker, string target, string text, long nowMs)
    {
        if (target.Length == 0)
            return null;
        ZoneEntity? receiver = null;
        foreach (var p in Players())
        {
            if (string.Equals(p.Name, target, StringComparison.OrdinalIgnoreCase))
            {
                receiver = p;
                break;
            }
        }

        if (receiver?.Session == null || !receiver.Session.InWorld)
            return new SystemMessageMsg { Id = ChatProtocol.PlayerNotOnline, Params = [], Text = "That player is not online." };
        var refusal = new SystemMessageMsg { Id = ChatProtocol.MessageRefusal, Params = [], Text = "That person is in message refusal mode." };
        if (receiver.Session.ChatBannedUntilUnixMs > nowMs || IsBlocked(receiver, speaker))
            return refusal;

        NetBridge?.FanoutCreatureSay([receiver.ObjectId], Line(speaker, ChatProtocol.Tell, text));
        NetBridge?.FanoutCreatureSay([speaker.ObjectId], Line(speaker, ChatProtocol.Tell, text, "->" + receiver.Name));
        return null;
    }

    /// <summary>
    /// Mobius <c>World.announce</c> / AnnouncementsTable: <c>CreatureSay</c> type 10 (or 18 critical) with object 0 and
    /// an empty name to every online player. Server-side only (no GM commands yet).
    /// </summary>
    internal void AnnounceToAll(string text, bool critical = false)
    {
        lock (_gate)
        {
            var watchers = Players().Where(p => p.Session != null).Select(p => p.ObjectId).ToList();
            if (watchers.Count == 0)
                return;
            NetBridge?.FanoutCreatureSay(watchers, new CreatureSayMsg
            {
                ObjectId = 0,
                Name = "",
                Text = text,
                ChatType = critical ? ChatProtocol.CriticalAnnounce : ChatProtocol.Announcement
            });
        }
    }

    /// <summary>Mobius PunishmentType.CHAT_BAN for <paramref name="durationMs"/> (0 lifts it). For GM tools and tests.</summary>
    internal void SetChatBan(ZoneSession session, long durationMs)
    {
        lock (_gate)
            session.ChatBannedUntilUnixMs = durationMs > 0 ? _clock().ToUnixTimeMilliseconds() + durationMs : 0;
    }

    static bool HearsLocal(ZoneEntity speaker, ZoneEntity other) =>
        speaker.Knownlist.Contains(other.ObjectId) &&
        SayRules.InLocalRange(speaker.X, speaker.Y, other.X, other.Y);
}
