using Fineage.Protocol;
using Fineage.Zone.Chat;

namespace Fineage.Zone.World;

/// <summary>
/// Say2 channels (Mobius CT_0 chat handlers, reimplemented): who hears each line, and the refusals the speaker gets.
/// Audiences: All = visible players within 1250; Shout / Trade = same map region (MapRegion locId) and instance;
/// Tell = one online player by name; Party / Clan / Alliance = online members; Hero = everyone; Petition / party
/// rooms are refused or ignored until those systems exist. Every receiver drops lines from players it blocks
/// (block list or message refusal), as Mobius <c>BlockList.isBlocked(receiver, speaker)</c>.
/// </summary>
public sealed partial class ZoneWorld
{
    readonly ChatActor _chat = new();

    /// <summary>Mobius GeneralConfig.BAN_CHAT_CHANNELS default: GENERAL;SHOUT;TRADE;HERO_VOICE;WHISPER.</summary>
    static bool BanCovers(int chatType) =>
        chatType is ChatProtocol.All or ChatProtocol.Shout or ChatProtocol.Trade or ChatProtocol.Hero or ChatProtocol.Tell;

    static bool TryParseVoicedWedding(string text, out string verb)
    {
        verb = "";
        if (!text.StartsWith(".", StringComparison.Ordinal))
            return false;
        string body = text[1..].Trim();
        int space = body.IndexOf(' ');
        verb = space < 0 ? body : body[..space];
        return verb.Equals(WeddingProtocol.Engage, StringComparison.OrdinalIgnoreCase)
            || verb.Equals(WeddingProtocol.Divorce, StringComparison.OrdinalIgnoreCase)
            || verb.Equals(WeddingProtocol.GoToLove, StringComparison.OrdinalIgnoreCase);
    }

    public Task<string?> TrySayAsync(
        ZoneSession session, string? text, int chatType, DateTimeOffset now, CancellationToken ct, string? target = null)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        if (!SayRules.TryNormalize(text, out string normalized))
            return Task.FromResult<string?>("bad_request");

