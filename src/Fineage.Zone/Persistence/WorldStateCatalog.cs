using Fineage.Accounts;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game.Persistence;
using Fineage.Game.Punishment;
using Fineage.Zone.Bosses;
using Fineage.Zone.Fishing;
using Fineage.Zone.Lottery;
using Fineage.Zone.MonsterRace;
using Fineage.Zone.Olympiad;
using Fineage.Zone.PcCafe;
using Fineage.Zone.RaidBossPoints;
using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.Persistence;

/// <summary>One world subsystem kept in <see cref="WorldStateStore"/>: its id, layout, and the sidecar it replaced.</summary>
public interface IWorldStateKind
{
    string System { get; }

    /// <summary>File name of the pre-#996 JSON sidecar in <c>FINEAGE_STATE_DIR</c>, read once by the cutover import.</summary>
    string LegacyFileName { get; }

    int SchemaVersion { get; }

    /// <summary>
    /// Reads the legacy sidecar strictly (no fresh fallback) and returns its snapshot re-serialized the way the store
    /// saves it. Throws <see cref="StateFileException"/> naming the file on any defect.
    /// </summary>
    string ReadLegacy(string path);
}

/// <summary>A typed <see cref="IWorldStateKind"/>.</summary>
public sealed class WorldStateKind<T> : IWorldStateKind
    where T : class, IVersionedStateSnapshot
{
    public WorldStateKind(
        string system,
        string legacyFileName,
        int schemaVersion,
        JsonSerializerOptions options,
        Func<T> createFresh,
        Action<T>? normalize = null,
        bool legacyUnversioned = false)
    {
        System = system;
        LegacyFileName = legacyFileName;
        SchemaVersion = schemaVersion;
        Options = options;
        CreateFresh = createFresh;
        Normalize = normalize;
        LegacyUnversioned = legacyUnversioned;
    }

    public string System { get; }

    public string LegacyFileName { get; }

    public int SchemaVersion { get; }

    public JsonSerializerOptions Options { get; }

    public Func<T> CreateFresh { get; }

    public Action<T>? Normalize { get; }

    /// <summary>The legacy file never carried a version (PC cafe): a file without one is version 1, anything else is checked.</summary>
    public bool LegacyUnversioned { get; }

    public WorldStateSlot<T> Slot(WorldStateStore store) => new(store, System, SchemaVersion, Options, CreateFresh, Normalize);

    /// <summary>One saved document of this system, strictly (<see cref="WorldStateStore.Parse{T}"/>).</summary>
    public T Parse(WorldStateDocument document)
    {
        var data = WorldStateStore.Parse<T>(document, SchemaVersion, Options);
        Normalize?.Invoke(data);
        return data;
    }

    public string ReadLegacy(string path)
    {
        T data = LegacyUnversioned
            ? AtomicJsonFile.LoadUnversionedStrict<T>(path, System, SchemaVersion, Options)
            : AtomicJsonFile.LoadStrict<T>(path, System, SchemaVersion, Options);
        data.SchemaVersion = SchemaVersion;
        Normalize?.Invoke(data);
        return JsonSerializer.Serialize(data, Options);
    }
}

/// <summary>The closed list of world subsystems saved in the database (#996), with the layout each one reads.</summary>
public static class WorldStateCatalog
{
    static JsonSerializerOptions Plain() => new() { DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    static JsonSerializerOptions CamelCase() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static WorldStateKind<BossScheduleSnapshot> BossSchedule { get; } = new(
        WorldStateSystems.BossSchedule, "boss-schedule.json", 1, CamelCase(), static () => new(),
        static data => data.Bosses ??= []);

    public static WorldStateKind<SevenSignsSnapshot> SevenSigns { get; } = new(
        WorldStateSystems.SevenSigns, "seven-signs.json", 1, Plain(), static () => new(),
        static data => data.Players ??= []);

    public static WorldStateKind<OlympiadSnapshot> Olympiad { get; } = new(
        WorldStateSystems.Olympiad, "olympiad.json", 1, Plain(), static () => new(),
        static data =>
        {
            data.Granted ??= [];
            data.Nobles ??= [];
            data.Heroes ??= [];
            data.LastPeriod ??= [];
            data.Passes ??= [];
        });

    public static WorldStateKind<LotterySnapshot> Lottery { get; } = new(
        WorldStateSystems.Lottery, "lottery.json", 1, new JsonSerializerOptions(), static () => new(),
        static data =>
        {
            data.Draws ??= [];
            data.Tickets ??= [];
        });

    public static WorldStateKind<RaidBossPointsSnapshot> RaidBossPoints { get; } = new(
        WorldStateSystems.RaidBossPoints, "raid-boss-points-store.json", 1, CamelCase(), static () => new(),
        static data =>
        {
            data.Characters ??= [];
            foreach (var row in data.Characters)
                row.ByBoss ??= [];
        });

    public static WorldStateKind<PunishmentSnapshot> Punishment { get; } = new(
        WorldStateSystems.Punishment, "punishments.json", PunishmentStateJson.SchemaVersion, PunishmentStateJson.Options,
        static () => new(), static data => data.Rows ??= []);

    public static WorldStateKind<MonsterRaceSnapshot> MonsterRace { get; } = new(
        WorldStateSystems.MonsterRace, "monster-race-state.json", 1, new JsonSerializerOptions(), static () => new(),
        static data =>
        {
            data.History ??= [];
            data.BetsPerLane ??= new();
            data.Runners ??= [];
            data.Speeds ??= [];
        });

    public static WorldStateKind<FishingChampionshipSnapshot> FishingChampionship { get; } = new(
        WorldStateSystems.FishingChampionship, "fishing-championship.json", 1, new JsonSerializerOptions(), static () => new(),
        static data =>
        {
            data.Current ??= [];
            data.Winners ??= [];
        });

    public static WorldStateKind<PcCafeSnapshot> PcCafe { get; } = new(
        WorldStateSystems.PcCafe, "pc-cafe.json", 1, new JsonSerializerOptions(), static () => new(),
        static data =>
        {
            data.Characters ??= [];
            foreach (var row in data.Characters)
                row.RedeemedCodes ??= [];
        },
        legacyUnversioned: true);

    public static IReadOnlyList<IWorldStateKind> All { get; } =
    [
        BossSchedule,
        SevenSigns,
        Olympiad,
        Lottery,
        RaidBossPoints,
        Punishment,
        MonsterRace,
        FishingChampionship,
        PcCafe
    ];
}

public sealed class StoredBossStore(WorldStateStore state) : IBossStore
{
    readonly WorldStateSlot<BossScheduleSnapshot> _slot = WorldStateCatalog.BossSchedule.Slot(state);

