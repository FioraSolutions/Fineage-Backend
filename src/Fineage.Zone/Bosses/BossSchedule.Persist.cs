namespace Fineage.Zone.Bosses;

public sealed partial class BossSchedule
{
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

    sealed class BossState
    {
        public BossLifeStatus Status { get; set; }
        public long RespawnUnixMs { get; set; }
        public int CurrentHp { get; set; }
        public int CurrentMp { get; set; }
        public bool Scheduled { get; set; }
    }
}
