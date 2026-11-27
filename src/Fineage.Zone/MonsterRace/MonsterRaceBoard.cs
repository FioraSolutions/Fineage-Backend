using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game;
using Fineage.Game.Persistence;
using Fineage.Protocol;
using Fineage.Zone.Npcs;
using Fineage.Zone.Persistence;

namespace Fineage.Zone.MonsterRace;

public enum MonsterRaceState
{
    AcceptingBets,
    Waiting,
    StartingRace,
    RaceEnd
}

public sealed class MonsterRaceHistory
{
    public int RaceId { get; set; }
    public int FirstLane { get; set; }
    public int SecondLane { get; set; }
    public double WinnerOdd { get; set; }
}

public sealed class MonsterRaceRunner
{
    public int NpcId { get; set; }
    public int ObjectId { get; set; }
    public double CollisionHeight { get; set; }
    public double CollisionRadius { get; set; }
}

public sealed class MonsterRaceSnapshot : IVersionedStateSnapshot
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonIgnore]
    public int Version
    {
        get => SchemaVersion;
        set => SchemaVersion = value;
    }

    public int RaceNumber { get; set; } = 1;
    public long CycleStartUnixMs { get; set; }
    public int LastHandledCountdown { get; set; } = -1;
    public List<MonsterRaceHistory> History { get; set; } = [];
    public Dictionary<int, long> BetsPerLane { get; set; } = new();
    public List<MonsterRaceRunner> Runners { get; set; } = [];
    public int[][] Speeds { get; set; } = Array.Empty<int[]>();
    public int FirstLaneIndex { get; set; }
    public int SecondLaneIndex { get; set; }
    public MonsterRaceState State { get; set; } = MonsterRaceState.RaceEnd;
    public int PacketCodeIndex { get; set; }
}

public interface IMonsterRaceStore
{
    MonsterRaceSnapshot Load();
    void Save(MonsterRaceSnapshot snapshot);
}

public sealed class MemoryMonsterRaceStore : IMonsterRaceStore
{
    string _json = "";

    public MonsterRaceSnapshot Load() =>
        _json.Length == 0 ? new MonsterRaceSnapshot() : JsonSerializer.Deserialize<MonsterRaceSnapshot>(_json) ?? new();

    public void Save(MonsterRaceSnapshot snapshot) => _json = JsonSerializer.Serialize(snapshot);
}

public readonly record struct MonsterRaceSystemNotice(int MessageId, string[] Params, string Text = "");

public readonly record struct MonsterRacePacketOrder(int CodeA, int CodeB, bool SendSpeeds);

public readonly record struct MonsterRaceSound(string File, bool Music);

public readonly record struct MonsterRaceDespawn(int[] ObjectIds);

public sealed class MonsterRaceEffect
{
    public List<MonsterRaceSystemNotice> Notices { get; } = [];
    public MonsterRacePacketOrder? Packet { get; set; }
    public List<MonsterRaceSound> Sounds { get; } = [];
    public MonsterRaceDespawn? Despawn { get; set; }
}

/// <summary>Mobius <c>MonsterRaceManager</c> state machine driven by wall-second countdown.</summary>
public sealed class MonsterRaceBoard
{
    readonly MonsterRaceSettings _settings;
    readonly IMonsterRaceStore _store;
    readonly ItemCatalog _items;
    readonly NpcCatalog _npcs;
    readonly Func<int> _allocateObjectId;
    readonly MonsterRaceSnapshot _s;

    public MonsterRaceBoard(
        long loadedAtUnixMs,
        ItemCatalog items,
        NpcCatalog npcs,
        Func<int> allocateObjectId,
        MonsterRaceSettings? settings = null,
        IMonsterRaceStore? store = null)
    {
        _settings = settings ?? MonsterRaceSettings.Mobius;
        _store = store ?? new MemoryMonsterRaceStore();
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _allocateObjectId = allocateObjectId ?? throw new ArgumentNullException(nameof(allocateObjectId));
        _s = _store.Load();
        foreach (int lane in Enumerable.Range(1, 8))
            _s.BetsPerLane.TryAdd(lane, 0);
        if (_s.CycleStartUnixMs == 0)
        {
            _s.CycleStartUnixMs = loadedAtUnixMs;
            Save();
        }
    }

    /// <summary>Opens the monster race saved in the database (#996).</summary>
    public static MonsterRaceBoard Open(
        WorldStateStore state,
        long loadedAtUnixMs,
        ItemCatalog items,
        NpcCatalog npcs,
        Func<int> allocateObjectId,
        MonsterRaceSettings? settings = null)
    {
        return new MonsterRaceBoard(loadedAtUnixMs, items, npcs, allocateObjectId, settings ?? MonsterRaceSettings.Mobius,
            new StoredMonsterRaceStore(state));
    }