    public BossScheduleSnapshot Load() => _slot.Load();

    public void Save(BossScheduleSnapshot snapshot) => _slot.Save(snapshot);
}

public sealed class StoredSevenSignsStore(WorldStateStore state) : ISevenSignsStore
{
    readonly WorldStateSlot<SevenSignsSnapshot> _slot = WorldStateCatalog.SevenSigns.Slot(state);

    public SevenSignsSnapshot Load() => _slot.Load();

    public void Save(SevenSignsSnapshot snapshot) => _slot.Save(snapshot);
}

public sealed class StoredOlympiadStore(WorldStateStore state) : IOlympiadStore
{
    readonly WorldStateSlot<OlympiadSnapshot> _slot = WorldStateCatalog.Olympiad.Slot(state);

    public OlympiadSnapshot Load() => _slot.Load();

    public void Save(OlympiadSnapshot snapshot) => _slot.Save(snapshot);
}

public sealed class StoredLotteryStore(WorldStateStore state) : ILotteryStore
{
    readonly WorldStateSlot<LotterySnapshot> _slot = WorldStateCatalog.Lottery.Slot(state);

    public LotterySnapshot Load() => _slot.Load();

    public void Save(LotterySnapshot snapshot) => _slot.Save(snapshot);
}

public sealed class StoredRaidBossPointsStore(WorldStateStore state) : IRaidBossPointsStore
{
    readonly WorldStateSlot<RaidBossPointsSnapshot> _slot = WorldStateCatalog.RaidBossPoints.Slot(state);

    public RaidBossPointsSnapshot Load() => _slot.Load();

    public void Save(RaidBossPointsSnapshot snapshot) => _slot.Save(snapshot);
}

public sealed class StoredPunishmentStore(WorldStateStore state) : IPunishmentStore
{
    readonly WorldStateSlot<PunishmentSnapshot> _slot = WorldStateCatalog.Punishment.Slot(state);

    public PunishmentSnapshot Load() => _slot.Load();

    public void Save(PunishmentSnapshot snapshot) => _slot.Save(snapshot);
}

public sealed class StoredMonsterRaceStore(WorldStateStore state) : IMonsterRaceStore
{
    readonly WorldStateSlot<MonsterRaceSnapshot> _slot = WorldStateCatalog.MonsterRace.Slot(state);

    public MonsterRaceSnapshot Load() => _slot.Load();

    public void Save(MonsterRaceSnapshot snapshot) => _slot.Save(snapshot);
}

public sealed class StoredFishingChampionshipStore(WorldStateStore state) : IFishingChampionshipStore
{
    readonly WorldStateSlot<FishingChampionshipSnapshot> _slot = WorldStateCatalog.FishingChampionship.Slot(state);

    public FishingChampionshipSnapshot Load() => _slot.Load();

    public void Save(FishingChampionshipSnapshot snapshot) => _slot.Save(snapshot);
}

public sealed class StoredPcCafeStore(WorldStateStore state) : IPcCafeStore
{
    readonly WorldStateSlot<PcCafeSnapshot> _slot = WorldStateCatalog.PcCafe.Slot(state);

    public PcCafeSnapshot Load() => _slot.Load();

    public void Save(PcCafeSnapshot snapshot) => _slot.Save(snapshot);
}
