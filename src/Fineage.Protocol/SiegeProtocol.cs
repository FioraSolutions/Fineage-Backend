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
/// 50 and 1065–1066 are skills. 51 and 1067–1069 are character actions. Chat stays 1040–1049.
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

    /// <summary>
    /// Registration replies have no Mobius system message (it resends the castle page), so they keep the legacy
    /// custom ids 140–144. The five announcements shipped before castles (145–149) keep their ids for client
    /// compatibility (the Mobius equivalents are noted); everything newer uses Mobius <c>SystemMessageId</c> ids.
    /// </summary>
    public const int RegisteredAttacker = 140;
    public const int RegisteredDefender = 141;
    public const int Unregistered = 142;
    public const int DefenderApproved = 143;
    public const int DefenderRefused = 144;

    /// <summary>THE_REGISTRATION_TERM_FOR_S1_HAS_ENDED.</summary>
    public const int RegistrationEnded = 145; // Mobius 293
    /// <summary>THE_SIEGE_OF_S1_HAS_STARTED.</summary>
    public const int SiegeStarted = 146; // Mobius 711
    /// <summary>THE_SIEGE_OF_S1_HAS_FINISHED.</summary>
    public const int SiegeFinished = 147; // Mobius 712
    /// <summary>THE_SIEGE_OF_S1_HAS_BEEN_CANCELED_DUE_TO_LACK_OF_INTEREST (NPC-owned castle).</summary>
    public const int SiegeCancelled = 148; // Mobius 846
    /// <summary>S1_S_SIEGE_WAS_CANCELED_BECAUSE_THERE_WERE_NO_CLANS_THAT_PARTICIPATED (clan-owned castle).</summary>
    public const int SiegeCancelledOwned = 295;
    /// <summary>CLAN_S1_HAS_SUCCEEDED_IN_ENGRAVING_THE_RULER.</summary>
    public const int CastleCaptured = 149; // Mobius 285
    /// <summary>CLAN_S1_IS_VICTORIOUS_OVER_S2_S_CASTLE_SIEGE.</summary>
    public const int Victorious = 291;
    /// <summary>THE_SIEGE_OF_S1_HAS_ENDED_IN_A_DRAW.</summary>
    public const int Draw = 856;
    /// <summary>S1_HOUR_S_UNTIL_CASTLE_SIEGE_CONCLUSION.</summary>
    public const int HoursLeft = 358;
    /// <summary>S1_MINUTE_S_UNTIL_CASTLE_SIEGE_CONCLUSION.</summary>
    public const int MinutesLeft = 359;
    /// <summary>CASTLE_SIEGE_S1_SECOND_S_LEFT.</summary>
    public const int SecondsLeft = 360;
    /// <summary>YOUR_BASE_IS_BEING_ATTACKED.</summary>
    public const int BaseAttacked = 286;
    /// <summary>THE_OPPOSING_CLAN_HAS_STARTED_TO_ENGRAVE_THE_MONUMENT.</summary>
    public const int EngraveStarted = 287;
    /// <summary>THE_CASTLE_GATE_HAS_BEEN_BROKEN_DOWN.</summary>
    public const int GateBroken = 288;
    /// <summary>YOU_CANNOT_BUILD_ANOTHER_HEADQUARTERS_SINCE_ONE_ALREADY_EXISTS.</summary>
    public const int HeadquartersExists = 289;
    /// <summary>YOU_CANNOT_SET_UP_A_BASE_HERE.</summary>
    public const int BaseNotHere = 290;
    /// <summary>YOU_MAY_NOT_BUILD_YOUR_HEADQUARTERS_IN_CLOSE_PROXIMITY_TO_ANOTHER_HEADQUARTERS.</summary>
    public const int HeadquartersTooClose = 860;
    /// <summary>S1_CANNOT_BE_USED_DUE_TO_UNSUITABLE_TERMS.</summary>
    public const int UnsuitableTerms = 113;
    /// <summary>INVALID_TARGET.</summary>
    public const int InvalidTarget = 109;
    /// <summary>IF_A_BASE_CAMP_DOES_NOT_EXIST_RESURRECTION_IS_NOT_POSSIBLE.</summary>
    public const int NoBaseCamp = 716;
    /// <summary>THE_CASTLE_GATES_CANNOT_BE_OPENED_AND_CLOSED_DURING_A_SIEGE.</summary>
    public const int GatesDuringSiege = 718;
    /// <summary>SINCE_YOUR_CLAN_WAS_DEFEATED_IN_A_SIEGE_S1_POINTS_HAVE_BEEN_DEDUCTED…</summary>
    public const int ReputationLost = 1772;
    /// <summary>SINCE_YOUR_CLAN_EMERGED_VICTORIOUS_FROM_THE_SIEGE_S1_POINTS_HAVE_BEEN_ADDED…</summary>
    public const int ReputationGained = 1773;
    /// <summary>YOU_DO_NOT_HAVE_ENOUGH_ADENA.</summary>
    public const int NotEnoughAdena = 279;
    /// <summary>YOU_ARE_NOT_AUTHORIZED_TO_DO_THAT.</summary>
    public const int NotAuthorized = 794;
    /// <summary>THE_GATE_IS_BEING_DESTROYED (Mobius sends 1686 for walls).</summary>
    public const int WallDamaged = 1686;

    /// <summary>Mobius <c>Feature.ini</c> <c>TakeCastlePoints</c> / <c>LooseCastlePoints</c> / <c>CastleDefendedPoints</c>.</summary>
    public const int TakeCastlePoints = 1500;
    public const int LoseCastlePoints = 3000;
    public const int CastleDefendedPoints = 750;

    /// <summary>Mobius <c>Siege.ini</c> <c>MaxFlags</c>: one headquarters per attacking clan.</summary>
    public const int MaxFlags = 1;

    /// <summary>Seal of Ruler (Mobius <c>getSiegeSkills</c>, clan leaders only) and Build Headquarters.</summary>
    public const int SealOfRulerSkillId = 246;
    public const int BuildHeadquartersSkillId = 247;

    /// <summary>Build Headquarters <c>itemConsumeId</c> / <c>itemConsumeCount</c> (Gemstone C).</summary>
    public const int HeadquartersItemId = 2131;
    public const int HeadquartersItemCount = 300;

    /// <summary>Headquarters NPC (Mobius <c>SiegeFlag</c>, npc 35062).</summary>
    public const int HeadquartersNpcId = 35062;

    /// <summary>Mobius <c>Castle.canTakeCastle</c>: artifact within 200, caster no more than 45 below it… engrave range.</summary>
    public const int EngraveRange = 200;
    public const int EngraveMaxZDiff = 45;

    /// <summary>Lord's Crown (chamberlain <c>give_crown</c>).</summary>
    public const int LordsCrownItemId = 6841;

    /// <summary>Mobius <c>CastleManager._castleCirclets</c> indexed by castle id 1..9.</summary>
    public static readonly int[] CircletItemIds = [0, 6838, 6835, 6839, 6837, 6840, 6834, 6836, 8182, 8183];

    /// <summary>Chamberlain <c>getTaxLimit</c> without a Seal of Strife owner.</summary>
    public const int MaxTaxPercent = 15;

    /// <summary>Mobius <c>Castle.spawnDoor(true)</c>: mid-victory gates come back at half HP.</summary>
    public const double WeakDoorHpRatio = 0.5;

    /// <summary>Siege relation bits (Mobius <c>RelationChanged</c>).</summary>
    public const int RelationInSiege = 0x200;
    public const int RelationAttacker = 0x400;
    public const int RelationAlly = 0x800;
    public const int RelationEnemy = 0x1000;

    /// <summary>Player siege state (Mobius <c>Player.getSiegeState</c>): 1 attacker, 2 defender.</summary>
    public const int StateAttacker = 1;
    public const int StateDefender = 2;

    public const string NoTarget = "siege_target";
    public const string Range = "siege_range";
    public const string Items = "siege_items";
    public const string Flag = "siege_flag";
    public const string Zone = "siege_zone";
    public const string Tax = "siege_tax";
    public const string Adena = "siege_adena";
    public const string Rights = "siege_rights";
    public const string Door = "siege_door";
    public const string UpgradeLevel = "siege_upgrade_level";
    public const string HasItem = "siege_has_item";
    public const string Inventory = "siege_inventory";
}
