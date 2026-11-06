using Fineage.Zone.Geo;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
using Fineage.Zone.Net;
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

public enum ZoneEntityKind
{
    Npc = 0,
    GroundItem = 1,
    Player = 2
}

sealed class ZoneEntity
{
    public int ObjectId { get; init; }
    public ZoneEntityKind Kind { get; init; }
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>Mobius instance type (Merchant, Guard, Monster, …).</summary>
    public string NpcType { get; init; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    /// <summary>Facing 0..65535 (Mobius heading); updated while moving and when swinging at a target.</summary>
    public int Heading { get; set; }
    /// <summary>Players: changes on class transfer (Mobius setClassId).</summary>
    public int ClassId { get; set; }
    public int NpcId { get; init; }
    public bool Attackable { get; init; }
    public int CurHp { get; set; }
    public int MaxHp { get; set; }
    public int CurMp { get; set; }
    public int MaxMp { get; set; }
    public int Level { get; set; } = 1;
    public long Exp { get; set; }
    public int Sp { get; set; }
    /// <summary>NPC kill reward from the Mobius template (acquire exp/sp). Zero for players.</summary>
    public long RewardExp { get; init; }
    public int RewardSp { get; init; }
    public QuestJournal Quests { get; set; } = new();
    public int Race { get; init; }
    public int Sex { get; init; }
    public bool Hero { get; set; }
    public bool InClan { get; set; }
    public bool ClanLeader { get; set; }
    public int PledgeClass { get; set; }
    public int CastleId { get; set; }
    public int HideoutId { get; set; }
    public bool PvpFlagged { get; set; }
    /// <summary>Unix ms when the PvP flag expires (Mobius <c>_pvpFlagLasts</c>).</summary>
    public long PvpFlagUntilMs { get; set; }
    public int PkKills { get; set; }
    public int PvpKills { get; set; }
    /// <summary>Mobius karma (Chaotic); death item drop uses karma rates when &gt; 0 and PK ≥ limit.</summary>
    public int Karma { get; set; }
    public int ExpertiseBonus { get; set; }
    /// <summary>Weapon expertise penalty, 0–4. Updated when gear changes and on enter.</summary>
    public int WeaponGradePenalty { get; set; }
    /// <summary>Armor expertise penalty, 0–4. Updated when gear changes and on enter.</summary>
    public int ArmorGradePenalty { get; set; }
    /// <summary>
    /// Grade Penalty level, 0–4 — the level skill 4267 is added at (Mobius
    /// <c>addSkill(4267, max(weaponPenalty, armorPenalty))</c>). The stat fold needs this single level, not the two raw
    /// penalties. Derived from gear, so never persisted.
    /// </summary>
    public int GradePenaltyLevel { get; set; }
    /// <summary>
    /// Death Penalty level, 0–15 (Mobius <c>Player._deathPenaltyBuffLevel</c>). Persisted: it survives logout and
    /// shrinks only on its own recovery rules. Folds skill 5076 into the stats and is reported on
    /// <c>EtcStatusUpdate</c>.
    /// </summary>
    public int DeathPenaltyLevel { get; set; }
    /// <summary>
    /// Weight Penalty level, 0–4 (Mobius <c>Player._curWeightPenalty</c>). Derived from the load and recomputed on
    /// every inventory change and on enter — never stored. Skips persistence on purpose: it is a function of the bag.
    /// </summary>
    public int WeightPenaltyLevel { get; set; }
    /// <summary>
    /// Fractional HP/MP regen left over from the previous tick. Mobius keeps vitals as doubles, so a halved rate
    /// (Weight Penalty 4270) still accumulates; here the integer vitals need the remainder carried explicitly.
    /// </summary>
    public double RegenCarryHp { get; set; }
    public double RegenCarryMp { get; set; }
    /// <summary>When true, regen rates double (classic sitting rest).</summary>
    public bool Sitting { get; set; }
    /// <summary>Mobius <c>PrivateStoreType</c> (None / Sell / Buy).</summary>
    public byte PrivateStoreType { get; set; }
    /// <summary>Store title shown above the sitting pawn (Mobius PrivateStoreMsgSell/Buy).</summary>
    public string PrivateStoreMessage { get; set; } = "";
    /// <summary>Active sell listings while <see cref="PrivateStoreType"/> is Sell.</summary>
    public List<PrivateStoreSellListing> PrivateSellList { get; } = [];
    /// <summary>Active buy listings while <see cref="PrivateStoreType"/> is Buy (by template id).</summary>
    public List<PrivateStoreBuyListing> PrivateBuyList { get; } = [];
    /// <summary>Active manufacture listings while <see cref="PrivateStoreType"/> is Manufacture.</summary>
    public List<ManufactureListing> ManufactureList { get; } = [];
    /// <summary>Mobius recipe book: known <c>RecipeList</c> ids (make-self / book open).</summary>
    public HashSet<int> KnownRecipes { get; } = [];
    /// <summary>NPC Mobius physical attack (0 = unknown → level placeholder). Players read the class template.</summary>
    public double PAtk { get; init; }
    /// <summary>NPC Mobius <c>vitals hpRegen</c>: HP restored per 3 s regen tick (players use the class table).</summary>
    public double HpRegen { get; init; }
    /// <summary>NPC Mobius <c>vitals mpRegen</c>: MP restored per 3 s regen tick.</summary>
    public double MpRegen { get; init; }
    /// <summary>NPC Mobius physical defence (0 = unknown).</summary>
    public double PDef { get; init; }
    /// <summary>NPC Mobius attack speed (Pspd, 0 = default 253).</summary>
    public int PAtkSpd { get; init; }
    /// <summary>NPC template physical attack range (Mobius atkRange: 40 melee, 1100 archers); 0 = default.</summary>
    public int AttackRange { get; init; }
    /// <summary>Mobius <c>ai isAggressive</c>; attacks players inside <see cref="AggroRange"/> on its own.</summary>
    public bool Aggressive { get; init; }
    public int AggroRange { get; init; }
    /// <summary>Mobius <c>clanHelpRange</c> + <c>clanList</c>: same-clan mobs in range join the fight.</summary>
    public int ClanHelpRange { get; init; }
    public IReadOnlyList<string>? Clans { get; init; }
    public IReadOnlyList<int>? IgnoreNpcIds { get; init; }
    /// <summary>NPC: Mobius AggroList (attacker objectId → hate). <see cref="AggroTargetId"/> is the most hated.</summary>
    public Dictionary<int, long> HateList { get; } = [];
    /// <summary>
    /// NPC: damage dealt per attacker (Mobius <c>AggroInfo.getDamage</c>). Exp/SP are paid as a share of this, so a
    /// kill belongs to everyone who hit the mob, not only to the last blow.
    /// </summary>
    public Dictionary<int, long> DamageBy { get; } = [];
    public long NextAggroScanMs { get; set; }
    public long NextClanCallMs { get; set; }
    /// <summary>Attack stance the watchers were last told (Mobius AutoAttackStart / AutoAttackStop).</summary>
    public bool StanceShown { get; set; }
    /// <summary>NPC Mobius base accuracy / evasion / crit / random spread / DEX (players use the class template).</summary>
    public double BaseAccuracy { get; init; }
    public double BaseEvasion { get; init; }
    public int BaseCritical { get; init; }
    public int RandomDamage { get; init; }
    /// <summary>
    /// Mobius <c>getDEX()</c>: feeds <c>FuncMoveSpeed</c> (run/walk/swim speed × DEX bonus), accuracy, evasion and
    /// crit. Players copy it from the class template and re-copy it on class change.
    /// </summary>
    public int Dex { get; set; } = 30;
    /// <summary>NPC Mobius shield defence / block rate (players read the equipped shield).</summary>
    public int ShieldDef { get; init; }
    public int ShieldRate { get; init; }
    /// <summary>NPC Mobius magic attack / defence.</summary>
    public double MAtk { get; init; }
    public double MDef { get; init; }
    /// <summary>Mobius ChargedShot SOULSHOTS: weapon is charged; next swing doubles P.Atk and clears it.</summary>
    public bool SoulshotCharged { get; set; }
    /// <summary>0 none, 1 spiritshot, 2 blessed spiritshot (Mobius ShotType SPIRITSHOTS / BLESSED_SPIRITSHOTS).</summary>
    public int SpiritshotCharge { get; set; }
    /// <summary>NPC remaining Mobius SoulShot stock (template starting count; decremented on fire).</summary>
    public int NpcSoulShotLeft { get; set; }
    /// <summary>NPC SoulShotRate % (template).</summary>
    public int NpcSoulShotChance { get; set; }
    /// <summary>NPC remaining SpiritShot stock.</summary>
    public int NpcSpiritShotLeft { get; set; }
    public int NpcSpiritShotChance { get; set; }
    /// <summary>Mobius weapon <c>_defenceTraits</c> multipliers (sword/bow/…); null = all 1.0.</summary>
    public IReadOnlyDictionary<string, double>? DefenceTraits { get; init; }
    /// <summary>Mobius AI attack skills (skillList ∩ skill-combat); null = melee only.</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSkill>? AiSkills { get; init; }
    /// <summary>Mobius AISkillScope.HEAL rows (self/clan heal when wounded).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiHealSkills { get; set; }
    /// <summary>Mobius AISkillScope.BUFF rows (self buff when missing).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiBuffSkills { get; set; }
    /// <summary>Mobius AISkillScope.DEBUFF rows (debuff hate target).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiDebuffSkills { get; set; }
    /// <summary>Mobius AISkillScope.IMMOBILIZE (sleep/root/stun/paralyze).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiImmobilizeSkills { get; set; }
    /// <summary>Mobius AISkillScope.COT silence.</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiSilenceSkills { get; set; }
    /// <summary>Mobius AISkillScope.SUICIDE (Death Bomb when HP &lt; 30%).</summary>
    public IReadOnlyList<Fineage.Zone.Npcs.NpcAiSupportSkill>? AiSuicideSkills { get; set; }
    public int MinSkillChance { get; init; } = 7;
    public int MaxSkillChance { get; init; } = 15;
    /// <summary>Mobius auto soulshot set: shot item ids recharged after each discharge.</summary>
    public HashSet<int> AutoShots { get; } = [];
    /// <summary>Mobius ShortCuts keyed by world slot (slot + page * 12).</summary>
    public Dictionary<int, Fineage.Login.StoredShortcut> Shortcuts { get; } = [];
    /// <summary>Trainer-learned skills (persisted; Mobius character_skills). Merged into KnownSkills.</summary>
    public Dictionary<int, int> LearnedSkills { get; } = [];
    /// <summary>True while an abnormal with combat stat funcs is active (UserInfo refresh on change).</summary>
    public bool HasStatAbnormal { get; set; }
    /// <summary>
    /// Base run speed (Mobius <c>CreatureTemplate.baseRunSpd</c>). Buffs and debuffs change the live
    /// <see cref="RunSpeed"/> through the <c>runSpd</c> (<c>Stat.MOVE_SPEED</c>) functions; the base is only rewritten
    /// by a template / class change.
    /// </summary>
    public float BaseRunSpeed { get; set; }

    /// <summary>
    /// Live run speed sent on the wire. Kept denormalised on the entity: spawn / MoveState / UserInfo / ChangeMoveType
    /// all carry it, and <see cref="RefreshRunSpeed"/> recomputes it when buffs, passives or the class change.
    /// </summary>
    public float RunSpeed
    {
        get => _runSpeed ?? BaseRunSpeed;
        set
        {
            BaseRunSpeed = value;
            _runSpeed = value;
        }
    }

    float? _runSpeed;

    /// <summary>Sets the live (buffed) run speed without touching the base template value.</summary>
    internal void SetLiveRunSpeed(float speed) => _runSpeed = speed;

    /// <summary>
    /// Base walk speed (Mobius <c>CreatureTemplate.baseWalkSpd</c>). <c>Stat.MOVE_SPEED("runSpd")</c> is the one speed
    /// stat in Mobius and <c>CreatureStat.getWalkSpeed</c> folds it onto this base, so buffs and debuffs move walk too.
    /// </summary>
    public float BaseWalkSpeed { get; set; }

    /// <summary>
    /// Live walk speed sent on the wire; mirrors <see cref="RunSpeed"/> and is recomputed with it.
    /// </summary>
    public float WalkSpeed
    {
        get => _walkSpeed ?? BaseWalkSpeed;
        set
        {
            BaseWalkSpeed = value;
            _walkSpeed = value;
        }
    }

    float? _walkSpeed;

    /// <summary>Sets the live (buffed) walk speed without touching the base template value.</summary>
    internal void SetLiveWalkSpeed(float speed) => _walkSpeed = speed;

    /// <summary>
    /// Mobius <c>baseMoveSpd/fastSwim</c> (<c>CreatureTemplate.java:147</c>, falls back to <c>baseRunSpd</c>).
    /// 0 = not in the template → use the run base, exactly like Mobius.
    /// </summary>
    public float BaseSwimRunSpeed { get; set; }

    /// <summary>
    /// Mobius <c>baseMoveSpd/slowSwim</c> (<c>CreatureTemplate.java:148</c>, falls back to <c>baseWalkSpd</c>).
    /// </summary>
    public float BaseSwimWalkSpeed { get; set; }

    /// <summary>Live swim run speed (Mobius <c>getSwimRunSpeed()</c>); recomputed with the land speeds.</summary>
    public float SwimRunSpeed
    {
        get => _swimRunSpeed ?? (BaseSwimRunSpeed > 0 ? BaseSwimRunSpeed : BaseRunSpeed);
        set
        {
            BaseSwimRunSpeed = value;
            _swimRunSpeed = value;
        }
    }

    float? _swimRunSpeed;

    /// <summary>Live swim walk speed (Mobius <c>getSwimWalkSpeed()</c>).</summary>
    public float SwimWalkSpeed
    {
        get => _swimWalkSpeed ?? (BaseSwimWalkSpeed > 0 ? BaseSwimWalkSpeed : BaseWalkSpeed);
        set
        {
            BaseSwimWalkSpeed = value;
            _swimWalkSpeed = value;
        }
    }

    float? _swimWalkSpeed;

    /// <summary>Sets the live swim speeds without touching the base template values.</summary>
    internal void SetLiveSwimSpeed(float run, float walk)
    {
        _swimRunSpeed = run;
        _swimWalkSpeed = walk;
    }

    public bool Running { get; set; } = true;
    /// <summary>Mobius isInsideZone(WATER): last water state seen by the mover (swim speed while true).</summary>
    public bool InWater { get; set; }
    /// <summary>Mobius MoveData: server-side move in progress (Net path). Position advances by speed each tick.</summary>
    public bool MoveActive { get; set; }
    public int MoveDestX { get; set; }
    public int MoveDestY { get; set; }
    public int MoveDestZ { get; set; }
    public long MoveLastTickMs { get; set; }
    /// <summary>Mobius MoveData.geoPath: remaining geo-path waypoints after the current segment.</summary>
    public Queue<(int X, int Y, int Z)> MovePath { get; } = new();
    /// <summary>Segment switched to the next waypoint inside AdvanceMove; tick broadcasts MoveToLocation for it.</summary>
    public bool MoveSegmentDirty { get; set; }
    /// <summary>Position of the last in-move knownlist refresh (Mobius region/knownlist revalidate while running).</summary>
    public int AoiRefreshX { get; set; }
    public int AoiRefreshY { get; set; }
    public ZoneSession? Session { get; init; }
    public PlayerInventory? Inventory { get; init; }
    /// <summary>
    /// Player is dead. Equip and unequip are rejected. Combat sets this; spawn leaves it false.
    /// CurHp is not the signal: a living player can sit at low HP while regen heals them.
    /// </summary>
    public bool Dead { get; set; }
    /// <summary>Scroll object id selected for the enchant window. 0 when the window is closed.</summary>
    public int ActiveEnchantObjectId { get; set; }
    public int GroundItemId { get; set; }
    public int GroundCount { get; set; } = 1;
    public bool GroundStackable { get; set; }
    /// <summary>Mobius DropProtection owner (killer); 0 = anyone may pick up.</summary>
    public int GroundOwnerId { get; set; }
    /// <summary>Unix ms until only <see cref="GroundOwnerId"/> (or their party) may pick up.</summary>
    public long GroundProtectUntilMs { get; set; }
    /// <summary>Mobius ItemsAutoDestroy: unix ms when the ground item is removed; 0 = never.</summary>
    public long GroundExpireAtMs { get; set; }
    /// <summary>Mobius spoil: who activated spoil on this attackable (0 = none).</summary>
    public int SpoilerObjectId { get; set; }
    /// <summary>Pre-rolled corpse drops after death when spoiled; cleared by sweeper.</summary>
    public List<Drops.DropStack> SweepItems { get; } = [];
    /// <summary>Dead NPC kept in world until sweeper takes <see cref="SweepItems"/>.</summary>
    public bool CorpseAwaitingSweep { get; set; }
    public List<ZoneAbnormalEntry> Abnormals { get; } = [];
    /// <summary>Known skill id to level (auto-get tree on enter).</summary>
    public Dictionary<int, int> KnownSkills { get; } = [];
    /// <summary>Skill id to unix-ms when reuse expires.</summary>
    public Dictionary<int, long> SkillReuseUntilMs { get; } = [];
    /// <summary>
    /// Item id to unix-ms when the item may be used again (Mobius <c>ItemInstance.getReuseDelay</c> / reuse stamp).
    /// Item skills carry a reuse delay; the stamp lives on the character so a stack shares one timer.
    /// </summary>
    public Dictionary<int, long> ItemReuseUntilMs { get; } = [];
    public HashSet<int> Knownlist { get; } = [];
    public int ClanId { get; set; }
    /// <summary>Attack stance end, unix milliseconds. Leave and dismiss are refused until this passes.</summary>
    public long CombatUntilUnixMs { get; set; }
    /// <summary>Catacomb or necropolis id while inside a Seven Signs gate. 0 is outside. Runtime only, same as Mobius <c>isIn7sDungeon</c>.</summary>
    public int DungeonId { get; set; }
    /// <summary>0 is the open world. An Olympiad stadium uses its zone id so the pair does not share knownlist with the town.</summary>
    public int InstanceId { get; set; }
    /// <summary>Private warehouse. Null on NPCs.</summary>
    public PrivateWarehouse? Warehouse { get; set; }
    /// <summary>First character created on the account. Mobius <c>Player.isNewbie</c> when the account had no characters yet.</summary>
    public bool Newbie { get; set; } = true;
    /// <summary>Mobius <c>Creature.isCastingNow</c>: unix ms until the current cast ends. Movement is refused before this.</summary>
    public long CastingUntilMs { get; set; }
    /// <summary>Skill damage queued for Mobius <c>onMagicHitTimer</c> (applied at hitTime, not on key press).</summary>
    public PendingSkillHit? PendingHit { get; set; }
    /// <summary>NPC: most-hated player (Mobius <c>AggroList</c> top, simplified to last attacker).</summary>
    public int AggroTargetId { get; set; }
    /// <summary>NPC: next melee swing time, unix ms.</summary>
    public long NextMobAttackMs { get; set; }
    /// <summary>NPC: last chase destination broadcast (Mobius MoveToPawn re-sent when the pawn moves).</summary>
    public int ChaseDestX { get; set; }
    public int ChaseDestY { get; set; }
    public bool Chasing { get; set; }
    /// <summary>NPC: last AI tick, unix ms (movement integration).</summary>
    public long LastAiTickMs { get; set; }
    /// <summary>NPC: spawn point captured on first aggro (Mobius <c>Attackable.returnHome</c> target).</summary>
    public bool HomeSet { get; set; }
    public int HomeX { get; set; }
    public int HomeY { get; set; }
    public int HomeZ { get; set; }
    /// <summary>NPC: walking back to <see cref="HomeX"/>/<see cref="HomeY"/> after forgetting its target.</summary>
    public bool ReturningHome { get; set; }
    /// <summary>NPC corpse: unix ms when DecayTaskManager removes it. 0 = alive / not scheduled.</summary>
    public long DecayAtMs { get; set; }
    /// <summary>Mobius merchant price config castle. 0 when the NPC is not in a taxed town.</summary>
    public int MerchantCastleId { get; set; }
    /// <summary>Town name for TerritoryStatus (%territory%); null = castle name.</summary>
    public string? MerchantTown { get; set; }
    /// <summary>Town base tax percent from Mobius <c>MerchantPriceConfig.xml</c>. Castle tax is separate.</summary>
    public int MerchantBaseTaxPercent { get; set; }
    /// <summary>Buylist or multisell this player opened. Null until a shop bypass.</summary>
    public ShopSession? Shop { get; set; }
}

/// <summary>
/// One zone. Players and NPCs are shared entities. Each player keeps a knownlist
/// of what their client has been told; AOI decides membership.
/// </summary>
public sealed partial class ZoneWorld
{
    public const int MaxSayLength = ChatProtocol.MaxTextLength;
    static readonly TimeSpan FanoutTimeout = TimeSpan.FromSeconds(2);