        SystemMessageMsg? notice = null;
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var speaker) || speaker.Session == null)
            return Task.FromResult<string?>("world");

        if (GmChatCommand.TrySplit(normalized, out _, out _))
        {
            session.LastChatUnixMs = now.ToUnixTimeMilliseconds();
            notice = TryGmChat(speaker, session, normalized);
        }
        else if (TryParseVoicedWedding(normalized, out string weddingVerb))
        {
            session.LastChatUnixMs = now.ToUnixTimeMilliseconds();
            TryWeddingVoiced(session, weddingVerb);
        }
        else
        {
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
                notice = new SystemMessageMsg
                {
                    Id = ChatProtocol.ChattingProhibited,
                    Key = "chat.prohibited",
                    Params = [],
                    Text = "Chatting is currently prohibited."
                };
            }
            else
            {
                session.LastChatUnixMs = nowMs;
                notice = Deliver(speaker, chatType, normalized, target?.Trim() ?? "", nowMs);
            }
        }

        if (notice != null)
        {
            notice = Localize(session, notice);
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

        if (!GmChatCommand.TrySplit(normalized, out _, out _))
            ZoneLog.Info($"say object={session.ObjectId} name={session.Name} type={chatType} text={normalized}");
        return Task.FromResult<string?>(null);
    }

    static SystemMessageMsg Plain(string text) => new() { Id = ChatProtocol.SystemPlain, Params = [], Text = text };

    static SystemMessageMsg Plain(string key, string text, params string[] args) => new()
    {
        Id = ChatProtocol.SystemPlain,
        Key = key,
        Params = args,
        Text = text
    };

    /// <summary>Routes one accepted line; returns the refusal for the speaker, or null when it went out.</summary>
    SystemMessageMsg? Deliver(ZoneEntity speaker, int chatType, string text, string target, long nowMs)
    {
        switch (chatType)
        {
            case ChatProtocol.All:
                SayLocal(speaker, text);
                return null;
            case ChatProtocol.Shout:
                if (speaker.Level < ChatProtocol.ShoutMinLevel)
                    return Plain("chat.shout_level", $"Players can Shout after Lv. {ChatProtocol.ShoutMinLevel}.", ChatProtocol.ShoutMinLevel.ToString());
                SayRegion(speaker, chatType, text, speakerMustNotBlock: true);
                return null;
            case ChatProtocol.Trade:
                if (speaker.Level < ChatProtocol.TradeMinLevel)
                    return Plain("chat.trade_level", $"Players can use Trade chat after Lv. {ChatProtocol.TradeMinLevel}.", ChatProtocol.TradeMinLevel.ToString());
                SayRegion(speaker, chatType, text, speakerMustNotBlock: false);
                return null;
            case ChatProtocol.Tell:
                return Whisper(speaker, target, text, nowMs);
            case ChatProtocol.Friend:
                return FriendChat(speaker, target, text, nowMs);
            case ChatProtocol.Party:
                if (_party.Of(speaker.ObjectId) is not { } party)
                    return Plain("chat.not_party", "You are not in a party.");
                SayToGroup(speaker, chatType, text, party.MemberIds);
                return null;
            case ChatProtocol.Clan:
                if (speaker.ClanId == 0 || ClanOf(speaker.ObjectId) is not { } clan)
                    return Plain("chat.not_clan", "You are not in a clan.");
                SayToGroup(speaker, chatType, text, OnlineMembers(clan).Select(member => member.ObjectId));
                return null;
            case ChatProtocol.Alliance:
                {
                    // Mobius ChatAlliance: every online member of every clan in the speaker's alliance.
                    int allyId = speaker.ClanId == 0 ? 0 : ClanOf(speaker.ObjectId)?.D.AllyId ?? 0;
                    if (allyId == 0)
                        return Plain("chat.not_alliance", "You are not in an alliance.");
                    SayToGroup(speaker, chatType, text, AllyOnlineIds(allyId));
                    return null;
                }
            case ChatProtocol.Hero:
                if (!speaker.Hero)
                    return Plain("chat.hero_only", "Only Heroes can enter the Hero channel.");
                if (speaker.Session!.LastHeroVoiceUnixMs > 0 && nowMs - speaker.Session.LastHeroVoiceUnixMs < ChatProtocol.HeroVoiceIntervalMs)
                    return Plain("chat.hero_wait", "Action failed. Heroes are only able to speak in the global channel once every 10 seconds.");
                speaker.Session.LastHeroVoiceUnixMs = nowMs;
                SayToGroup(speaker, chatType, text, _chat.Online);
                return null;
            case ChatProtocol.PetitionPlayer:
            case ChatProtocol.PetitionGm:
                return new SystemMessageMsg
                {
                    Id = ChatProtocol.NotInPetitionChat,
                    Key = "chat.not_petition",
                    Params = [],
                    Text = "You are currently not in a petition chat."
                };
            case ChatProtocol.PartyRoomCommander:
                if (_party.Of(speaker.ObjectId) is not { } ccParty)
                    return null;
                if (_commandChannel.OfParty(ccParty.PartyId) is not { } ccCmd)
                    return null;
                if (ccCmd.LeaderObjectId != speaker.ObjectId)
                    return null;
                SayToGroup(speaker, chatType, text, CommandChannelOnlineMembers(ccCmd));
                return null;
            case ChatProtocol.PartyRoomAll:
                if (_party.Of(speaker.ObjectId) is not { } roomParty)
                    return null;
                if (_commandChannel.OfParty(roomParty.PartyId) is not { } ccAll)
                    return null;
                if (roomParty.LeaderObjectId != speaker.ObjectId)
                    return null;
                SayToGroup(speaker, chatType, text, CommandChannelOnlineMembers(ccAll));
                return null;
            case ChatProtocol.PartyMatchRoom:
            {
                var roomMembers = PartyMatchRoomOnlineMembers(speaker.ObjectId);
                if (roomMembers == null)
                {
                    ZoneLog.Warn(
                        $"[chat] PartyMatchRoom say rejected: characterId={speaker.ObjectId} has no party-match room");
                    return null;
                }

                SayToGroup(speaker, chatType, text, roomMembers);
                return null;
            }
            default:
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

    /// <summary>The player in the world with that name (case-insensitive), from the chat actor's roster.</summary>
    ZoneEntity? OnlinePlayerNamed(string name) =>
        _chat.Find(name) is var id and not 0 && _byId.TryGetValue(id, out var player) ? player : null;

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
    /// <summary>Mobius ChatFriend: online friend by name; blocked peers rejected.</summary>
    SystemMessageMsg? FriendChat(ZoneEntity speaker, string target, string text, long nowMs)
    {
        if (target.Length == 0)
            return Plain("chat.friend_target", "Select a friend to chat with.");
        var receiver = OnlinePlayerNamed(target);
        if (receiver?.Session == null || !receiver.Session.InWorld)
            return new SystemMessageMsg
            {
                Id = ChatProtocol.PlayerNotOnline,
                Key = "chat.not_online",
                Params = [],
                Text = "That player is not online."
            };
        if (!Lists(speaker).Friends.ContainsKey(receiver.ObjectId))
            return Plain("chat.not_friend", "That player is not on your friend list.");
        if (receiver.Session.ChatBannedUntilUnixMs > nowMs || IsBlocked(receiver, speaker) || IsBlocked(speaker, receiver))
            return new SystemMessageMsg
            {
                Id = ChatProtocol.MessageRefusal,
                Key = "chat.refusal",
                Params = [],
                Text = "That person is in message refusal mode."
            };

        NetBridge?.FanoutCreatureSay([receiver.ObjectId], Line(speaker, ChatProtocol.Friend, text));
        NetBridge?.FanoutCreatureSay([speaker.ObjectId], Line(speaker, ChatProtocol.Friend, text, "->" + receiver.Name));
        return null;
    }

    SystemMessageMsg? Whisper(ZoneEntity speaker, string target, string text, long nowMs)
    {
        if (target.Length == 0)
            return null;
        var receiver = OnlinePlayerNamed(target);

        if (receiver?.Session == null || !receiver.Session.InWorld)
            return new SystemMessageMsg
            {
                Id = ChatProtocol.PlayerNotOnline,
                Key = "chat.not_online",
                Params = [],
                Text = "That player is not online."
            };
        var refusal = new SystemMessageMsg
        {
            Id = ChatProtocol.MessageRefusal,
            Key = "chat.refusal",
            Params = [],
            Text = "That person is in message refusal mode."
        };
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
        using (_affinity.Step())
        {
            var watchers = _chat.Online;
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

    static bool HearsLocal(ZoneEntity speaker, ZoneEntity other) =>
        speaker.Knownlist.Contains(other.ObjectId) &&
        SayRules.InLocalRange(speaker.X, speaker.Y, other.X, other.Y);
}
