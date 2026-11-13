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
    readonly ZoneCharacterPersist _persist;
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
    /// <summary>Mobius private minions (data/npc-minions.json).</summary>
    readonly Fineage.Zone.Npcs.NpcMinionCatalog _minions;
    readonly SkillStatEffectCatalog _passives;
    /// <summary>Mobius DefenceTrait percents from skill-passives (jewelry poison/hold/… resists).</summary>
    readonly SkillDefenceTraitCatalog _defenceTraits;
    /// <summary>Mobius DamOverTime / DamOverTimePercent (data/skill-dots.json).</summary>
    readonly SkillDotCatalog _skillDots;
    /// <summary>Mobius skill &lt;effect&gt; rows (data/skill-effects.json) — Cancel, ManaBurn, …</summary>
    readonly SkillEffectCatalog _skillEffects;
    /// <summary>Mobius hennaList dyes (data/henna.json). Deltas fold before the STR/DEX bonus table.</summary>
    readonly HennaCatalog _henna;
    /// <summary>Mobius armorsets (data/armor-sets.json). Complete-set skills fold via GearSkills.</summary>
    readonly ArmorSetCatalog _armorSets;
    /// <summary>Mobius Inventory item_skill on Weapon/Armor (data/item-equip-skills.json).</summary>
    readonly ItemEquipSkillCatalog _equipSkills;
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
    readonly ZoneRates _rates;
    readonly DropRates _dropRates;
    readonly object _gate = new();
    /// <summary>
    /// When true, <see cref="SaveInventory"/> / <see cref="SaveWarehouse"/> refresh under the lock but
    /// queue Postgres until <see cref="FlushDirtyInventories"/> (Claude B8 — keep SQL off <c>_gate</c>).
    /// </summary>
    bool _deferInventoryPersist;
    readonly Dictionary<int, (ZoneSession Session, PlayerInventory Inventory)> _dirtyInventories = [];
    readonly Dictionary<int, ZoneEntity> _dirtyWarehouses = [];
    readonly bool _npcRegen;
    readonly GeoEngine _geo;
    static readonly Lazy<GeoEngine> SharedGeo = new(GeoEngine.LoadDefault);
    static readonly Lazy<DoorTable> SharedDoors = new(DoorTable.LoadDefault);
    static readonly Lazy<WaterZones> SharedWater = new(WaterZones.LoadDefault);
    static readonly Lazy<PeaceZones> SharedPeace = new(PeaceZones.LoadDefault);
    static readonly Lazy<PvpZones> SharedPvp = new(PvpZones.LoadDefault);

    /// <summary>Mobius DoorData (movement collision). Settable for tests.</summary>
    internal DoorTable Doors { get; set; } = DoorTable.Empty;
    /// <summary>Mobius WaterZone cuboids (swim speed). Settable for tests.</summary>
    internal WaterZones Water { get; set; } = WaterZones.Empty;
    /// <summary>Mobius PeaceZone polygons. Settable for tests.</summary>
    internal PeaceZones Peace { get; set; } = PeaceZones.Empty;
    /// <summary>Mobius ArenaZone polygons (<c>ZoneId.PVP</c>). Settable for tests.</summary>
    internal PvpZones Pvp { get; set; } = PvpZones.Empty;

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
        ZoneRates? rates = null,
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
        SkillDefenceTraitCatalog? defenceTraits = null,
        SkillDotCatalog? skillDots = null,
        SkillEffectCatalog? skillEffects = null,
        DeathRuleCatalog? deathRules = null,
        Fineage.Game.Services.DeathDropRules? deathDrop = null,
        Random? deathRandom = null,
        bool? npcRegen = null,
        Fineage.Game.Services.ItemSkillCatalog? itemSkills = null,
        Fineage.Game.Services.MapRegionCatalog? mapRegions = null,
        ZoneGameTime? gameTime = null,
        HennaCatalog? henna = null,
        ArmorSetCatalog? armorSets = null,
        ItemEquipSkillCatalog? equipSkills = null)
    {
        _npcRegen = npcRegen ?? ZoneNpcSettings.NpcRegenEnabled;
        _skillCombat = skillCombat ?? SkillCombatCatalog.LoadDefault();
        _npcAiSupport = npcAiSupport ?? Fineage.Zone.Npcs.NpcAiSupportCatalog.LoadDefault();
        _minions = Fineage.Zone.Npcs.NpcMinionCatalog.LoadDefault();
        _statEffects = statEffects ?? SkillStatEffectCatalog.LoadDefault();
        _passives = passives ?? SkillStatEffectCatalog.LoadPassives();
        _defenceTraits = defenceTraits ?? SkillDefenceTraitCatalog.LoadDefault();
        _skillDots = skillDots ?? SkillDotCatalog.LoadDefault();
        _skillEffects = skillEffects ?? SkillEffectCatalog.LoadDefault();
        _henna = henna ?? HennaCatalog.LoadDefault();
        _armorSets = armorSets ?? ArmorSetCatalog.LoadDefault();
        _equipSkills = equipSkills ?? ItemEquipSkillCatalog.LoadDefault();
        _combatRandom = combatRandom ?? Random.Shared;
        _gameTime = gameTime ?? ZoneGameTime.FromLocalMidnight();
        _aoi = aoi ?? new SingleCellAoi();
        _game = game ?? GameData.LoadDefault();
        _itemIds = itemIds ?? new ItemObjectIdAllocator();
        _spawnRandom = spawnRandom ?? Random.Shared;
        _dropRandom = dropRandom ?? spawnRandom ?? Random.Shared;
        _accounts = accounts;
        _persist = new ZoneCharacterPersist(accounts);
        _expertisePenalty = expertisePenalty ?? ZoneGearSettings.ExpertisePenalty;
        _spawnCatalog = spawnCatalog ?? SpawnCatalog.LoadDefault();
        _dropCatalog = dropCatalog ?? DropCatalog.LoadDefault();
        _rates = rates ?? ZoneRates.FromEnvironment();
        _dropRates = dropRates ?? _rates.AsDropRates();
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
            Peace = SharedPeace.Value;
            Pvp = SharedPvp.Value;
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
    internal IWorldEventPublisher? NetBridge { get; set; }

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

}