    readonly IAoi _aoi;
    readonly GameData _game;
    readonly ItemObjectIdAllocator _itemIds;
    readonly Random _spawnRandom;
    readonly Random _dropRandom;
    readonly IAccountStore? _accounts;
    readonly bool _expertisePenalty;
    readonly IEnchantRandom _enchantRandom;
    readonly EnchantRates _enchantRates;
    readonly QuestMachine _quests;
    readonly SevenSignsEngine _sevenSigns;
    readonly DungeonCatalog _dungeons;
    readonly DungeonAccess _dungeonAccess;
    readonly SiegeSettings _siegeSettings;
    readonly SiegeBoard _siege;
    readonly NpcHtmlCatalog _html;
    readonly BypassRegistry _bypasses;
    readonly OlympiadBoard _olympiad;
    readonly TeleportCatalog _teleports;
    readonly NewbieBuffCatalog _newbieBuffs;
    readonly ItemWeights _weights;
    /// <summary>Mobius <c>handler="ItemSkills"</c> items: what a potion or scroll actually does (data/item-skills.json).</summary>
    readonly Fineage.Game.Services.ItemSkillCatalog _itemSkills;
    /// <summary>Mobius <c>MapRegionManager</c>: the "nearest town" a Scroll of Escape lands in.</summary>
    readonly Fineage.Game.Services.MapRegionCatalog _mapRegions;
    readonly int _teleportOffset;
    readonly Random _serviceRandom;
    /// <summary>Hit/miss, crit and damage spread rolls (Mobius Rnd in Formulas).</summary>
    readonly Random _combatRandom;
    /// <summary>Mobius GameTimeTaskManager (IG day / night for HitConditionBonus dark).</summary>
    readonly ZoneGameTime _gameTime;
    /// <summary>Buff/debuff combat stat funcs (Mobius skill &lt;for&gt;&lt;effect&gt;).</summary>
    readonly SkillStatEffectCatalog _statEffects;
    /// <summary>Mobius attack-skill power / MP / timing (data/skill-combat.json).</summary>
    readonly SkillCombatCatalog _skillCombat;
    /// <summary>Mobius AISkillScope HEAL/BUFF/DEBUFF rows (data/npc-ai-support.json).</summary>
    readonly Fineage.Zone.Npcs.NpcAiSupportCatalog _npcAiSupport;
    readonly SkillStatEffectCatalog _passives;
    readonly BuyListCatalog _buyLists;
    readonly MultiSellCatalog _multisells;
    readonly ShopStock _shopStock;
    /// <summary>Mobius death rules: exp-bar share lost, Death Penalty chance, respawn vitals restore.</summary>
    readonly DeathRuleCatalog _deathRules;
    /// <summary>Mobius <c>Player.onDieDropItem</c> rates (data/death-drop.json).</summary>
    readonly Fineage.Game.Services.DeathDropRules _deathDrop;
    /// <summary>Mobius <c>Rnd.get</c> for the Death Penalty roll (separate from the damage rolls).</summary>
    readonly Random _deathRandom;
    readonly Dictionary<int, ZoneEntity> _byId = [];
    readonly DropCatalog _dropCatalog;
    readonly DropRates _dropRates;
    readonly object _gate = new();
    readonly bool _npcRegen;
    readonly GeoEngine _geo;
    static readonly Lazy<GeoEngine> SharedGeo = new(GeoEngine.LoadDefault);
    static readonly Lazy<DoorTable> SharedDoors = new(DoorTable.LoadDefault);
    static readonly Lazy<WaterZones> SharedWater = new(WaterZones.LoadDefault);

