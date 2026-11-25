namespace Fineage.Protocol;

/// <summary>
/// Zone chat. Say2 type numbers match the Fineage client <c>ChatTypes</c> table
/// (the same numbering Interlude uses). This is not a port of a server class.
/// Wire layout: <c>docs/chat-v2-protocol.md</c>.
/// </summary>
/// <remarks>
/// Server → client chat replies use the band <see cref="ChatBandFirst"/>–<see cref="ChatBandLast"/>
/// so they stay clear of presence 1005–1007, inventory 1014–1016, combat 1008–1010 / 1017,
/// and party 1027–1029. <see cref="MsgType.Say"/> (8) and <see cref="MsgType.CreatureSay"/> (1012)
/// were already assigned. <see cref="MsgType.SystemMessage"/> is 1040. 1041–1049 are reserved.
/// </remarks>
public static class ChatProtocol
{
    public const ushort ChatBandFirst = 1040;
    public const ushort ChatBandLast = 1049;

    public const int All = 0;
    public const int Shout = 1;
    public const int Tell = 2;
    public const int Party = 3;
    public const int Clan = 4;
    public const int Gm = 5;
    public const int PetitionPlayer = 6;
    public const int PetitionGm = 7;
    public const int Trade = 8;
    public const int Alliance = 9;
    public const int Announcement = 10;
    public const int Boat = 11;
    public const int Friend = 12;
    public const int Msn = 13;
    public const int PartyMatchRoom = 14;
    public const int PartyRoomCommander = 15;
    public const int PartyRoomAll = 16;
    public const int Hero = 17;
    public const int CriticalAnnounce = 18;

    /// <summary>Normal say radius in world units (2D). Shout uses the speaker's map region (Mobius getMapRegionLocId).</summary>
    public const int LocalRange = 1250;

    /// <summary>
    /// Maximum chat text length after the client has sent it, before trim. Longer bodies are rejected
    /// (Mobius Say2: 105, verified on the official client).
    /// </summary>
    public const int MaxTextLength = 105;

    /// <summary>Mobius GeneralConfig.MINIMUM_CHAT_LEVEL default: shout channel opens at this level.</summary>
    public const int ShoutMinLevel = 20;

    /// <summary>Mobius ChatTrade: trade channel opens at this level.</summary>
    public const int TradeMinLevel = 20;

    /// <summary>Mobius FloodProtectorHeroVoiceInterval = 100 ticks: one hero line per 10 s.</summary>
    public const int HeroVoiceIntervalMs = 10_000;

    /// <summary>Interlude <c>THAT_PLAYER_IS_NOT_ONLINE</c>.</summary>
    public const int PlayerNotOnline = 145;

    /// <summary>Interlude <c>CHATTING_IS_CURRENTLY_PROHIBITED</c> (chat ban, Say2).</summary>
    public const int ChattingProhibited = 147;

    /// <summary>Interlude <c>THAT_PERSON_IS_IN_MESSAGE_REFUSAL_MODE</c> (whisper to a blocker / refusal / banned).</summary>
    public const int MessageRefusal = 176;

    /// <summary>Interlude <c>YOU_ARE_CURRENTLY_NOT_IN_A_PETITION_CHAT</c>.</summary>
    public const int NotInPetitionChat = 745;

    /// <summary>Minimum gap between accepted say attempts on one session.</summary>
    public const int FloodIntervalMs = 1000;

    /// <summary>Display <see cref="SystemMessageMsg.Text"/> as written. No catalog lookup.</summary>
    public const int SystemPlain = 0;

    /// <summary>Enter world. Params[0] is the character name. Text is <c>Welcome, {name}.</c></summary>
    public const int Welcome = 1;

    /// <summary>Chat type is not delivered. Text is <c>not supported yet</c>. Params[0] is the type number.</summary>
    public const int ChatNotSupported = 2;

    /// <summary>Another accepted say arrived inside <see cref="FloodIntervalMs"/>.</summary>
    public const int ChatFlood = 3;
}
