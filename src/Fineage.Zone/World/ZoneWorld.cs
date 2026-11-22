using Fineage.Zone.Geo;
using Fineage.Game;
using Fineage.Game.Economy;
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
using Fineage.Game.Services;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Shops;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Dungeons;
using Fineage.Zone.DimensionalRift;
using Fineage.Zone.Clan;
using Fineage.Zone.Siege;
using Fineage.Zone.Olympiad;
using Fineage.Zone.Party;
using Fineage.Zone.Punishment;
using Fineage.Zone.Fishing;
using Fineage.Zone.Lottery;
using Fineage.Zone.MonsterRace;
using Fineage.Game.Wedding;
using Fineage.Zone.Wedding;
using Fineage.Zone.Simulation;
using Fineage.Zone.CursedWeapons;

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
    readonly IRegionRandom _spawnRandom;
    readonly IRegionRandom _dropRandom;
    readonly IAccountStore? _accounts;
    readonly ZoneCharacterPersist _persist;
    readonly bool _expertisePenalty;
    readonly IRegionRandom _enchantRandom;
    readonly IRegionRandom _respawnRandom;
    readonly EnchantRates _enchantRates;
    readonly QuestMachine _quests;
    readonly QuestJournalGeometry _questGeometry;
    readonly SevenSignsActor _sevenSigns;
    readonly SevenSignsFestivalCatalog _festivalCatalog;
    readonly SevenSignsFestivalSpawnCatalog _festivalSpawns;
    readonly SevenSignsFestivalRuntime _festivalRuntime;
    readonly SevenSignsFestivalActor _festivalActor;
    int _nextFestivalInstanceId;
    readonly DungeonCatalog _dungeons;
    readonly DungeonAccess _dungeonAccess;
    readonly DimensionalRiftCatalog _dimensionalRift;
    readonly DimensionalRiftSettings _dimensionalRiftSettings;
    readonly DimensionalRiftActor _dimensionalRiftActor;
    readonly Random _riftRandom;
    readonly SiegeSettings _siegeSettings;
    readonly SiegeActor _siege;
    readonly ChSiegeActor _chSiege;
    readonly NpcHtmlCatalog _html;
    readonly Fineage.Game.L10n.LocalizationCatalog _l10n;
    readonly LocalizedHtml _pages;
    readonly BypassRegistry _bypasses;
    readonly OlympiadActor _olympiad;
    readonly TeleportCatalog _teleports;
    readonly GmTeleportCatalog _gmTeleports;
    readonly NewbieBuffCatalog _newbieBuffs;
    readonly ItemWeights _weights;
    /// <summary>Mobius <c>handler="ItemSkills"</c> items: what a potion or scroll actually does (data/item-skills.json).</summary>
    readonly Fineage.Game.Services.ItemSkillCatalog _itemSkills;
    /// <summary>Mobius <c>MapRegionManager</c>: the "nearest town" a Scroll of Escape lands in.</summary>
    readonly Fineage.Game.Services.MapRegionCatalog _mapRegions;
    /// <summary>Mobius clan hall owner restart points (<c>data/clan-hall-spawns.json</c>).</summary>
    readonly Fineage.Game.Services.ClanHallSpawnCatalog _clanHallSpawns;
    readonly int _teleportOffset;
    readonly IRegionRandom _serviceRandom;
    /// <summary>Hit/miss, crit and damage spread rolls (Mobius Rnd in Formulas).</summary>
    readonly IRegionRandom _combatRandom;
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
    /// <summary>Mobius augmentation options/chances (data/augmentation/*.json).</summary>
    readonly Augmentation.AugmentationCatalog _augmentation;
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
    readonly IRegionRandom _deathRandom;
    /// <summary>Every entity in the world by object id, each owned by one region (<see cref="RegionDirectory{T}"/>).</summary>
    RegionDirectory<ZoneEntity> _byId;
    readonly DropCatalog _dropCatalog;
    readonly EventDropCatalog _eventDropCatalog;
    readonly ZoneRates _rates;
    readonly DropRates _dropRates;
    readonly WorldAffinity _affinity = new();
    readonly SimulationSyncIoGuard _syncIo;
    readonly CharacterPersistWorker _snapshots;
    /// <summary>
    /// Nesting depth of <see cref="WithDeferredInventoryPersist{T}"/>. While positive, <see cref="SaveInventory"/> /
    /// <see cref="SaveWarehouse"/> refresh the snapshot but queue Postgres until the outermost call runs
    /// <see cref="FlushDirtyInventories"/>, once per step instead of once per change.
    /// </summary>
    int _deferInventoryPersistDepth;

    bool InventoryPersistDeferred => _deferInventoryPersistDepth > 0;
    readonly Dictionary<int, (ZoneSession Session, PlayerInventory Inventory)> _dirtyInventories = [];
    readonly Dictionary<int, ZoneEntity> _dirtyWarehouses = [];
    readonly Dictionary<int, ZoneEntity> _dirtyFreight = [];
    readonly bool _npcRegen;
    readonly GeoEngine _geo;
    static readonly Lazy<GeoEngine> SharedGeo = new(GeoEngine.LoadDefault);
    static readonly Lazy<DoorTable> SharedDoors = new(DoorTable.LoadDefault);
    static readonly Lazy<WaterZones> SharedWater = new(WaterZones.LoadDefault);
    static readonly Lazy<PeaceZones> SharedPeace = new(PeaceZones.LoadDefault);
    static readonly Lazy<PvpZones> SharedPvp = new(PvpZones.LoadDefault);
    static readonly Lazy<SiegeZones> SharedSiegeZones = new(SiegeZones.LoadDefault);

    /// <summary>Mobius DoorData (movement collision). Settable for tests.</summary>
    internal DoorTable Doors { get; set; } = DoorTable.Empty;
    /// <summary>Mobius WaterZone cuboids (swim speed). Settable for tests.</summary>
    internal WaterZones Water { get; set; } = WaterZones.Empty;
    /// <summary>Mobius PeaceZone polygons. Settable for tests.</summary>
    internal PeaceZones Peace { get; set; } = PeaceZones.Empty;
    /// <summary>Mobius ArenaZone polygons (<c>ZoneId.PVP</c>). Settable for tests.</summary>
    internal PvpZones Pvp { get; set; } = PvpZones.Empty;
    internal SiegeZones SiegeAreas { get; set; } = SiegeZones.Empty;

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
        EventDropCatalog? eventDropCatalog = null,
        ZoneRates? rates = null,
        DropRates? dropRates = null,
        string? activeMapId = null,
        Func<DateTimeOffset>? clock = null,
        Random? enchantRandom = null,
        EnchantRates? enchantRates = null,
        SevenSignsEngine? sevenSigns = null,
        SevenSignsFestivalRuntime? festivalRuntime = null,
        bool includeEnabledMaps = false,
        bool allowAmbientBoats = true,
        DungeonCatalog? dungeons = null,
        DungeonAccessOptions? dungeonAccess = null,
        DimensionalRiftCatalog? dimensionalRift = null,
        DimensionalRiftSettings? dimensionalRiftSettings = null,
        SiegeSettings? siege = null,
        BossSchedule? bosses = null,
        Random? bossRandom = null,
        NpcHtmlCatalog? html = null,
        BypassRegistry? bypasses = null,
        OlympiadBoard? olympiad = null,
        OlympiadSettings? olympiadSettings = null,
        TeleportCatalog? teleports = null,
        GmTeleportCatalog? gmTeleports = null,
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
        Fineage.Game.Services.ClanHallSpawnCatalog? clanHallSpawns = null,
        ZoneGameTime? gameTime = null,
        HennaCatalog? henna = null,
        Augmentation.AugmentationCatalog? augmentation = null,
        ArmorSetCatalog? armorSets = null,
        ItemEquipSkillCatalog? equipSkills = null,
        Fineage.Zone.Manor.ManorSettings? manor = null,
        Random? manorRandom = null,
        Random? fishingRandom = null,
        IRegionPartition? regions = null,
        ISimulationObserver? simulationObserver = null,
        ulong? simulationSeed = null,
        IRegionExecutor? regionExecutor = null,
        ILedgerJournal? ledgerJournal = null,
        PunishmentManager? punishments = null,
        Fineage.Game.L10n.LocalizationCatalog? localization = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _simulationSeed = simulationSeed ?? ZoneSimulationSettings.WorldSeed();
        InitSimulation(regions ?? GridRegionPartition.MapTiles, simulationObserver ?? new ZoneSimulationObserver(),
            regionExecutor ?? ZoneSimulationSettings.RegionExecutor(ZoneSimulationSettings.RegionWorkers));
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
        _augmentation = augmentation ?? Augmentation.AugmentationCatalog.LoadDefault();
        Augmentation.AugmentationRules.BindCatalog(_augmentation);
        _armorSets = armorSets ?? ArmorSetCatalog.LoadDefault();
        _equipSkills = equipSkills ?? ItemEquipSkillCatalog.LoadDefault();
        _combatRandom = Streams(combatRandom, RandomPurpose.Combat);
        _gameTime = gameTime ?? ZoneGameTime.FromLocalMidnight(WallNow());
        _aoi = new RelocationTrackingAoi(aoi ?? new SingleCellAoi(), _byId.Relocated);
        _game = game ?? GameData.LoadDefault();
        _spawnRandom = Streams(spawnRandom, RandomPurpose.Spawn);
        _dropRandom = Streams(dropRandom, RandomPurpose.Drop);
        _syncIo = new SimulationSyncIoGuard(_affinity);
        _accounts = accounts == null ? null : SimulationGuardedAccountStore.Wrap(accounts, _syncIo);
        _snapshots = new CharacterPersistWorker(_accounts);
        _persist = new ZoneCharacterPersist(_accounts, _snapshots);
        _expertisePenalty = expertisePenalty ?? ZoneGearSettings.ExpertisePenalty;
        _spawnCatalog = spawnCatalog ?? SpawnCatalog.LoadDefault();
        _dropCatalog = dropCatalog ?? DropCatalog.LoadDefault();
        _eventDropCatalog = eventDropCatalog ?? EventDropCatalog.LoadDefault();
        _rates = rates ?? ZoneRates.FromEnvironment();
        _dropRates = dropRates ?? _rates.AsDropRates();
        _activeMapId = string.IsNullOrWhiteSpace(activeMapId) ? SpawnCatalog.DefaultMapId : activeMapId.Trim();
        _enchantRates = enchantRates ?? EnchantRates.Default;
        _enchantRates.Validate();
        _includeEnabledMaps = includeEnabledMaps;
        _allowAmbientBoats = allowAmbientBoats;
        _quests = new QuestMachine(_game.Quests, _game.Items, random: () => throw new InvalidOperationException(
            "quest roll without the acting character's region stream: build the QuestActor with QuestActorOf"));
        _questGeometry = QuestJournalGeometry.LoadDefault();
        _festivalCatalog = festivalRuntime?.Board.Catalog ?? SevenSignsFestivalCatalog.LoadDefault();
        _festivalSpawns = SevenSignsFestivalSpawnCatalog.LoadDefault();
        _festivalRuntime = festivalRuntime ?? new SevenSignsFestivalRuntime(_festivalCatalog);
        SevenSignsEngine ssEngine = sevenSigns ?? new SevenSignsEngine(WallNow(), festival: _festivalRuntime);
        _festivalRuntime.BindEngine(ssEngine);
        _sevenSigns = new SevenSignsActor(ssEngine);
        _festivalActor = new SevenSignsFestivalActor(_festivalRuntime.Board);
        _nextFestivalInstanceId = FestivalInstanceBase;
        _dungeons = dungeons ?? DungeonCatalog.LoadDefault();
        _dungeonAccess = new DungeonAccess(_dungeons, dungeonAccess ?? DungeonAccessOptions.FromEnvironment());
        _dimensionalRiftSettings = dimensionalRiftSettings ?? DimensionalRiftSettings.Default;
        _dimensionalRift = dimensionalRift ?? DimensionalRiftCatalog.LoadDefault();
        _riftRandom = GlobalStream(RandomPurpose.Service);
        _dimensionalRiftActor = new DimensionalRiftActor(
            new DimensionalRiftBoard(_dimensionalRift),
            _dimensionalRiftSettings,
            _riftRandom);
        _siegeSettings = siege ?? SiegeSettings.FromEnvironment();
        _siege = new SiegeActor(new SiegeBoard(NowUnixMs(), _siegeSettings));
        _chSiege = new ChSiegeActor(new ChSiegeBoard(SiegableHallCatalog.Default.All), ChSiegeSettings.Default);
        var bossPolicy = new MapFilteredBossSpawnPolicy(includeEnabledMaps);
        _bosses = new BossActor(
            bosses ?? BossSchedule.LoadDefault(WallNow().ToUnixTimeMilliseconds(), spawnPolicy: bossPolicy),
            bossRandom ?? GlobalStream(RandomPurpose.Boss));
        IReadOnlyList<Fineage.Accounts.CursedWeaponRecord>? curseRows = null;
        try
        {
            curseRows = accounts?.ListCursedWeapons();
        }
        catch (Exception ex)
        {
            ZoneLog.Warn($"cursed weapons restore skipped: {ex.Message}");
        }

        _cursedWeapons = new CursedWeaponsManager(CursedWeaponsCatalog.LoadDefault(), curseRows);
        _html = html ?? NpcHtmlCatalog.LoadDefault();
        _l10n = localization ?? LoadLocalization();
        _pages = new LocalizedHtml(_l10n, _html);
        _bypasses = bypasses ?? BypassRegistry.CreateDefault(_pages);
        _party = new PartyActor(new PartyMembers(this));
        var resolvedOlympiadSettings = olympiadSettings ?? OlympiadSettings.FromEnvironment();
        _olympiadEnchantLimit = resolvedOlympiadSettings.EnchantLimit;
        _olympiadDisableBlessedSpiritshots = resolvedOlympiadSettings.DisableBlessedSpiritshots;
        _olympiadRestrictedItems = resolvedOlympiadSettings.RestrictedItems;
        _olympiad = new OlympiadActor(olympiad ?? new OlympiadBoard(WallNow(), resolvedOlympiadSettings),
            LookupOlympiad, GlobalStream(RandomPurpose.Olympiad));
        _teleports = teleports ?? TeleportCatalog.LoadDefault();
        _gmTeleports = gmTeleports ?? GmTeleportCatalog.LoadDefault();
        _newbieBuffs = newbieBuffs ?? NewbieBuffCatalog.LoadDefault();
        _weights = weights ?? ItemWeights.LoadDefault();
        _teleportOffset = teleportOffset ?? ReadTeleportOffset();
        _serviceRandom = Streams(serviceRandom, RandomPurpose.Service);
        _buyLists = buyLists ?? BuyListCatalog.LoadDefault();
        _multisells = HallItemCreateCatalog.MergeInto(
            multisells ?? MultiSellCatalog.LoadDefault(),
            HallItemCreateCatalog.LoadDefault());
        _shopStock = new ShopStock(_buyLists);
        ILedgerJournal journal = ledgerJournal
            ?? (accounts != null
                ? throw new InvalidOperationException(
                    "ILedgerJournal is required when IAccountStore is configured. Production hosts pass " +
                    "accounts.OpenLedgerJournal(); tests must pass new MemoryLedgerJournal().")
                : new MemoryLedgerJournal());
        _ledger = new EconomyLedger(journal, LedgerIdempotencyMilliseconds / _simulationClock.StepMilliseconds);
        foreach (var entry in journal.ReadAll())
            _ledgerIds.Adopt(entry.Transaction.Id);
        ExpireOrphanGroundJournalAccounts();
        _itemIds = SeedItemObjectIds(accounts, itemIds, _ledger);
        _deathRules = deathRules ?? DeathRuleCatalog.LoadDefault();
        _deathDrop = deathDrop ?? Fineage.Game.Services.DeathDropRules.LoadDefault();
        _deathRandom = Streams(deathRandom, RandomPurpose.Death);
        _questRandom = Streams(null, RandomPurpose.Quest);
        _enchantRandom = Streams(enchantRandom, RandomPurpose.Enchant);
        _respawnRandom = Streams(null, RandomPurpose.Respawn);
        SoulCrystalRandom = Streams(null, RandomPurpose.SoulCrystal);
        RequireRegionStreamsForExecutor();
        _itemSkills = itemSkills ?? Fineage.Game.Services.ItemSkillCatalog.LoadDefault();
        _mapRegions = mapRegions ?? Fineage.Game.Services.MapRegionCatalog.LoadDefault();
        _clanHallSpawns = clanHallSpawns ?? Fineage.Game.Services.ClanHallSpawnCatalog.LoadDefault();
        _geo = geo ?? SharedGeo.Value;
        if (geo == null)
        {
            // Production world: real datapack doors / water. Tests that inject geo keep the empty tables.
            Doors = SharedDoors.Value;
            Water = SharedWater.Value;
            Peace = SharedPeace.Value;
            Pvp = SharedPvp.Value;
            SiegeAreas = SharedSiegeZones.Value;
            Swamp = SharedSwamp.Value;
            Damage = SharedDamage.Value;
            NoLanding = SharedNoLanding.Value;
            MotherTree = SharedMotherTree.Value;
        }
        BootstrapSpawns();
        BootstrapBoats();
        BootstrapBosses();
        BootstrapQuestNpcs();
        LoadClans();
        LoadSiege();
        LoadChSiege();
        SyncClanHallOwnership();
        InitManor(manor, manorRandom);
        _fishingRandom = fishingRandom ?? GlobalStream(RandomPurpose.Fishing);
        _lottery = new LotteryActor(new LotteryBoard(WallNow().ToUnixTimeMilliseconds()), GlobalStream(RandomPurpose.Lottery));
        _fishingChampionship = new FishingChampionshipActor(new FishingChampionshipBoard(WallNow().ToUnixTimeMilliseconds()));
        _monsterRaceSettings = MonsterRaceSettings.Mobius;
        _monsterRaceBroadcast = _monsterRaceSettings.ResolveBroadcastZones(Peace);
        _monsterRace = new MonsterRaceActor(
            new MonsterRaceBoard(
                WallNow().ToUnixTimeMilliseconds(),
                _game.Items,
                Npcs.NpcCatalog.LoadDefault(),
                () => _itemIds.Next(),
                _monsterRaceSettings),
            GlobalStream(RandomPurpose.MonsterRace));
        _mammon = new MammonActor(_sevenSigns, GlobalStream(RandomPurpose.Mammon));
        _weddingSettings = WeddingSettings.LoadDefault();
        _wedding = new WeddingActor(accounts?.ListWeddingCouples(), accounts?.NextWeddingCoupleId ?? 1);
        SpawnCastleDoors();
        InitPunishment(punishments);
    }

    /// <summary>Bypass handlers. A later <see cref="BypassRegistry.Register"/> replaces a stub.</summary>
    public BypassRegistry Bypasses => _bypasses;

    /// <summary>Castle row for shop tax. Tests set <see cref="Siege.CastleSiege.OwnerClanId"/> and tax on the result.</summary>
    internal CastleSiege? FindCastle(int castleId) => _siege.Board.Find(castleId);

    public IReadOnlyList<int> NpcObjectIds
    {
        get
        {
            using (_affinity.Step())
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

    int _onlinePlayers;

    /// <summary>
    /// Players with an active zone session in the world (for OTEL <c>fineage.zone.online</c>), counted at each enter
    /// and leave; any thread may read it.
    /// </summary>
    public int OnlinePlayerCount => Volatile.Read(ref _onlinePlayers);

    void CountOnlinePlayers()
    {
        Volatile.Write(ref _onlinePlayers, Players().Count);
    }

    /// <summary>Live player entity after a successful enter (same object Net enter projects from).</summary>
    internal bool TryGetLivePlayer(ZoneSession session, out ZoneEntity entity)
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

    /// <summary>
    /// Optional Net PacketEnvelope bridge for sim/AI fanout (MoveState) while V2 flushes stay on ZoneSession streams.
    /// Set by the Net host when it owns the zone session. Reads return the <see cref="OutboxingWorldEventPublisher"/>
    /// over it, so simulation steps batch their fan-out (zone-simulation.md §3.6).
    /// </summary>
    internal IWorldEventPublisher? NetBridge
    {
        get => _netBridge;
        set => _netBridge = value == null ? null : new OutboxingWorldEventPublisher(value, _fanout);
    }

    IWorldEventPublisher? _netBridge;
    readonly WorldFanoutSequencer _fanout = new();

    /// <summary>
    /// Live players currently on <paramref name="session"/>'s knownlist (post-enter / post-move hysteresis).
    /// Used by Net presence fanout so peers match V2 CharInfo/MoveToAck recipients.
    /// </summary>
    internal List<ZoneEntity> SnapshotKnownPlayers(ZoneSession session)
    {
        if (!TryGetLivePlayer(session, out var self))
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
