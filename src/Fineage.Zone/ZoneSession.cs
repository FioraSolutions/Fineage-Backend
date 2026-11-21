
namespace Fineage.Zone;

public sealed class ZoneSession
{
    public ZoneSession()
    {
    }

    public int ObjectId { get; private set; }
    public string Account { get; private set; } = "";
    public string Name { get; private set; } = "";
    public int ClassId { get; private set; }
    /// <summary>From handoff claims; used when store lag would otherwise default race to Human.</summary>
    public int Race { get; private set; }
    public int Sex { get; private set; }
    public int WireSessionId { get; set; }
    public ushort LastPacketId { get; set; }
    internal bool FailureRecorded { get; set; }
    public bool InWorld { get; internal set; }
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public int Z { get; internal set; }
    public int SelectedTargetId { get; set; }

    /// <summary>
    /// The trainer NPC whose window this player last opened. It lives on the session, not the world:
    /// a single world-wide value let one player opening a trainer decide which NPC answered
    /// <b>everyone's</b> learn-skill request until someone else opened a window.
    /// </summary>
    public int TrainerNpcId { get; set; }

    /// <summary>Lottery numbers picked on the ticket seller's button page (Mobius <c>Player.getLoto</c>), 0 = empty.</summary>
    public int[] LotoPicks { get; } = new int[5];

    /// <summary>Mobius <c>Player.getRaceTicket(0)</c>: selected lane 1–8, 0 unset.</summary>
    public int RaceTicketLane { get; set; }

    /// <summary>Mobius <c>Player.getRaceTicket(1)</c>: ticket price tier 1–8, 0 unset.</summary>
    public int RaceTicketPriceIndex { get; set; }

    /// <summary>Unix milliseconds of the last accepted say. 0 means this session has not spoken.</summary>
    public long LastChatUnixMs { get; set; }

    /// <summary>Mobius FloodProtectors HERO_VOICE: last accepted hero-channel line (10 s window).</summary>
    public long LastHeroVoiceUnixMs { get; set; }

    /// <summary>Mobius chat ban (PunishmentType.CHAT_BAN): unix ms until which banned channels are refused; 0 = none.</summary>
    public long ChatBannedUntilUnixMs { get; set; }

    /// <summary>
    /// Player language for this character session. Copied from the account at enter, or from the
    /// EnterWorld tail when the client sends one. Defaults to <c>en</c>.
    /// </summary>
    public string Language { get; private set; } = Fineage.Game.L10n.PlayerLocale.Default;

    public bool Bound => ObjectId != 0;

    public void SetLanguage(string? language) =>
        Language = Fineage.Game.L10n.PlayerLocale.Normalize(language);

    public void Bind(int objectId, string account, string name, int classId, int race = 0, int sex = 0)
    {
        ObjectId = objectId;
        Account = account;
        Name = name;
        ClassId = classId;
        Race = race;
        Sex = sex;
        InWorld = false;
    }

    /// <summary>Mobius <c>setClassId</c> after a village-master transfer.</summary>
    internal void ChangeClass(int classId) => ClassId = classId;

    internal void NoteForLog(string account)
    {
        if (!string.IsNullOrEmpty(account))
            Account = account;
    }

    public void Clear()
    {
        ObjectId = 0;
        Account = "";
        Name = "";
        ClassId = 0;
        Race = 0;
        Sex = 0;
        InWorld = false;
        SelectedTargetId = 0;
        TrainerNpcId = 0;
        RaceTicketLane = 0;
        RaceTicketPriceIndex = 0;
        LastChatUnixMs = 0;
        LastHeroVoiceUnixMs = 0;
        Language = Fineage.Game.L10n.PlayerLocale.Default;
    }
}