    /// <summary>Mobius DoorData (movement collision). Settable for tests.</summary>
    internal DoorTable Doors { get; set; } = DoorTable.Empty;
    /// <summary>Mobius WaterZone cuboids (swim speed). Settable for tests.</summary>
    internal WaterZones Water { get; set; } = WaterZones.Empty;

    /// <summary>Server geodata (empty when no region files are present).</summary>
    public GeoEngine GeoData => _geo;

    public ZoneWorld(
        IAoi? aoi = null,
        GameData? game = null,
        ItemObjectIdAllocator? itemIds = null,
        Random? spawnRandom = null,
        Random? dropRandom = null,
        IAccountStore? accounts = null,
        bool? expertisePenalty = null,
        SpawnCatalog? spawnCatalog = null,
        DropCatalog? dropCatalog = null,
        DropRates? dropRates = null,
        string? activeMapId = null,
        Func<DateTimeOffset>? clock = null,
        IEnchantRandom? enchantRandom = null,
        EnchantRates? enchantRates = null,
        SevenSignsEngine? sevenSigns = null,
        bool includeEnabledMaps = false,
        DungeonCatalog? dungeons = null,
        DungeonAccessOptions? dungeonAccess = null,
        SiegeSettings? siege = null,
        BossSchedule? bosses = null,
        Random? bossRandom = null,
        NpcHtmlCatalog? html = null,
        BypassRegistry? bypasses = null,
        OlympiadBoard? olympiad = null,
        OlympiadSettings? olympiadSettings = null,
        Random? olympiadRandom = null,
        TeleportCatalog? teleports = null,
        NewbieBuffCatalog? newbieBuffs = null,
        ItemWeights? weights = null,
        int? teleportOffset = null,
        Random? serviceRandom = null,
        BuyListCatalog? buyLists = null,
        MultiSellCatalog? multisells = null,
        GeoEngine? geo = null,
        Random? combatRandom = null,
        SkillStatEffectCatalog? statEffects = null,
        SkillCombatCatalog? skillCombat = null,
        Fineage.Zone.Npcs.NpcAiSupportCatalog? npcAiSupport = null,
        SkillStatEffectCatalog? passives = null,
        DeathRuleCatalog? deathRules = null,
        Fineage.Game.Services.DeathDropRules? deathDrop = null,
        Random? deathRandom = null,
        bool? npcRegen = null,
        Fineage.Game.Services.ItemSkillCatalog? itemSkills = null,
        Fineage.Game.Services.MapRegionCatalog? mapRegions = null,
        ZoneGameTime? gameTime = null)
    {
        _npcRegen = npcRegen ?? ZoneNpcSettings.NpcRegenEnabled;
        _skillCombat = skillCombat ?? SkillCombatCatalog.LoadDefault();
        _npcAiSupport = npcAiSupport ?? Fineage.Zone.Npcs.NpcAiSupportCatalog.LoadDefault();
        _statEffects = statEffects ?? SkillStatEffectCatalog.LoadDefault();
        _passives = passives ?? SkillStatEffectCatalog.LoadPassives();
        _combatRandom = combatRandom ?? Random.Shared;
        _gameTime = gameTime ?? ZoneGameTime.FromLocalMidnight();
        _aoi = aoi ?? new SingleCellAoi();
        _game = game ?? GameData.LoadDefault();
        _itemIds = itemIds ?? new ItemObjectIdAllocator();
        _spawnRandom = spawnRandom ?? Random.Shared;
        _dropRandom = dropRandom ?? spawnRandom ?? Random.Shared;
        _accounts = accounts;
        _expertisePenalty = expertisePenalty ?? ZoneGearSettings.ExpertisePenalty;
        _spawnCatalog = spawnCatalog ?? SpawnCatalog.LoadDefault();
        _dropCatalog = dropCatalog ?? DropCatalog.LoadDefault();
        _dropRates = dropRates ?? DropRates.Retail;
        _activeMapId = string.IsNullOrWhiteSpace(activeMapId) ? SpawnCatalog.DefaultMapId : activeMapId.Trim();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _enchantRandom = enchantRandom ?? SharedEnchantRandom.Instance;
        _enchantRates = enchantRates ?? EnchantRates.Default;
        _enchantRates.Validate();
        _includeEnabledMaps = includeEnabledMaps;
        _quests = new QuestMachine(_game.Quests, _game.Items);
        _sevenSigns = sevenSigns ?? new SevenSignsEngine(new SystemGameClock());
        _dungeons = dungeons ?? DungeonCatalog.LoadDefault();
        _dungeonAccess = new DungeonAccess(_dungeons, dungeonAccess ?? DungeonAccessOptions.FromEnvironment());
        _siegeSettings = siege ?? SiegeSettings.FromEnvironment();
        _siege = new SiegeBoard(_clock().ToUnixTimeMilliseconds(), _siegeSettings);
        _bosses = bosses ?? BossSchedule.LoadDefault(_clock, bossRandom);
        _html = html ?? NpcHtmlCatalog.LoadDefault();
        _bypasses = bypasses ?? BypassRegistry.CreateDefault(_html);
        _olympiad = olympiad ?? new OlympiadBoard(_clock(), olympiadSettings ?? OlympiadSettings.FromEnvironment(), random: olympiadRandom);
        _teleports = teleports ?? TeleportCatalog.LoadDefault();
        _newbieBuffs = newbieBuffs ?? NewbieBuffCatalog.LoadDefault();
        _weights = weights ?? ItemWeights.LoadDefault();
        _teleportOffset = teleportOffset ?? ReadTeleportOffset();
        _serviceRandom = serviceRandom ?? Random.Shared;
        _buyLists = buyLists ?? BuyListCatalog.LoadDefault();
        _multisells = multisells ?? MultiSellCatalog.LoadDefault();
        _shopStock = new ShopStock(_buyLists);
        _deathRules = deathRules ?? DeathRuleCatalog.LoadDefault();
        _deathDrop = deathDrop ?? Fineage.Game.Services.DeathDropRules.LoadDefault();
        _deathRandom = deathRandom ?? Random.Shared;
        _itemSkills = itemSkills ?? Fineage.Game.Services.ItemSkillCatalog.LoadDefault();
        _mapRegions = mapRegions ?? Fineage.Game.Services.MapRegionCatalog.LoadDefault();
        _geo = geo ?? SharedGeo.Value;
        if (geo == null)
        {
            // Production world: real datapack doors / water. Tests that inject geo keep the empty tables.
            Doors = SharedDoors.Value;
            Water = SharedWater.Value;
        }
        BootstrapSpawns();
        BootstrapBosses();
        BootstrapQuestNpcs();
        LoadClans();
        LoadSiege();
    }

