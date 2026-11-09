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
        _persist = new ZoneCharacterPersist(accounts);
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

    public async Task<bool> TryEnterAsync(ZoneSession session, CancellationToken ct)
    {
        // Load durable character state off the world lock so a slow Postgres round-trip
        // cannot stall move/combat handlers waiting on _gate.
        CharacterRecord? stored = null;
        if (_accounts != null && session.ObjectId > 0)
        {
            stored = await Task.Run(
                () => _accounts.FindById(session.Account, session.ObjectId),
                ct).ConfigureAwait(false);
        }

        // Starter-kit backfill is durable IO — keep it off the lock too.
        if (stored is { Items: null } && _accounts != null)
        {
            var granted = _game.CreateStarterInventory(session.ClassId, _itemIds);
            await Task.Run(
                () => _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, granted),
                ct).ConfigureAwait(false);
            stored = stored with { Items = CharacterInventory.Rows(granted) };
        }

        bool ok;
        int known;
        int enterX = 0, enterY = 0, enterZ = 0;
        List<UnlinkPersistSnapshot>? pendingUnlink = null;
        lock (_gate)
            ok = TryEnter(session, stored, out known, out enterX, out enterY, out enterZ, out pendingUnlink);
        if (!ok)
            return false;

        if (pendingUnlink is { Count: > 0 })
        {
            foreach (var snapshot in pendingUnlink)
                PersistUnlinkSnapshot(snapshot);
        }

        // Presence / last_access after the world commit so DB latency is not under _gate.
        SavePresence(session, enterX, enterY, enterZ, touchAccess: true);
        ZoneLog.EnterWorld(session, known);
        return true;
    }

    public Task LeaveAsync(ZoneSession session, CancellationToken ct)
    {
        UnlinkPersistSnapshot? snapshot = null;
        lock (_gate)
            TryDetachPlayer(session, capturePersist: true, out _, out snapshot);
        if (snapshot == null)
            return Task.CompletedTask;

        PersistUnlinkSnapshot(snapshot.Value);

        _sevenSigns.Save();
        _bosses.Save();
        ZoneLog.LeaveWorld(session);
        return Task.CompletedTask;
    }

    public Task<string?> TryMoveAsync(ZoneSession session, int destX, int destY, int destZ, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = TryMove(session, destX, destY, destZ);
        if (error != null)
            return Task.FromResult<string?>(error);

        MaybeSavePresenceOnMove(session);
        return Task.FromResult<string?>(null);
    }

    bool TryEnter(
        ZoneSession session,
        CharacterRecord? stored,
        out int known,
        out int enterX,
        out int enterY,
        out int enterZ,
        out List<UnlinkPersistSnapshot>? pendingUnlink)
    {
        known = 0;
        enterX = enterY = enterZ = 0;
        pendingUnlink = null;
        if (session.ObjectId <= 0)
            return false;
        if (_byId.TryGetValue(session.ObjectId, out var occupied) && occupied.Kind != ZoneEntityKind.Player)
            return false;

        // Same-session re-enter: detach without flushing (avoid double write), then rebuild
        // from the preloaded CharacterRecord so durable edits while InWorld (death penalty,
        // etc.) still apply — Reattach alone would keep a stale in-memory snapshot.
        if (session.InWorld)
        {
            if (!TryDetachPlayer(session, capturePersist: false, out _, out _))
                return false;
        }
        else if (_byId.TryGetValue(session.ObjectId, out var current) &&
                 current.Session != null &&
                 !ReferenceEquals(current.Session, session) &&
                 TryDetachPlayer(current.Session, capturePersist: true, out _, out var evicted))
        {
            pendingUnlink = [evicted!.Value];
        }

        var spawn = SpawnFor(session, stored);
        var inventory = LoadInventory(session, stored);
        var vitals = LoadVitals(session, stored);
        var warehouse = new PrivateWarehouse(_game.Items);
        if (stored?.Warehouse != null)
            warehouse.Restore(stored.Warehouse.Select(item => new WarehouseRow(item.ItemId, item.Count, item.EnchantLevel, item.ObjectId)));
        RememberPenalties(session, stored);
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
        LoadQuestProgress(session, entity, stored);
        LoadLearnedSkills(entity, stored);
        LoadKnownRecipes(entity, stored);
        LoadSocialLists(entity, stored);
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
        enterX = entity.X;
        enterY = entity.Y;
        enterZ = entity.Z;

        // The enter sequence itself (UserInfo, ItemList, SkillList, spawns) is Net-only and lives in
        // NetZoneServer.OnEnterAsync; the knownlist is still built here because the sim owns it.
        foreach (var other in OrderedVisible(entity.ObjectId))
        {
            entity.Knownlist.Add(other.ObjectId);
            if (other.Session != null)
                other.Knownlist.Add(entity.ObjectId);
        }

        AnnounceClanEnter(entity);
        FriendPlayerEnteredWorld(entity);
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

    string? TryMove(ZoneSession session, int destX, int destY, int destZ)
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

        // Origin/ObjectId filled here so Net and tests only pass destination; MoveToMsg remains the internal/ack shape.
        var move = new MoveToMsg
        {
            ObjectId = session.ObjectId,
            DestX = destX,
            DestY = destY,
            DestZ = destZ,
            OriginX = session.X,
            OriginY = session.Y,
            OriginZ = session.Z
        };

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

    /// <returns>False when nothing was detached (stale session / object id).</returns>
    bool TryDetachPlayer(
        ZoneSession session,
        bool capturePersist,
        out ZoneEntity? detached,
        out UnlinkPersistSnapshot? snapshot)
    {
        detached = null;
        snapshot = null;
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

        foreach (int id in entity.Knownlist)
        {
            if (!_byId.TryGetValue(id, out var other))
                continue;
            other.Knownlist.Remove(session.ObjectId);
        }

        NoteOlympiadDisconnect(entity);

        entity.Knownlist.Clear();
        _aoi.Leave(session.ObjectId);
        _byId.Remove(session.ObjectId);
        ForgetActiveSets(session.ObjectId);
        session.InWorld = false;

        PartyPlayerLeftWorld(session.ObjectId);
        TradePlayerLeftWorld(session.ObjectId);
        PrivateStorePlayerLeftWorld(session.ObjectId);
        FriendPlayerLeftWorld(session.ObjectId);
        ClanPlayerLeftWorld(session.ObjectId, session.Name);

        detached = entity;
        if (capturePersist)
            snapshot = new UnlinkPersistSnapshot(session, entity);

        // The Net despawn for the leaver is owned by NetZoneServer.LeaveNetAsync (it knows the connection's
        // EntityId and unregisters the presence); fanning it here too would double-remove the same entity.
        return true;
    }

    /// <summary>Inventory / warehouse / vitals / quests / presence — must run outside <c>_gate</c>.</summary>
    void PersistUnlinkSnapshot(UnlinkPersistSnapshot snapshot)
    {
        var session = snapshot.Session;
        var entity = snapshot.Entity;
        if (entity.Inventory != null)
            _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, entity.Inventory);
        SaveWarehouse(session, entity);
        SaveVitals(session, entity);
        SaveQuestProgress(session, entity);
        SavePresence(session, entity.X, entity.Y, entity.Z, touchAccess: false);
    }

    ClassSpawn.Point SpawnFor(ZoneSession session, CharacterRecord? stored = null)
    {
        if (stored is { HasPosition: true })
            return new ClassSpawn.Point(stored.X, stored.Y, stored.Z);

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
        if (!_persist.Enabled || session.ObjectId <= 0)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        long last = _lastPresenceSaveMs.GetValueOrDefault(session.ObjectId, long.MinValue / 2);
        if (now - last < PresenceSaveIntervalMs)
            return;
        _lastPresenceSaveMs[session.ObjectId] = now;
        SavePresence(session, session.X, session.Y, session.Z, touchAccess: false);
    }

    void SavePresence(ZoneSession session, int x, int y, int z, bool touchAccess) =>
        _persist.SavePresence(
            session.Account,
            session.ObjectId,
            x,
            y,
            z,
            touchAccess ? DateTimeOffset.UtcNow : null);

    PlayerInventory LoadInventory(ZoneSession session, CharacterRecord? stored = null)
    {
        if (stored == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);
        // Items == null was backfilled off-lock in TryEnterAsync; treat missing as starter in-memory only.
        if (stored.Items == null)
            return _game.CreateStarterInventory(session.ClassId, _itemIds);

        return CharacterInventory.Restore(_game.Items, _itemIds, stored.Items);
    }

    void SaveInventory(ZoneSession session, PlayerInventory inventory)
    {
        // Mobius PlayerInventory.refreshWeight() → _owner.refreshOverloaded(): every inventory write funnels through
        // here, so the overload level is recomputed exactly when the load changes (pickup, drop, shop, trade,
        // warehouse, enchant, equip/unequip, quest reward, sweep, shots).
        RefreshWeightPenaltyAfterInventoryChange(session);
        _persist.ReplaceItemsFromInventory(session.Account, session.ObjectId, inventory);
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
