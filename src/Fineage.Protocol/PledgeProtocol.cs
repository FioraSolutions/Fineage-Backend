namespace Fineage.Protocol;

/// <summary>
/// Interlude clan constants beyond the roster, matched against L2J Mobius CT_0 (<c>Clan</c>, <c>ClanPrivilege</c>,
/// <c>ClanMember</c>, <c>VillageMaster</c>, <c>ClanTable</c>, <c>RelationChanged</c>). Values reimplemented, not copied.
/// </summary>
public static class PledgeProtocol
{
    // Mobius Clan.SUBUNIT_*.
    public const int Academy = -1;
    public const int Main = 0;
    public const int Royal1 = 100;
    public const int Royal2 = 200;
    public const int Knight1 = 1001;
    public const int Knight2 = 1002;
    public const int Knight3 = 2001;
    public const int Knight4 = 2002;

    public static bool IsRoyal(int type) => type is Royal1 or Royal2;
    public static bool IsKnight(int type) => type is Knight1 or Knight2 or Knight3 or Knight4;
    public static bool IsSubUnit(int type) => type == Academy || IsRoyal(type) || IsKnight(type);
    public static bool IsKnownType(int type) => type == Main || IsSubUnit(type);

    // Mobius ClanPrivilege: mask = 1 << ordinal (DUMMY is ordinal 0).
    public const int PrivInviteMember = 1 << 1;
    public const int PrivAssignTitle = 1 << 2;
    public const int PrivAccessWarehouse = 1 << 3;
    public const int PrivModifyRanks = 1 << 4;
    public const int PrivWarDeclaration = 1 << 5;
    public const int PrivRemoveMember = 1 << 6;
    public const int PrivChangeCrest = 1 << 7;
    public const int PrivDismissMentee = 1 << 8;
    public const int PrivMemberFame = 1 << 9;
    public const int PrivAccessAirship = 1 << 10;
    public const int PrivHallOpenDoor = 1 << 11;
    public const int PrivHallFunctions = 1 << 12;
    public const int PrivHallAuction = 1 << 13;
    public const int PrivHallBanish = 1 << 14;
    public const int PrivHallManageFunctions = 1 << 15;
    public const int PrivCastleOpenDoor = 1 << 16;
    public const int PrivCastleManor = 1 << 17;
    public const int PrivCastleSiege = 1 << 18;
    public const int PrivCastleFunctions = 1 << 19;
    public const int PrivCastleBanish = 1 << 20;
    public const int PrivCastleVault = 1 << 21;
    public const int PrivCastleMercenaries = 1 << 22;
    public const int PrivCastleManageFunctions = 1 << 23;
    /// <summary>Every privilege bit (the clan leader's mask).</summary>
    public const int PrivAll = (1 << 24) - 2;
    /// <summary>Mobius RequestPledgePower: grade 9 (academy) may only hold warehouse + doors.</summary>
    public const int PrivAcademyMask = PrivAccessWarehouse | PrivHallOpenDoor | PrivCastleOpenDoor;

    public const int MinPowerGrade = 1;
    public const int MaxPowerGrade = 9;
    public const int NewMemberGrade = 5;
    public const int AcademyGrade = 9;
    public const int LeaderGrade = 1;

    public const int MaxClanLevel = 8;
    public const int BloodMarkItemId = 1419;
    public const int AllianceManifestoItemId = 3874;
    public const int SealOfAspirationItemId = 3870;
    public const int AdenaItemId = 57;
    /// <summary>Mobius VillageMaster increase_clan_level: MagicSkillUse 5103 on success.</summary>
    public const int LevelUpSkillId = 5103;

    /// <summary>Mobius <c>Clan.levelUpClan</c> requirements to go from <paramref name="current"/> to current + 1.</summary>
    public static LevelUpCost LevelUp(int current) => current switch
    {
        0 => new LevelUpCost(20_000, 650_000, 0, 0, 0),
        1 => new LevelUpCost(100_000, 2_500_000, 0, 0, 0),
        2 => new LevelUpCost(350_000, 0, BloodMarkItemId, 0, 0),
        3 => new LevelUpCost(1_000_000, 0, AllianceManifestoItemId, 0, 0),
        4 => new LevelUpCost(2_500_000, 0, SealOfAspirationItemId, 0, 0),
        5 => new LevelUpCost(0, 0, 0, 5_000, 30),
        6 => new LevelUpCost(0, 0, 0, 10_000, 50),
        7 => new LevelUpCost(0, 0, 0, 20_000, 80),
        _ => default
    };

    public const int AcademyMinClanLevel = 5;
    public const int RoyalMinClanLevel = 6;
    public const int KnightMinClanLevel = 7;
    /// <summary>Mobius Clan.createSubPledge reputation price (royal guard / order of knights).</summary>
    public const int RoyalGuardCost = 5_000;
    public const int KnightUnitCost = 10_000;