    /// <summary>Bypass handlers. A later <see cref="BypassRegistry.Register"/> replaces a stub.</summary>
    public BypassRegistry Bypasses => _bypasses;

    /// <summary>Castle row for shop tax. Tests set <see cref="Siege.CastleSiege.OwnerClanId"/> and tax on the result.</summary>
    internal CastleSiege? FindCastle(int castleId) => _siege.Find(castleId);

    public IReadOnlyList<int> NpcObjectIds
    {
        get
        {
            lock (_gate)
            {
                var ids = new List<int>();
                foreach (var entity in _byId.Values)
                {
                    if (entity.Kind == ZoneEntityKind.Npc)
                        ids.Add(entity.ObjectId);
                }

                ids.Sort();
                return ids;
            }
        }
    }

    /// <summary>Players with an active zone session in the world (for OTEL <c>fineage.zone.online</c>).</summary>
    public int OnlinePlayerCount
    {
        get
        {
            lock (_gate)
            {
                int count = 0;
                foreach (var entity in _byId.Values)
                {
                    if (entity.Kind == ZoneEntityKind.Player && entity.Session is { InWorld: true })
                        count++;
                }

                return count;
            }
        }
    }

    /// <summary>Live player entity after a successful enter (same object Net enter projects from).</summary>
    internal bool TryGetLivePlayer(ZoneSession session, out ZoneEntity entity)
    {
        lock (_gate)
            return TryGetLivePlayerUnlocked(session, out entity);
    }