    public MonsterRaceSettings Settings => _settings;
    public int RaceNumber => _s.RaceNumber;
    public MonsterRaceState State => _s.State;
    public IReadOnlyList<MonsterRaceHistory> History => _s.History;
    public IReadOnlyList<MonsterRaceRunner> Runners => _s.Runners;
    public int[][] Speeds => _s.Speeds;

    public IReadOnlyList<double> Odds =>
        MonsterRaceLogic.CalculateOdds(_s.BetsPerLane, _settings.OddsPoolShare, _settings.MinOdd);

    public int FirstLaneIndex => _s.FirstLaneIndex;
    public int SecondLaneIndex => _s.SecondLaneIndex;

    public void Use(MonsterRaceSnapshot snapshot) => throw new NotSupportedException("Use full board reload via store.");

    public void AddBet(int lane, long amount)
    {
        if (lane is < 1 or > 8 || amount < 0)
            throw new ArgumentOutOfRangeException(nameof(lane));
        _s.BetsPerLane[lane] = _s.BetsPerLane.GetValueOrDefault(lane) + amount;
        Save();
    }

    public void ClearBets()
    {
        foreach (int lane in _s.BetsPerLane.Keys.ToList())
            _s.BetsPerLane[lane] = 0;
        Save();
    }

    public MonsterRaceHistory? HistoryForRace(int raceId) =>
        _s.History.FirstOrDefault(h => h.RaceId == raceId);

    public IReadOnlyList<MonsterRaceEffect> Tick(long nowUnixMs, Random random)
    {
        var batch = new List<MonsterRaceEffect>();
        if (!_settings.Enabled)
            return batch;

        int countdown = MonsterRaceLogic.CountdownAt(_s.CycleStartUnixMs, nowUnixMs, _settings.CountdownWrapAbove);
        if (countdown > _settings.CountdownWrapAbove)
        {
            _s.CycleStartUnixMs += (_settings.CountdownWrapAbove + 1) * 1000L;
            countdown = 0;
            _s.LastHandledCountdown = -1;
        }

        int from = _s.LastHandledCountdown + 1;
        if (from < 0)
            from = 0;
        for (int sec = from; sec <= countdown; sec++)
        {
            var effect = HandleSecond(sec, random);
            if (effect.Notices.Count > 0 || effect.Packet != null || effect.Sounds.Count > 0 || effect.Despawn != null)
                batch.Add(effect);
        }

        _s.LastHandledCountdown = countdown;
        Save();
        return batch;
    }

