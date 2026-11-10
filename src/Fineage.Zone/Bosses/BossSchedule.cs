namespace Fineage.Zone.Bosses;

public sealed partial class BossSchedule
{
    public const double DefaultRaidMinRespawnMultiplier = 1.0;
    public const double DefaultRaidMaxRespawnMultiplier = 1.0;

    readonly IBossStore _store;
    readonly Func<DateTimeOffset> _clock;
    readonly IBossRandom _random;
    readonly double _raidMinMultiplier;
    readonly double _raidMaxMultiplier;
    readonly object _gate = new();
    readonly Dictionary<int, BossState> _states;

    public BossSchedule(
        BossCatalog catalog,
        IBossStore store,
        Func<DateTimeOffset> clock,
        IBossRandom random,
        double raidMinRespawnMultiplier = DefaultRaidMinRespawnMultiplier,
        double raidMaxRespawnMultiplier = DefaultRaidMaxRespawnMultiplier)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _raidMinMultiplier = RequireMultiplier(raidMinRespawnMultiplier, nameof(raidMinRespawnMultiplier));
        _raidMaxMultiplier = RequireMultiplier(raidMaxRespawnMultiplier, nameof(raidMaxRespawnMultiplier));
        _states = new Dictionary<int, BossState>(catalog.Bosses.Count);
        Restore();
    }

    public BossCatalog Catalog { get; }

    public static BossSchedule LoadDefault(Func<DateTimeOffset> clock, Random? random = null, IBossStore? store = null) =>
        new(
            BossCatalog.LoadDefault(),
            store ?? new MemoryBossStore(),
            clock,
            new InclusiveBossRandom(random ?? Random.Shared));

    /// <summary>
    /// Opens the respawn schedule in <paramref name="stateDirectory"/> unless <c>FINEAGE_BOSS_STORE</c>
    /// names an explicit file.
    /// </summary>
    public static BossSchedule OpenInStateDirectory(string stateDirectory, Func<DateTimeOffset>? clock = null, Random? random = null)
    {
        string? configured = Environment.GetEnvironmentVariable("FINEAGE_BOSS_STORE");
        string path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory, "boss-schedule.json")
            : configured.Trim();
        return new BossSchedule(
            BossCatalog.LoadDefault(),
            new JsonBossStore(path),
            clock ?? (() => DateTimeOffset.UtcNow),
            new InclusiveBossRandom(random ?? Random.Shared),
            ReadMultiplier("FINEAGE_RAID_MIN_RESPAWN_MULTIPLIER", DefaultRaidMinRespawnMultiplier),
            ReadMultiplier("FINEAGE_RAID_MAX_RESPAWN_MULTIPLIER", DefaultRaidMaxRespawnMultiplier));
    }

    /// <summary>
    /// Combat hook. Marks the boss dead, stores max HP/MP for the next life, and rolls the respawn window.
    /// Returns false when <paramref name="npcId"/> is not a scheduled boss.
    /// </summary>
    public bool OnBossKilled(int npcId)
    {
        lock (_gate)
        {
            if (!Catalog.TryGet(npcId, out var definition))
                return false;

            var state = _states[npcId];
            long delay = Roll(definition);
            if (delay < 0)
                delay = 0;
            state.Status = BossLifeStatus.Dead;
            state.CurrentHp = definition.MaxHp;
            state.CurrentMp = definition.MaxMp;
            state.RespawnUnixMs = NowMs() + delay;
            state.Scheduled = IsArmed(definition);
            SaveLocked();
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
            SaveLocked();
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
            SaveLocked();
            return true;
        }
    }

    public IReadOnlyList<BossSpawnOrder> StartupOrders()
    {
        lock (_gate)
            return OrdersWhere(static (definition, state) =>
                definition.Enabled && state.Status is BossLifeStatus.Alive or BossLifeStatus.Fighting);
    }

    /// <summary>
    /// Promotes due dead bosses to alive. Disabled rows change status and stay out of the returned list.
    /// </summary>
    public IReadOnlyList<BossSpawnOrder> CollectDue()
    {
        lock (_gate)
        {
            long now = NowMs();
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
                if (definition.Enabled)
                    spawned.Add(Order(definition, state, ignoreEnabled: false));
            }

            if (changed)
                SaveLocked();
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