    /// <summary>
    /// Optional Net PacketEnvelope bridge for sim/AI fanout (MoveState) while V2 flushes stay on ZoneSession streams.
    /// Set by the Net host when it owns the zone session.
    /// </summary>
    internal INetPresenceBridge? NetBridge { get; set; }

    /// <summary>
    /// Live players currently on <paramref name="session"/>'s knownlist (post-enter / post-move hysteresis).
    /// Used by Net presence fanout so peers match V2 CharInfo/MoveToAck recipients.
    /// </summary>
    internal List<ZoneEntity> SnapshotKnownPlayers(ZoneSession session)
    {
        lock (_gate)
        {
            if (!TryGetLivePlayerUnlocked(session, out var self))
                return [];

            var players = new List<ZoneEntity>(self.Knownlist.Count);
            foreach (int id in self.Knownlist)
            {
                if (_byId.TryGetValue(id, out var other) &&
                    other.Kind == ZoneEntityKind.Player &&
                    other.Session is { InWorld: true } &&
                    other.InstanceId == self.InstanceId)
                    players.Add(other);
            }

            players.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));
            return players;
        }
    }

    /// <summary>
    /// All live entities on the session knownlist (players + NPCs + ground items).
    /// </summary>
    internal List<ZoneEntity> SnapshotKnownEntities(ZoneSession session)
    {
        lock (_gate)
        {
            if (!TryGetLivePlayerUnlocked(session, out var self))
                return [];

            var list = new List<ZoneEntity>(self.Knownlist.Count);
            foreach (int id in self.Knownlist)
            {
                if (!_byId.TryGetValue(id, out var other) || other.InstanceId != self.InstanceId)
                    continue;
                if (other.Kind == ZoneEntityKind.Player && other.Session is not { InWorld: true })
                    continue;
                list.Add(other);
            }

            list.Sort(static (a, b) => a.ObjectId.CompareTo(b.ObjectId));
            return list;
        }
    }

    /// <summary>Object ids on the session's knownlist (any entity kind). For move enter/leave diffs.</summary>
    internal HashSet<int> SnapshotKnownIds(ZoneSession session)
    {
        lock (_gate)
        {
            if (!TryGetLivePlayerUnlocked(session, out var self))
                return [];
            return new HashSet<int>(self.Knownlist);
        }
    }

    bool TryGetLivePlayerUnlocked(ZoneSession session, out ZoneEntity entity)
    {
        if (session.ObjectId != 0 &&
            _byId.TryGetValue(session.ObjectId, out entity!) &&
            entity.Kind == ZoneEntityKind.Player &&
            ReferenceEquals(entity.Session, session) &&
            session.InWorld)
            return true;
        entity = null!;
        return false;
    }

    /// <summary>Live player by object id (for Net knownlist enter diffs).</summary>
    internal bool TryGetLivePlayerById(int objectId, out ZoneEntity entity)
    {
        lock (_gate)
        {
            if (objectId != 0 &&
                _byId.TryGetValue(objectId, out entity!) &&
                entity.Kind == ZoneEntityKind.Player &&
                entity.Session is { InWorld: true })
                return true;
            entity = null!;
            return false;
        }
    }

    /// <summary>Any world entity by id (player / NPC) for Net knownlist diffs.</summary>
    internal bool TryGetEntityById(int objectId, out ZoneEntity entity)
    {
        lock (_gate)
        {
            if (objectId != 0 && _byId.TryGetValue(objectId, out entity!))
                return true;
            entity = null!;
            return false;
        }
    }

    /// <summary>
    /// Test hook: kill <paramref name="player"/> as if <paramref name="killer"/> had landed the final blow, so the
    /// exp loss and Death Penalty roll run through the production path.
    /// </summary>
    internal void KillPlayerByNpcForTest(ZoneEntity player, ZoneEntity killer)
    {
        lock (_gate)
            ApplyPlayerDeath(player, killer.ObjectId);
    }

    /// <summary>Test hook: rebuild the book after a test mutates <see cref="ZoneEntity.LearnedSkills"/>.</summary>
    internal void RefreshKnownSkillsForTest(ZoneEntity player)
    {
        lock (_gate)
            RefreshKnownSkills(player);
    }

    /// <summary>Test hook: run Mobius <c>checkPlayerSkills</c> without going through a death.</summary>
    internal void DecreaseSkillsOnDelevelForTest(ZoneEntity player)
    {
        lock (_gate)
            DecreaseSkillsOnDelevel(player);
    }

    public Task<bool> TryEnterAsync(ZoneSession session, CancellationToken ct)
    {
        bool ok;
        int known;
        lock (_gate)
            ok = TryEnter(session, out known);
        if (!ok)
            return Task.FromResult(false);

        ZoneLog.EnterWorld(session, known);
        return Task.FromResult(true);
    }

    public Task LeaveAsync(ZoneSession session, CancellationToken ct)
    {
        bool removed;
        lock (_gate)
            removed = Unlink(session);
        if (!removed)
            return Task.CompletedTask;

        _sevenSigns.Save();
        _bosses.Save();
        ZoneLog.LeaveWorld(session);
        return Task.CompletedTask;
    }

    public Task<string?> TryMoveAsync(ZoneSession session, MoveToMsg move, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = TryMove(session, move);
        if (error != null)
            return Task.FromResult<string?>(error);

        MaybeSavePresenceOnMove(session);
        return Task.FromResult<string?>(null);
    }

    bool TryEnter(ZoneSession session, out int known)
    {
        known = 0;
        if (session.ObjectId <= 0)
            return false;
        if (_byId.TryGetValue(session.ObjectId, out var occupied) && occupied.Kind != ZoneEntityKind.Player)
            return false;

        if (session.InWorld)
            Unlink(session);
        else if (_byId.TryGetValue(session.ObjectId, out var current) &&
                 current.Session != null &&
                 !ReferenceEquals(current.Session, session))
            Unlink(current.Session);

        var spawn = SpawnFor(session);
        var inventory = LoadInventory(session);
        var stored = _accounts?.FindById(session.Account, session.ObjectId);
        var vitals = LoadVitals(session);
        var warehouse = new PrivateWarehouse(_game.Items);
        if (stored?.Warehouse != null)
            warehouse.Restore(stored.Warehouse.Select(item => new WarehouseRow(item.ItemId, item.Count, item.EnchantLevel, item.ObjectId)));
        RememberPenalties(session);
        var entity = new ZoneEntity
        {
            ObjectId = session.ObjectId,
            Kind = ZoneEntityKind.Player,
            Name = session.Name,
            X = spawn.X,
            Y = spawn.Y,
            Z = spawn.Z,
            ClassId = session.ClassId,
            RunSpeed = MoveSpeed(session.ClassId).run,
            WalkSpeed = MoveSpeed(session.ClassId).walk,
            Dex = DexOf(session.ClassId),
            // Mobius player templates always declare baseMoveSpd slowSwim/fastSwim = 50.
            BaseSwimRunSpeed = ZoneCombat.DefaultSwimSpeed,
            BaseSwimWalkSpeed = ZoneCombat.DefaultSwimSpeed,
            Running = true,
            Session = session,
            Inventory = inventory,
            MaxHp = vitals.maxHp,
            CurHp = vitals.hp,
            MaxMp = vitals.maxMp,
            CurMp = vitals.mp,
            Level = vitals.level,
            Race = vitals.race,
            Sex = vitals.sex,
            ClanId = ClanIdFor(session.ObjectId),
            Warehouse = warehouse,
            Newbie = stored?.Newbie ?? true,
            // Mobius Player.loadCharFromDB:6578 restores _deathPenaltyBuffLevel, so the debuff outlives the session.
            DeathPenaltyLevel = Math.Clamp(stored?.DeathPenaltyLevel ?? 0, 0, DeathPenalty.MaxLevel),
            Karma = Math.Max(0, stored?.Karma ?? 0),
            PkKills = Math.Max(0, stored?.PkKills ?? 0),
            PvpKills = Math.Max(0, stored?.PvpKills ?? 0)
        };
        LoadQuestProgress(session, entity);
        LoadLearnedSkills(entity, stored);
        LoadKnownRecipes(entity, stored);
        RefreshKnownSkills(entity);
        // Mobius GameClient:493 / EnterWorld:208 — refreshOverloaded() runs before the first UserInfo, so a character
        // that logs in while overloaded already carries skill 4270's level and its slowed speeds. This must come after
        // RefreshKnownSkills: Weight Limit 150 and the armor-set weightLimit/weightPenalty passives are only in
        // KnownSkills once the skill book is rebuilt, and they change both the limit and the bonus.
        int penalty = WeightPenaltyLevelOf(entity);
        if (penalty != entity.WeightPenaltyLevel)
        {
            entity.WeightPenaltyLevel = penalty;
            RefreshRunSpeed(entity); // PlayerStatMods folds 4270, so the live speed needs the level first
        }
        LoadShortcuts(entity, stored);
        SyncClanStanding(entity);
        RefreshGradePenalty(entity);
        _byId[entity.ObjectId] = entity;
        _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        session.InWorld = true;
        session.X = entity.X;
        session.Y = entity.Y;
        session.Z = entity.Z;
        SavePresence(session, entity.X, entity.Y, entity.Z, touchAccess: true);

        // The enter sequence itself (UserInfo, ItemList, SkillList, spawns) is Net-only and lives in
        // NetZoneServer.OnEnterAsync; the knownlist is still built here because the sim owns it.
        foreach (var other in OrderedVisible(entity.ObjectId))
        {
            entity.Knownlist.Add(other.ObjectId);
            if (other.Session != null)
                other.Knownlist.Add(entity.ObjectId);
        }

        AnnounceClanEnter(entity);
        known = entity.Knownlist.Count;
        return true;
    }

    /// <summary>
    /// Sanity clamp for one client move request (L2 units). This is a "is the packet plausible" guard,
    /// not a region boundary: click-to-move crosses a whole village, and every spawn map shares it.
    /// See <see cref="MoveLimits"/>.
    /// </summary>
    static bool WithinMoveRequest(int fromX, int fromY, int toX, int toY) =>
        MoveLimits.WithinStep(fromX, fromY, toX, toY);

    string? TryMove(ZoneSession session, MoveToMsg move)
    {
        if (!IsLive(session))
            return "world";

        var entity = _byId[session.ObjectId];
        if (entity.Dead)
            return InventoryProtocol.Dead;
        if (IsImmobilized(entity))
            return ActionProtocol.Immobilized;
        if (entity.Sitting)
            return ActionProtocol.Sitting;
        if (entity.CastingUntilMs > _clock().ToUnixTimeMilliseconds())
        {
            // Mobius PlayerAI.onIntentionMoveTo: casting → ActionFailed + saveNextIntention(MOVE_TO); runs after the cast.
            session.QueuedMove = move;
            return null;
        }
        session.QueuedMove = null;
        // Sanity clamp for a single client move request, not a region gate: a click can be far away
        // (click-to-move across a village) and the same code runs for every spawn map.
        if (!WithinMoveRequest(entity.X, entity.Y, move.DestX, move.DestY))
            return "range";

        if (IsApproachSync(session, move))
        {
            // Client reporting its run along our moveToPawn path: keep the sticky approach and server dest.
            return null;
        }
        CancelAttackIntentionOnMove(session, move);
        if (NetBridge != null)
            return StartTimedMove(entity, session, move);

        // No Net client attached (unit-level world / selftest): keep the old snap so the caller's position is final.
        entity.X = move.DestX;
        entity.Y = move.DestY;
        entity.Z = move.DestZ;
        session.X = entity.X;
        session.Y = entity.Y;
        session.Z = entity.Z;
        ReconcileKnownlist(entity, ack: null);
        return null;
    }

    /// <summary>
    /// After <paramref name="entity"/> moved: AOI move, CharInfo to newly visible, MoveToAck to self + stayed,
    /// DeleteObject only past the forget radius and never for a pair locked in combat (Mobius knownlist).
    /// </summary>
    void ReconcileKnownlist(ZoneEntity entity, MoveToMsg? ack)
    {
        var before = new HashSet<int>(entity.Knownlist);
        _aoi.Move(entity.ObjectId, entity.X, entity.Y);

        var after = new HashSet<int>();
        CollectVisible(entity, after);
        foreach (int id in before)
        {
            if (!after.Contains(id) && _byId.TryGetValue(id, out var other) && other.InstanceId == entity.InstanceId &&
                (_aoi.StillKnown(entity.ObjectId, id) || CombatPinned(entity, other)))
                after.Add(id);
        }

        List<int>? netWatchers = null;
        if (NetBridge != null && entity.Session != null && ack != null)
        {
            netWatchers = [entity.ObjectId];
        }

        foreach (int id in SortedExcept(after, before))
            LinkSeen(entity, id);
        foreach (int id in SortedShared(after, before))
        {
            if (ack != null && _byId.TryGetValue(id, out var other) && other.Session != null)
                netWatchers?.Add(other.ObjectId);
        }

        foreach (int id in SortedExcept(before, after))
            Forget(entity, id);

        if (netWatchers is { Count: > 0 } && NetBridge != null && ack != null)
        {
            NetBridge.FanoutMoveState(
                netWatchers,
                entity.ObjectId,
                ack.OriginX, ack.OriginY, ack.OriginZ,
                ack.DestX, ack.DestY, ack.DestZ,
                entity.Running);
        }
    }

    static bool CombatPinned(ZoneEntity a, ZoneEntity b) =>
        (a.Session != null && (a.Session.AutoAttackTargetId == b.ObjectId || a.Session.SelectedTargetId == b.ObjectId)) ||
        (b.Session != null && (b.Session.AutoAttackTargetId == a.ObjectId || b.Session.SelectedTargetId == a.ObjectId)) ||
        a.AggroTargetId == b.ObjectId || b.AggroTargetId == a.ObjectId;

    void LinkSeen(ZoneEntity self, int otherId)
    {
        if (self.Session == null || !_byId.TryGetValue(otherId, out var other))
            return;

        self.Knownlist.Add(otherId);
        if (other.Session != null)
            other.Knownlist.Add(self.ObjectId);
    }

    void Forget(ZoneEntity self, int otherId)
    {
        if (self.Session == null)
            return;

        self.Knownlist.Remove(otherId);
        if (_byId.TryGetValue(otherId, out var other))
            other.Knownlist.Remove(self.ObjectId);
    }

    bool Unlink(ZoneSession session)
    {
        if (session.ObjectId == 0 || !_byId.TryGetValue(session.ObjectId, out var entity))
        {
            session.InWorld = false;
            return false;
        }

        if (!ReferenceEquals(entity.Session, session))
        {
            session.InWorld = false;
            return false;
        }

        var watchers = new List<ZoneSession>();
        foreach (int id in entity.Knownlist)
        {
            if (!_byId.TryGetValue(id, out var other))
                continue;
            other.Knownlist.Remove(session.ObjectId);
            if (other.Session != null)
                watchers.Add(other.Session);
        }

        NoteOlympiadDisconnect(entity);

        if (entity.Inventory != null)
            SaveInventory(session, entity.Inventory);
        SaveWarehouse(session, entity);
        SaveVitals(session, entity);
        SaveQuestProgress(session, entity);
        SavePresence(session, entity.X, entity.Y, entity.Z, touchAccess: false);

        entity.Knownlist.Clear();
        _aoi.Leave(session.ObjectId);
        _byId.Remove(session.ObjectId);
        session.InWorld = false;

        PartyPlayerLeftWorld(session.ObjectId);
        TradePlayerLeftWorld(session.ObjectId);
        PrivateStorePlayerLeftWorld(session.ObjectId);
        ClanPlayerLeftWorld(session.ObjectId, session.Name);

        // The Net despawn for the leaver is owned by NetZoneServer.LeaveNetAsync (it knows the connection's
        // EntityId and unregisters the presence); fanning it here too would double-remove the same entity.
        _ = watchers;
        return true;
    }

    ClassSpawn.Point SpawnFor(ZoneSession session)
    {
        if (_accounts != null)
        {
            var record = _accounts.FindById(session.Account, session.ObjectId);
            if (record is { HasPosition: true })
                return new ClassSpawn.Point(record.X, record.Y, record.Z);
        }

        if (!PlayerClasses.TryGet(session.ClassId, out var template) || template == null || template.Spawns.Count == 0)
            return new ClassSpawn.Point(TalkingIslandSpawn.X, TalkingIslandSpawn.Y, TalkingIslandSpawn.Z);
        var picked = template.Spawns[_spawnRandom.Next(template.Spawns.Count)];
        return new ClassSpawn.Point(picked.X, picked.Y, picked.Z);
    }

    ClassCatalog PlayerClasses => _game.Classes ?? ClassTemplates.Catalog;

    (float run, float walk) MoveSpeed(int classId)
    {
        if (PlayerClasses.TryGet(classId, out var template) && template != null)
            return (template.RunSpeed, template.WalkSpeed);
        return (120f, 80f);
    }

    /// <summary>Mobius <c>getDEX()</c> from the class template — drives the <c>FuncMoveSpeed</c> speed multiplier.</summary>
    int DexOf(int classId) =>
        PlayerClasses.TryGet(classId, out var template) && template != null ? template.Dex : 30;

    const long PresenceSaveIntervalMs = 3000;
    readonly System.Collections.Concurrent.ConcurrentDictionary<int, long> _lastPresenceSaveMs = new();

    /// <summary>Throttled save of the move destination so a Zone restart mid-session keeps the player's spot.</summary>
    void MaybeSavePresenceOnMove(ZoneSession session)
    {
        if (_accounts == null || session.ObjectId <= 0)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        long last = _lastPresenceSaveMs.GetValueOrDefault(session.ObjectId, long.MinValue / 2);
        if (now - last < PresenceSaveIntervalMs)
            return;
        _lastPresenceSaveMs[session.ObjectId] = now;
        SavePresence(session, session.X, session.Y, session.Z, touchAccess: false);
    }

    void SavePresence(ZoneSession session, int x, int y, int z, bool touchAccess)
    {
        if (_accounts == null)
            return;
        _accounts.TrySavePresence(
            session.Account,
            session.ObjectId,
            x,
            y,
            z,
            touchAccess ? DateTimeOffset.UtcNow : null);
    }

    PlayerInventory LoadInventory(ZoneSession session)
    {
        if (_accounts == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);

        var record = _accounts.FindById(session.Account, session.ObjectId);
        if (record == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);
        if (record.Items == null)
        {
            var granted = _game.CreateStarterInventory(session.ClassId, _itemIds);
            _accounts.TryReplaceItems(session.Account, session.ObjectId, CharacterInventory.Rows(granted));
            return granted;
        }

        return CharacterInventory.Restore(_game.Items, _itemIds, record.Items);
    }

    void SaveInventory(ZoneSession session, PlayerInventory inventory)
    {
        // Mobius PlayerInventory.refreshWeight() → _owner.refreshOverloaded(): every inventory write funnels through
        // here, so the overload level is recomputed exactly when the load changes (pickup, drop, shop, trade,
        // warehouse, enchant, equip/unequip, quest reward, sweep, shots).
        RefreshWeightPenaltyAfterInventoryChange(session);
        if (_accounts == null)
            return;
        _accounts.TryReplaceItems(session.Account, session.ObjectId, CharacterInventory.Rows(inventory));
    }

    bool IsLive(ZoneSession session) =>
        session.InWorld &&
        session.ObjectId != 0 &&
        _byId.TryGetValue(session.ObjectId, out var entity) &&
        ReferenceEquals(entity.Session, session);

    List<ZoneEntity> OrderedVisible(int objectId)
    {
        var visible = new List<ZoneEntity>();
        if (!_byId.TryGetValue(objectId, out var self))
            return visible;
        foreach (int id in _aoi.GetVisible(objectId))
        {
            if (_byId.TryGetValue(id, out var entity) && entity.InstanceId == self.InstanceId)
                visible.Add(entity);
        }

        visible.Sort(static (a, b) =>
        {
            int kind = a.Kind.CompareTo(b.Kind);
            return kind != 0 ? kind : a.ObjectId.CompareTo(b.ObjectId);
        });
        return visible;
    }

    List<ZoneEntity> Players()
    {
        var players = new List<ZoneEntity>();
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind == ZoneEntityKind.Player && entity.Session is { InWorld: true })
                players.Add(entity);
        }

        return players;
    }

    static List<int> SortedExcept(HashSet<int> source, HashSet<int> other)
    {
        var ids = new List<int>();
        foreach (int id in source)
        {
            if (!other.Contains(id))
                ids.Add(id);
        }

        ids.Sort();
        return ids;
    }

    static List<int> SortedShared(HashSet<int> source, HashSet<int> other)
    {
        var ids = new List<int>();
        foreach (int id in source)
        {
            if (other.Contains(id))
                ids.Add(id);
        }

        ids.Sort();
        return ids;
    }

    InventoryMessages.UserInfoVitals BuildUserInfoVitals(ZoneEntity entity)
    {
        int level = Math.Max(1, entity.Level);
        int maxCp = 0;
        int str = 0, dex = 0, con = 0, intel = 0, wit = 0, men = 0;
        int pAtk = 0, mAtk = 0, pDef = 0, mDef = 0, pAtkSpd = 0, mAtkSpd = 0, accuracy = 0, crit = 0, evasion = 0;
        if (PlayerClasses.TryGet(entity.ClassId, out var template) && template != null)
        {
            try { maxCp = template.ModifiedCp(level); }
            catch (ArgumentOutOfRangeException) { maxCp = template.MaxCp; }
            str = template.Str;
            dex = template.Dex;
            con = template.Con;
            intel = template.Int;
            wit = template.Wit;
            men = template.Men;
            // Same pipeline combat uses (Mobius stat funcs), so the client status window matches the server.
            var stats = PlayerStats(entity);
            pAtk = stats.PAtk;
            mAtk = stats.MAtk;
            pDef = stats.PDef;
            mDef = stats.MDef;
            pAtkSpd = stats.PAtkSpd;
            mAtkSpd = stats.MAtkSpd;
            accuracy = stats.Accuracy;
            crit = stats.CritRate;
            evasion = stats.Evasion;
        }

        return new InventoryMessages.UserInfoVitals(
            level,
            entity.CurHp,
            entity.MaxHp,
            entity.CurMp,
            entity.MaxMp,
            maxCp,
            maxCp,
            entity.Exp,
            entity.Sp,
            str, dex, con, intel, wit, men,
            pAtk, mAtk, pDef, mDef, pAtkSpd, mAtkSpd,
            accuracy, crit, evasion);
    }

    static NpcInfoMsg NpcInfo(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        NpcId = entity.NpcId,
        Name = entity.Name,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        Heading = entity.Heading,
        Attackable = entity.Attackable,
        RunSpeed = entity.RunSpeed,
        WalkSpeed = entity.WalkSpeed,
        Title = entity.Title,
        NpcType = entity.NpcType
    };

    /// <summary>
    /// Moves a live player to <paramref name="level"/> and runs the same reward-skills refresh a level up does, so a
    /// test can assert the book and the SkillList fanout without grinding exp.
    /// </summary>
    internal bool TrySetPlayerLevelForTests(ZoneSession session, int level)
    {
        ZoneEntity player;
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var entity) || entity.Inventory == null)
                return false;
            entity.Level = Math.Max(1, level);
            RefreshKnownSkills(entity);
            player = entity;
        }

        // The client only learns the new book from a SkillList, exactly as a level up sends it.
        NetBridge?.FanoutSkillList([session.ObjectId],
            Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
        return true;
    }

    internal NpcInfoMsg NpcInfoByTemplateForTests(int npcId)
    {
        lock (_gate)
        {
            ZoneEntity? found = null;
            foreach (var entity in _byId.Values)
            {
                if (entity.Kind != ZoneEntityKind.Npc || entity.NpcId != npcId)
                    continue;
                if (found == null || entity.ObjectId < found.ObjectId)
                    found = entity;
            }

            if (found == null)
                throw new KeyNotFoundException($"NPC template {npcId} is not spawned.");
            return NpcInfo(found);
        }
    }

    internal static SpawnItemMsg SpawnItem(ZoneEntity entity) => new()
    {
        ObjectId = entity.ObjectId,
        ItemId = entity.GroundItemId,
        X = entity.X,
        Y = entity.Y,
        Z = entity.Z,
        Stackable = entity.GroundStackable,
        Count = entity.GroundCount
    };

}
