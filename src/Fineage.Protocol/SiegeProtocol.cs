namespace Fineage.Protocol;

/// <summary>
/// Castle siege registration and ownership. Layout: <c>docs/siege-v2-protocol.md</c>.
/// </summary>
/// <remarks>
/// Rules and defaults follow L2J Mobius Interlude (<c>org.l2jmobius</c>), not its class layout:
/// <c>Castle</c>, <c>Siege</c>, <c>SiegeManager</c>, <c>dist/game/config/Siege.ini</c>,
/// <c>dist/game/config/SiegeSchedule.xml</c>, and <c>dist/db_installer/sql/game/castle.sql</c>.
/// Shipped <c>Siege.ini</c> is <c>SiegeCycle = 2</c>, <c>SiegeLength = 120</c>,
/// <c>SiegeClanMinLevel = 4</c>, <c>AttackerMaxClans = 500</c>, <c>DefenderMaxClans = 500</c>.
/// The Java fallback when the min-level key is missing is 5; the ini and the
/// "level 4 or higher" system message are what a stock server runs.
/// Clan registration closes when at most 24 hours remain (<c>86_400_000</c> ms).
/// The owner clan is a defender and is not stored in <c>siege_clans</c>.
/// Defender signup is <c>DEFENDER_NOT_APPROVED</c> until the owner approves.
/// Requests are 46–49, after quests 42–45. The reply is 1064, after quest 1063.
/// 50 and 1065–1069 stay free. Chat stays 1040–1049.
/// </remarks>
public static class SiegeProtocol
{
    public const ushort Register = 46;
    public const ushort DefenderAnswer = 47;
    public const ushort List = 48;
    public const ushort AdminCapture = 49;
    public const ushort Info = 1064;

    /// <summary>Mobius <c>Siege.OWNER</c>. Not written to the registration table.</summary>
    public const int Owner = -1;

    /// <summary>Mobius <c>Siege.DEFENDER</c>.</summary>
    public const int Defender = 0;

    /// <summary>Mobius <c>Siege.ATTACKER</c>.</summary>
    public const int Attacker = 1;

    /// <summary>Mobius <c>Siege.DEFENDER_NOT_APPROVED</c>.</summary>
    public const int DefenderPending = 2;

    public const int CycleWeeks = 2;
    public const int LengthMinutes = 120;
    public const int MinClanLevel = 4;
    public const int MaxAttackers = 500;
    public const int MaxDefenders = 500;

    /// <summary>Clan registration closes this long before the siege. Mobius uses 24 hours.</summary>
    public const long RegistrationCloseMillis = 86_400_000L;

    /// <summary>SQL insert tax for every Interlude castle. The column default is 15; the rows are 0.</summary>
    public const int DefaultTaxPercent = 0;

    public const string Clan = "siege_clan";
    public const string Leader = "siege_leader";
    public const string Dissolving = "siege_dissolving";
    public const string Npc = "siege_npc";
    public const string Closed = "siege_closed";
    public const string Progress = "siege_progress";
    public const string Level = "siege_level";
    public const string AutoDefender = "siege_auto_defender";
    public const string Owns = "siege_owns";
    public const string Duplicate = "siege_duplicate";
    public const string SameDay = "siege_sameday";
    public const string Attackers = "siege_attackers";
    public const string Defenders = "siege_defenders";
    public const string None = "siege_none";
    public const string NotOwner = "siege_owner";
    public const string Pending = "siege_pending";
    public const string Castle = "siege_castle";
    public const string Idle = "siege_idle";
    public const string NotAttacker = "siege_not_attacker";
    public const string Store = "siege_store";

    /// <summary>System message ids on <see cref="MsgType.SystemMessage"/>. Chat is 1–3, enchant 10–20, quests 30–38, clan 100–114.</summary>
    public const int RegisteredAttacker = 140;
    public const int RegisteredDefender = 141;
    public const int Unregistered = 142;
    public const int DefenderApproved = 143;
    public const int DefenderRefused = 144;
    public const int RegistrationEnded = 145;
    public const int SiegeStarted = 146;
    public const int SiegeFinished = 147;
    public const int SiegeCancelled = 148;
    public const int CastleCaptured = 149;
}