    MonsterRaceEffect HandleSecond(int sec, Random random)
    {
        var effect = new MonsterRaceEffect();
        switch (sec)
        {
            case 0:
                StartNewRace(random);
                _s.State = MonsterRaceState.AcceptingBets;
                _s.PacketCodeIndex = 0;
                effect.Packet = PacketOrder(0, true);
                effect.Notices.Add(Notice(MonsterRaceProtocol.TicketsAvailable, [_s.RaceNumber.ToString()]));
                break;
            case 30 or 60 or 90 or 120 or 150 or 180 or 210 or 240 or 270 or 330 or 360 or 390 or 420 or 450 or 480 or 510 or 540 or 570 or 630 or 660 or 690 or 720 or 750 or 780 or 810 or 870:
                effect.Notices.Add(Notice(MonsterRaceProtocol.TicketsAvailable, [_s.RaceNumber.ToString()]));
                break;
            case 300:
                effect.Notices.Add(Notice(MonsterRaceProtocol.NowSelling, [_s.RaceNumber.ToString()]));
                effect.Notices.Add(Notice(MonsterRaceProtocol.SalesEndInMinutes, ["10"]));
                break;
            case 600:
                effect.Notices.Add(Notice(MonsterRaceProtocol.NowSelling, [_s.RaceNumber.ToString()]));
                effect.Notices.Add(Notice(MonsterRaceProtocol.SalesEndInMinutes, ["5"]));
                break;
            case 840:
                effect.Notices.Add(Notice(MonsterRaceProtocol.NowSelling, [_s.RaceNumber.ToString()]));
                effect.Notices.Add(Notice(MonsterRaceProtocol.SalesEndInMinutes, ["1"]));
                break;
            case 900:
                _s.State = MonsterRaceState.Waiting;
                effect.Notices.Add(Notice(MonsterRaceProtocol.TicketsAvailable, [_s.RaceNumber.ToString()]));
                effect.Notices.Add(Notice(MonsterRaceProtocol.SalesClosedOddsPosted, [_s.RaceNumber.ToString()]));
                break;
            case 960:
                effect.Notices.Add(Notice(MonsterRaceProtocol.RaceBeginsInMinutes, ["2", _s.RaceNumber.ToString()]));
                break;
            case 1020:
                effect.Notices.Add(Notice(MonsterRaceProtocol.RaceBeginsInMinutes, ["1", _s.RaceNumber.ToString()]));
                break;
            case 1050:
                effect.Notices.Add(Notice(MonsterRaceProtocol.RaceBeginsIn30Seconds, [_s.RaceNumber.ToString()]));
                break;
            case 1070:
                effect.Notices.Add(Notice(MonsterRaceProtocol.RaceCountdownFiveSeconds, [_s.RaceNumber.ToString()]));
                break;
            case > 1070 and < 1080:
                effect.Notices.Add(Notice(MonsterRaceProtocol.RaceBeginsInSeconds, [(1080 - sec).ToString()]));
                break;
            case 1080:
                _s.State = MonsterRaceState.StartingRace;
                _s.PacketCodeIndex = 1;
                effect.Packet = PacketOrder(1, true);
                effect.Notices.Add(Notice(MonsterRaceProtocol.TheyreOff, []));
                effect.Sounds.Add(new(_settings.Sounds.RaceMusic, true));
                effect.Sounds.Add(new(_settings.Sounds.RaceStart, false));
                break;
            case 1085:
                _s.PacketCodeIndex = 2;
                effect.Packet = PacketOrder(2, false);
                break;
            case 1115:
                FinishRace();
                _s.State = MonsterRaceState.RaceEnd;
                effect.Notices.Add(Notice(MonsterRaceProtocol.FirstAndSecondPrizeLanes,
                    [(_s.FirstLaneIndex + 1).ToString(), (_s.SecondLaneIndex + 1).ToString()]));
                effect.Notices.Add(Notice(MonsterRaceProtocol.RaceFinished, [_s.RaceNumber.ToString()]));
                _s.RaceNumber++;
                break;
            case 1140:
                effect.Despawn = new(_s.Runners.Select(r => r.ObjectId).ToArray());
                break;
        }

        return effect;
    }

    void StartNewRace(Random random)
    {
        _s.History.Add(new MonsterRaceHistory { RaceId = _s.RaceNumber });
        var pool = _settings.RunnerNpcIds.ToList();
        Shuffle(pool, random);
        _s.Runners = [];
        for (int i = 0; i < MonsterRaceProtocol.RunnerCount; i++)
        {
            int npcId = pool[i];
            var template = _npcs.Get(npcId);

            _s.Runners.Add(new MonsterRaceRunner
            {
                NpcId = npcId,
                ObjectId = _allocateObjectId(),
                CollisionHeight = template.CollisionHeight,
                CollisionRadius = template.CollisionRadius
            });
        }

        var rolled = MonsterRaceLogic.RollSpeeds(random);
        _s.Speeds = rolled.speeds;
        _s.FirstLaneIndex = rolled.firstLane;
        _s.SecondLaneIndex = rolled.secondLane;
    }

    void FinishRace()
    {
        if (_s.History.Count == 0)
            return;
        var row = _s.History[^1];
        row.FirstLane = _s.FirstLaneIndex;
        row.SecondLane = _s.SecondLaneIndex;
        var odds = Odds;
        row.WinnerOdd = _s.FirstLaneIndex >= 0 && _s.FirstLaneIndex < odds.Count ? odds[_s.FirstLaneIndex] : 0;
        ClearBets();
    }

    MonsterRacePacketOrder PacketOrder(int codeIndex, bool sendSpeeds)
    {
        var codes = _settings.PacketCodes[Math.Clamp(codeIndex, 0, _settings.PacketCodes.Length - 1)];
        _s.PacketCodeIndex = codeIndex;
        return new(codes[0], codes[1], sendSpeeds && codes[0] == 0);
    }

    public MonsterRacePacketOrder CurrentPacket() =>
        PacketOrder(_s.PacketCodeIndex, _s.PacketCodeIndex == 0);

    static MonsterRaceSystemNotice Notice(int id, string[] pars) => new(id, pars, "");

    static void Shuffle<T>(IList<T> list, Random random)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>Writes the current race board to its store; the shutdown registry calls this once the world is drained.</summary>
    public void Save() => _store.Save(_s);
}
