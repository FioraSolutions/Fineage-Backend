using Fineage.Zone.Geo;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.Drops;
using Fineage.Zone.Bosses;
using Fineage.Game.Shops;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Shops;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Dungeons;
using Fineage.Zone.Siege;
using Fineage.Zone.Olympiad;
using Fineage.Game.Services;

namespace Fineage.Zone.World;

readonly record struct UnlinkPersistSnapshot(ZoneSession Session, ZoneEntity Entity);

public enum ZoneEntityKind
{
    Npc = 0,
    GroundItem = 1,
    Player = 2
}

/// <summary>Ground-item fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZoneGroundItemPayload
{
    public int ItemId { get; set; }
    public int Count { get; set; } = 1;
    public bool Stackable { get; set; }
    public int OwnerId { get; set; }
    public long ProtectUntilMs { get; set; }
    public long ExpireAtMs { get; set; }
}

/// <summary>NPC spoil/sweep corpse fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZoneNpcSpoilPayload
{
    public int SpoilerObjectId { get; set; }
    public List<Drops.DropStack> SweepItems { get; } = [];
    public bool CorpseAwaitingSweep { get; set; }
}

/// <summary>NPC merchant tax/town fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZoneNpcMerchantPayload
{
    public int MerchantCastleId { get; set; }
    public string? MerchantTown { get; set; }
    public int MerchantBaseTaxPercent { get; set; }
}

/// <summary>NPC template / identity combat fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZoneNpcTemplatePayload
{
    public string NpcType { get; set; } = "";
    public int NpcId { get; set; }
    public bool Attackable { get; set; }
    public long RewardExp { get; set; }
    public int RewardSp { get; set; }
    public double PAtk { get; set; }
    public double HpRegen { get; set; }
    public double MpRegen { get; set; }
    public double PDef { get; set; }
    public int PAtkSpd { get; set; }
    public int AttackRange { get; set; }
    public bool Aggressive { get; set; }
    public int AggroRange { get; set; }
    public int ClanHelpRange { get; set; }
    public IReadOnlyList<string>? Clans { get; set; }
    public IReadOnlyList<int>? IgnoreNpcIds { get; set; }
    public double BaseAccuracy { get; set; }
    public double BaseEvasion { get; set; }
    public int BaseCritical { get; set; }
    public int RandomDamage { get; set; }
    public int ShieldDef { get; set; }
    public int ShieldRate { get; set; }
    public double MAtk { get; set; }
    public double MDef { get; set; }
    public int NpcSoulShotLeft { get; set; }
    public int NpcSoulShotChance { get; set; }
    public int NpcSpiritShotLeft { get; set; }
    public int NpcSpiritShotChance { get; set; }
    public IReadOnlyDictionary<string, double>? DefenceTraits { get; set; }
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSkill>? AiSkills { get; set; }
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiHealSkills { get; set; }
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiBuffSkills { get; set; }
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiDebuffSkills { get; set; }
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiImmobilizeSkills { get; set; }
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiSilenceSkills { get; set; }
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiSuicideSkills { get; set; }
    public int MinSkillChance { get; set; } = 7;
    public int MaxSkillChance { get; set; } = 15;
}

/// <summary>Player race/clan/PvP identity fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZonePlayerIdentityPayload
{
    public int Race { get; set; }
    public int Sex { get; set; }
    public bool Hero { get; set; }
    public bool InClan { get; set; }
    public bool ClanLeader { get; set; }
    public int PledgeClass { get; set; }
    public int CastleId { get; set; }
    public int HideoutId { get; set; }
    public bool PvpFlagged { get; set; }
    public long PvpFlagUntilMs { get; set; }
    public int PkKills { get; set; }
    public int PvpKills { get; set; }
    public int Karma { get; set; }
    public int ClanId { get; set; }
    public bool Newbie { get; set; } = true;
}

/// <summary>Player friend/block social fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZonePlayerSocialPayload
{
    public Dictionary<int, string> Friends { get; } = [];
    public Dictionary<int, string> Blocked { get; } = [];
    public bool BlockAll { get; set; }
}

/// <summary>Shared HP/MP/XP/sitting/death fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZoneEntityVitalsPayload
{
    public int CurHp { get; set; }
    public int MaxHp { get; set; }
    public int CurMp { get; set; }
    public int MaxMp { get; set; }
    public int Level { get; set; } = 1;
    public long Exp { get; set; }
    public int Sp { get; set; }
    public bool Sitting { get; set; }
    public bool Dead { get; set; }
}

/// <summary>Shared combat timing / DEX / enchant/decay fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZoneEntityCombatPayload
{
    public int Dex { get; set; } = 30;
    public long CombatUntilUnixMs { get; set; }
    public long CastingUntilMs { get; set; }
    public PendingSkillHit? PendingHit { get; set; }
    public int ActiveEnchantObjectId { get; set; }
    public long DecayAtMs { get; set; }
}

