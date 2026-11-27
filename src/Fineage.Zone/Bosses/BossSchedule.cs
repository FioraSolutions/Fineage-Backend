using Fineage.Zone.Persistence;

namespace Fineage.Zone.Bosses;

public sealed partial class BossSchedule
{
    public const double DefaultRaidMinRespawnMultiplier = 1.0;
    public const double DefaultRaidMaxRespawnMultiplier = 1.0;

    readonly IBossStore _store;
    readonly IBossSpawnPolicy _spawnPolicy;
    readonly double _raidMinMultiplier;
    readonly double _raidMaxMultiplier;
    readonly object _gate = new();
    readonly Dictionary<int, BossState> _states;

    /// <summary>
    /// Restores the saved schedule as of <paramref name="loadedAtUnixMs"/>, the process start: a corpse whose time has
    /// passed is alive again. The schedule holds no clock and no generator; <see cref="BossActor"/> passes the step's
    /// time and its own stream.
    /// </summary>
    public BossSchedule(
        BossCatalog catalog,
        IBossStore store,
        long loadedAtUnixMs,
        double raidMinRespawnMultiplier = DefaultRaidMinRespawnMultiplier,
        double raidMaxRespawnMultiplier = DefaultRaidMaxRespawnMultiplier,
        IBossSpawnPolicy? spawnPolicy = null)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _spawnPolicy = spawnPolicy ?? DatapackBossSpawnPolicy.Instance;
        _raidMinMultiplier = RequireMultiplier(raidMinRespawnMultiplier, nameof(raidMinRespawnMultiplier));
        _raidMaxMultiplier = RequireMultiplier(raidMaxRespawnMultiplier, nameof(raidMaxRespawnMultiplier));
        _states = new Dictionary<int, BossState>(catalog.Bosses.Count);
        Restore(loadedAtUnixMs);
    }

    public BossCatalog Catalog { get; }

    public static BossSchedule LoadDefault(
        long loadedAtUnixMs,
        IBossStore? store = null,
        IBossSpawnPolicy? spawnPolicy = null) =>
        new(BossCatalog.LoadDefault(), store ?? new MemoryBossStore(), loadedAtUnixMs, spawnPolicy: spawnPolicy);

    /// <summary>Opens the respawn schedule saved in the database (#996).</summary>
    public static BossSchedule Open(WorldStateStore state, DateTimeOffset loadedAt, IBossSpawnPolicy? spawnPolicy = null)
    {
        return new BossSchedule(
            BossCatalog.LoadDefault(),
            new StoredBossStore(state),
            loadedAt.ToUnixTimeMilliseconds(),
            ReadMultiplier("FINEAGE_RAID_MIN_RESPAWN_MULTIPLIER", DefaultRaidMinRespawnMultiplier),
            ReadMultiplier("FINEAGE_RAID_MAX_RESPAWN_MULTIPLIER", DefaultRaidMaxRespawnMultiplier),
            spawnPolicy);
    }

    public bool ShouldSpawn(BossDefinition definition) => _spawnPolicy.ShouldSpawn(definition);

    /// <summary>
    /// Marks the boss dead at <paramref name="killedAtUnixMs"/>, stores max HP/MP for the next life, and rolls the
    /// respawn window on <paramref name="random"/>. Returns false when <paramref name="npcId"/> is not a scheduled boss.
    /// </summary>
    public bool OnBossKilled(int npcId, long killedAtUnixMs, IBossRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        lock (_gate)
        {
            if (!Catalog.TryGet(npcId, out var definition))
                return false;

            var state = _states[npcId];
            long delay = Roll(definition, random);
            if (delay < 0)
                delay = 0;
            state.Status = BossLifeStatus.Dead;
            state.CurrentHp = definition.MaxHp;
            state.CurrentMp = definition.MaxMp;
            state.RespawnUnixMs = killedAtUnixMs + delay;
            state.Scheduled = IsArmed(definition);
            PersistSnapshot();
            return true;
        }
    }

    /// <summary>Clears the respawn time and returns a spawn order, including bosses whose datapack row is disabled.</summary>
    public bool TryForceSpawn(int npcId, out BossSpawnOrder order)
    {
        lock (_gate)
        {
            if (!Catalog.TryGet(npcId, out var definition))
            {
                order = default;
                return false;
            }

            var state = _states[npcId];
            state.Status = BossLifeStatus.Alive;
            state.RespawnUnixMs = 0;
            state.Scheduled = false;
            NormalizeVitals(state, definition);
            PersistSnapshot();
            order = Order(definition, state, ignoreEnabled: true);
            return true;
        }
    }

    public bool TryNoteVitals(int npcId, int currentHp, int currentMp)
    {
        lock (_gate)
        {
            if (!Catalog.TryGet(npcId, out var definition))
                return false;
            var state = _states[npcId];
            if (state.Status == BossLifeStatus.Dead)
                return false;

            state.CurrentHp = ClampVital(currentHp, definition.MaxHp);
            state.CurrentMp = ClampVital(currentMp, definition.MaxMp);
            PersistSnapshot();
            return true;
        }
    }

    public IReadOnlyList<BossSpawnOrder> StartupOrders()
    {
        lock (_gate)
            return OrdersWhere((definition, state) =>
                ShouldSpawn(definition) && state.Status is BossLifeStatus.Alive or BossLifeStatus.Fighting);
    }

    /// <summary>
    /// Promotes due dead bosses to alive. Disabled rows change status and stay out of the returned list.
    /// </summary>
    public IReadOnlyList<BossSpawnOrder> CollectDue(long nowUnixMs)
    {
        lock (_gate)
        {
            long now = nowUnixMs;
            var spawned = new List<BossSpawnOrder>();
            bool changed = false;
            foreach (var definition in Catalog.Bosses)
            {
                var state = _states[definition.NpcId];
                if (state.Status != BossLifeStatus.Dead || !state.Scheduled)
                    continue;
                if (now < state.RespawnUnixMs)
                    continue;

                state.Status = BossLifeStatus.Alive;
                state.RespawnUnixMs = 0;
                state.Scheduled = false;
                NormalizeVitals(state, definition);
                changed = true;
                if (ShouldSpawn(definition))
                    spawned.Add(Order(definition, state, ignoreEnabled: false));
            }

            if (changed)
                PersistSnapshot();
            return spawned;
        }
    }

    public bool TryView(int npcId, out BossView view)
    {
        lock (_gate)
        {
            if (!Catalog.TryGet(npcId, out var definition))
            {
                view = default;
                return false;
            }

            view = View(definition, _states[npcId]);
            return true;
        }
    }

    public IReadOnlyList<BossView> Views()
    {
        lock (_gate)
        {
            var views = new List<BossView>(Catalog.Bosses.Count);
            foreach (var definition in Catalog.Bosses)
                views.Add(View(definition, _states[definition.NpcId]));
            return views;
        }
    }
}