    /// <summary>Mobius RequestJoinPledge academy rule: level 40 or lower and no second class transfer.</summary>
    public const int AcademyMaxLevel = 40;
    public const int AcademyMinRep = 190;
    public const int AcademyMaxRep = 650;
    /// <summary>Mobius graduation reward: Academy Circlet (8181).</summary>
    public const int AcademyCircletItemId = 8181;

    /// <summary>Mobius Player graduation: 650 at join level 16 or lower, 190 at 39+, else 650 − (lvl − 16) · 20.</summary>
    public static int AcademyGraduationRep(int levelJoined) =>
        levelJoined <= 16 ? AcademyMaxRep : levelJoined >= 39 ? AcademyMinRep : AcademyMaxRep - (levelJoined - 16) * 20;

    /// <summary>Mobius clan war kill: ±1 reputation (CLAN_WAR_POINTS... default 1 in CT_0).</summary>
    public const int ReputationPerKill = 1;
    public const int ReputationCap = 100_000_000;
    /// <summary>Mobius: reputation only changes for clans at level 5 or higher.</summary>
    public const int ReputationMinClanLevel = 5;

    public const int WarMinClanLevel = 3;
    public const int WarMinMembers = 15;
    /// <summary>Mobius CT_0: after either side stops a war, neither may declare on the other for five days.</summary>
    public const int WarCooldownDays = 5;
    public const long WarCooldownMillis = WarCooldownDays * 24L * 3600 * 1000;
    /// <summary>Mobius clan war death: members of the declaring clan lose one quarter of normal death XP when killed by the enemy clan.</summary>
    public const double WarDeathExpShare = 0.25d;

    public const int CrestKindPledge = 0;
    public const int CrestKindLarge = 1;
    public const int CrestKindAlly = 2;
    public const int MaxPledgeCrestBytes = 256;
    public const int MaxLargeCrestBytes = 2176;
    public const int MaxAllyCrestBytes = 192;
    public const int CrestMinClanLevel = 3;

    public const int TitleMinClanLevel = 3;
    public const int MaxTitleLength = 16;

    public const int ClanWarehouseSlots = 200;
    /// <summary>Mobius WarehouseDepositList fee per stack (30 adena).</summary>
    public const int WarehouseFeePerItem = 30;
    public const int WarehouseTypePrivate = 1;
    public const int WarehouseTypeClan = 2;
    /// <summary>Mobius <c>WareHouseWithdrawalList.FREIGHT</c> (package pickup).</summary>
    public const int WarehouseTypeFreight = 4;

    // Mobius RelationChanged bits.
    public const int RelationPvpFlag = 0x00002;
    public const int RelationHasKarma = 0x00004;
    public const int RelationLeader = 0x00080;
    public const int RelationInSiege = 0x00200;
    public const int RelationAttacker = 0x00400;
    public const int RelationAlly = 0x00800;
    public const int RelationEnemy = 0x01000;
    public const int RelationMutualWar = 0x08000;
    public const int RelationOneSidedWar = 0x10000;

    public const int AllyMinClanLevel = 5;
    public const int AllyMaxClans = 3;
    public const int AllyJoinDaysWhenLeaved = 1;
    public const int AllyJoinDaysWhenDismissed = 1;
    public const int AllyAcceptDaysWhenDismissed = 1;
    public const int AllyCreateDaysWhenDissolved = 10;
    public const int AllyPenaltyClanLeaved = 1;
    public const int AllyPenaltyClanDismissed = 2;
    public const int AllyPenaltyDismissClan = 3;
    public const int AllyPenaltyDissolveAlly = 4;

    // Error codes returned to the client reject path.
    public const string Denied = "clan_denied";
    public const string Conditions = "clan_conditions";
    public const string Reputation = "clan_reputation";
    public const string NoSuchClan = "clan_no_such";
    public const string NotAtWar = "clan_not_at_war";
    public const string AlreadyAtWar = "clan_already_at_war";
    public const string WarAlly = "clan_war_ally";
    public const string WarCombat = "clan_war_combat";
    public const string CrestSize = "clan_crest_size";
    public const string BadArgument = "clan_bad_argument";
    public const string Offline = "clan_offline";
    public const string AcademyMember = "clan_academy";
    public const string Dissolving = "clan_dissolving";
}

/// <summary>One clan level-up step (Mobius <c>Clan.levelUpClan</c>).</summary>
public readonly record struct LevelUpCost(int Sp, long Adena, int ItemId, int Reputation, int MinMembers)
{
    public bool Exists => Sp > 0 || Adena > 0 || ItemId > 0 || Reputation > 0;
}