/// <summary>Shared move / swim / path fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZoneEntityMovePayload
{
    public float BaseRunSpeed { get; set; }
    public float? LiveRunSpeed { get; set; }
    public float BaseWalkSpeed { get; set; }
    public float? LiveWalkSpeed { get; set; }
    public float BaseSwimRunSpeed { get; set; }
    public float BaseSwimWalkSpeed { get; set; }
    public float? LiveSwimRunSpeed { get; set; }
    public float? LiveSwimWalkSpeed { get; set; }
    public bool Running { get; set; } = true;
    public bool InWater { get; set; }
    public bool MoveActive { get; set; }
    public int MoveDestX { get; set; }
    public int MoveDestY { get; set; }
    public int MoveDestZ { get; set; }
    public long MoveLastTickMs { get; set; }
    public Queue<(int X, int Y, int Z)> MovePath { get; } = new();
    public bool MoveSegmentDirty { get; set; }
    public int AoiRefreshX { get; set; }
    public int AoiRefreshY { get; set; }
}

/// <summary>Player skill / abnormal / shortcut fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZonePlayerSkillsPayload
{
    public Dictionary<int, Fineage.Accounts.StoredShortcut> Shortcuts { get; } = [];
    public Dictionary<int, int> LearnedSkills { get; } = [];
    public bool HasStatAbnormal { get; set; }
    public List<ZoneAbnormalEntry> Abnormals { get; } = [];
    public Dictionary<int, int> KnownSkills { get; } = [];
    /// <summary>
    /// Mobius <c>Clan.addSkillEffects</c> rows (Clan Might 376, Aegis 377, …). Not persisted on the character —
    /// refreshed from the clan skill book on join / enter / leave via <c>RefreshClanSkills</c>.
    /// Folded into combat mods via <c>PassiveSkillLevels</c>.
    /// </summary>
    public Dictionary<int, int> ClanSkills { get; } = [];
    /// <summary>
    /// Ephemeral equip + complete armor-set skills (Mobius <c>Inventory.addSkill(..., false)</c>).
    /// Rebuilt by <c>RefreshGearSkills</c>; never persisted.
    /// </summary>
    public Dictionary<int, int> GearSkills { get; } = [];
    public Dictionary<int, long> SkillReuseUntilMs { get; } = [];
    public Dictionary<int, long> ItemReuseUntilMs { get; } = [];
}

/// <summary>Player penalty / shot / regen-carry fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZonePlayerStatusPayload
{
    public int ExpertiseBonus { get; set; }
    public int WeaponGradePenalty { get; set; }
    public int ArmorGradePenalty { get; set; }
    public int GradePenaltyLevel { get; set; }
    public int DeathPenaltyLevel { get; set; }
    public int WeightPenaltyLevel { get; set; }
    public double RegenCarryHp { get; set; }
    public double RegenCarryMp { get; set; }
    public bool SoulshotCharged { get; set; }
    public int SpiritshotCharge { get; set; }
    public HashSet<int> AutoShots { get; } = [];
    /// <summary>Mobius henna dye ids in the three tattoo slots (0 = empty).</summary>
    public int HennaDye1 { get; set; }
    public int HennaDye2 { get; set; }
    public int HennaDye3 { get; set; }
    /// <summary>Cached sum of worn henna STR/CON/… (recomputed when dyes change).</summary>
    public int HennaStr { get; set; }
    public int HennaCon { get; set; }
    public int HennaDex { get; set; }
    public int HennaInt { get; set; }
    public int HennaMen { get; set; }
    public int HennaWit { get; set; }
}

/// <summary>Player private-store / manufacture fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZonePlayerStorePayload
{
    public byte PrivateStoreType { get; set; }
    public string PrivateStoreMessage { get; set; } = "";
    public List<PrivateStoreSellListing> PrivateSellList { get; } = [];
    public List<PrivateStoreBuyListing> PrivateBuyList { get; } = [];
    public List<ManufactureListing> ManufactureList { get; } = [];
    public HashSet<int> KnownRecipes { get; } = [];
}

/// <summary>NPC aggro / chase / return-home runtime fields composed off <see cref="ZoneEntity"/> (#353).</summary>
sealed class ZoneNpcAggroPayload
{
    public Dictionary<int, long> HateList { get; } = [];
    public Dictionary<int, long> DamageBy { get; } = [];
    public long NextAggroScanMs { get; set; }
    public long NextClanCallMs { get; set; }
    public bool StanceShown { get; set; }
    public int AggroTargetId { get; set; }
    public long NextMobAttackMs { get; set; }
    public int ChaseDestX { get; set; }
    public int ChaseDestY { get; set; }
    public bool Chasing { get; set; }
    public long LastAiTickMs { get; set; }
    public bool HomeSet { get; set; }
    public int HomeX { get; set; }
    public int HomeY { get; set; }
    public int HomeZ { get; set; }
    public bool ReturningHome { get; set; }
}
