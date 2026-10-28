namespace Fineage.Zone.Bosses;

/// <summary>One boss the world should materialize. <see cref="IgnoreEnabled"/> is the admin force-spawn path.</summary>
public readonly record struct BossSpawnOrder(
    int NpcId,
    int ObjectId,
    int CurrentHp,
    int CurrentMp,
    bool IgnoreEnabled);

/// <summary>Schedule view. <c>Spawned</c> is filled by the world, not the store.</summary>
public readonly record struct BossView(
    int NpcId,
    int ObjectId,
    BossKind Kind,
    BossLifeStatus Status,
    long RespawnUnixMs,
    int CurrentHp,
    int MaxHp,
    int CurrentMp,
    int MaxMp,
    bool Enabled);

/// <summary>
/// Raid and grand boss respawn schedule.
/// Raid windows follow Mobius <c>RaidBossSpawnManager.updateStatus</c>
/// (<c>Rnd.get(respawnMinDelay, respawnMaxDelay)</c> after the NPC.ini multipliers).
/// Grand windows follow the boss scripts
/// (<c>interval + getRandom(-random, random)</c> hours from GrandBoss.ini).
/// A zero raid delay is not armed; the next process start spawns the boss once the saved time is in the past.
/// </summary>
public sealed class BossSchedule
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

    public static BossSchedule OpenBesideAccounts(string accountStorePath, Func<DateTimeOffset>? clock = null, Random? random = null)
    {
        string? configured = Environment.GetEnvironmentVariable("FINEAGE_BOSS_STORE");
        string path = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(StoreDirectory(accountStorePath), "boss-schedule.json")
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

    public void Save()
    {
        lock (_gate)
            SaveLocked();
    }

    void Restore()
    {
        var snapshot = _store.Load();
        var saved = new Dictionary<int, BossScheduleRow>(snapshot.Bosses.Count);
        foreach (var row in snapshot.Bosses)
        {
            if (row.NpcId <= 0 || !saved.TryAdd(row.NpcId, row))
                continue;
            if (!Enum.IsDefined(typeof(BossLifeStatus), row.Status))
                throw new InvalidDataException($"Boss {row.NpcId} has status {row.Status}.");
        }

        long now = NowMs();
        bool changed = false;
        foreach (var definition in Catalog.Bosses)
        {
            var state = new BossState();
            if (saved.TryGetValue(definition.NpcId, out var row))
            {
                state.Status = (BossLifeStatus)row.Status;
                state.RespawnUnixMs = row.RespawnUnixMs;
                state.CurrentHp = row.CurrentHp;
                state.CurrentMp = row.CurrentMp;
                NormalizeVitals(state, definition);
            }
            else
            {
                state.Status = definition.InitialStatus;
                state.CurrentHp = definition.MaxHp;
                state.CurrentMp = definition.MaxMp;
            }

            if (state.Status == BossLifeStatus.Dead && (state.RespawnUnixMs == 0 || now > state.RespawnUnixMs))
            {
                state.Status = BossLifeStatus.Alive;
                state.RespawnUnixMs = 0;
                state.Scheduled = false;
                changed = true;
            }
            else
            {
                state.Scheduled = state.Status == BossLifeStatus.Dead
                    && definition.HasRespawnWindow
                    && state.RespawnUnixMs >= now;
            }

            _states.Add(definition.NpcId, state);
        }

        if (changed)
            SaveLocked();
    }

    long Roll(BossDefinition definition)
    {
        if (!definition.HasRespawnWindow)
            return 0;

        long min;
        long max;
        if (definition.Kind == BossKind.Raid)
        {
            min = Scale(definition.RespawnMinMillis, _raidMinMultiplier);
            max = Scale(definition.RespawnMaxMillis, _raidMaxMultiplier);
        }
        else
        {
            min = definition.RespawnMinMillis;
            max = definition.RespawnMaxMillis;
        }

        if (min == 0 && max == 0)
            return 0;
        return _random.NextInclusive(min, max);
    }

    bool IsArmed(BossDefinition definition)
    {
        if (!definition.HasRespawnWindow)
            return false;
        if (definition.Kind != BossKind.Raid)
            return true;

        int min = Scale(definition.RespawnMinMillis, _raidMinMultiplier);
        int max = Scale(definition.RespawnMaxMillis, _raidMaxMultiplier);
        return min > 0 || max > 0;
    }

    IReadOnlyList<BossSpawnOrder> OrdersWhere(Func<BossDefinition, BossState, bool> include)
    {
        var orders = new List<BossSpawnOrder>();
        foreach (var definition in Catalog.Bosses)
        {
            var state = _states[definition.NpcId];
            if (include(definition, state))
                orders.Add(Order(definition, state, ignoreEnabled: false));
        }

        return orders;
    }

    static BossSpawnOrder Order(BossDefinition definition, BossState state, bool ignoreEnabled) =>
        new(definition.NpcId, definition.ObjectId, state.CurrentHp, state.CurrentMp, ignoreEnabled);

    static BossView View(BossDefinition definition, BossState state) =>
        new(
            definition.NpcId,
            definition.ObjectId,
            definition.Kind,
            state.Status,
            state.RespawnUnixMs,
            state.CurrentHp,
            definition.MaxHp,
            state.CurrentMp,
            definition.MaxMp,
            definition.Enabled);

    void SaveLocked()
    {
        var snapshot = new BossScheduleSnapshot();
        foreach (var definition in Catalog.Bosses)
        {
            var state = _states[definition.NpcId];
            snapshot.Bosses.Add(new BossScheduleRow
            {
                NpcId = definition.NpcId,
                Status = (int)state.Status,
                RespawnUnixMs = state.RespawnUnixMs,
                CurrentHp = state.CurrentHp,
                CurrentMp = state.CurrentMp
            });
        }

        _store.Save(snapshot);
    }

    long NowMs() => _clock().ToUnixTimeMilliseconds();

    static void NormalizeVitals(BossState state, BossDefinition definition)
    {
        state.CurrentHp = ClampVital(state.CurrentHp, definition.MaxHp);
        state.CurrentMp = ClampVital(state.CurrentMp, definition.MaxMp);
    }

    static int ClampVital(int current, int max)
    {
        if (current <= 0)
            return max;
        return Math.Min(current, max);
    }

    /// <summary>Java <c>(int)</c> cast of the scaled millisecond window.</summary>
    static int Scale(long millis, double multiplier)
    {
        double scaled = millis * multiplier;
        if (double.IsNaN(scaled) || scaled >= int.MaxValue)
            return int.MaxValue;
        if (scaled <= int.MinValue)
            return int.MinValue;
        return (int)scaled;
    }

    static double RequireMultiplier(double value, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            throw new ArgumentOutOfRangeException(name, value, "Raid respawn multiplier must be finite and >= 0.");
        return value;
    }

    static double ReadMultiplier(string name, double fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value))
            return fallback;
        return value < 0 ? fallback : value;
    }

    static string StoreDirectory(string accountStorePath)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(accountStorePath));
        return string.IsNullOrEmpty(dir) ? "data" : dir;
    }

    sealed class BossState
    {
        public BossLifeStatus Status { get; set; }
        public long RespawnUnixMs { get; set; }
        public int CurrentHp { get; set; }
        public int CurrentMp { get; set; }
        public bool Scheduled { get; set; }
    }
}
