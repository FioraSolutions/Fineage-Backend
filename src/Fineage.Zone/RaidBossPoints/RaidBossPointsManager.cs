using Fineage.Zone.Persistence;

namespace Fineage.Zone.RaidBossPoints;

public readonly record struct RaidBossPointsRankEntry(int Rank, int CharacterId, string Name, int TotalPoints);

public sealed class RaidBossPointsManager
{
    readonly IRaidBossPointsStore _store;
    readonly RaidBossPointsCatalog _catalog;
    readonly object _gate = new();
    readonly Dictionary<int, RaidBossPointsRow> _byCharacter = [];

    public RaidBossPointsManager(RaidBossPointsCatalog catalog, IRaidBossPointsStore store)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Load();
    }

    public RaidBossPointsCatalog Catalog => _catalog;

    public static RaidBossPointsManager CreateDefault() =>
        new(RaidBossPointsCatalog.LoadDefault(), new MemoryRaidBossPointsStore());

    /// <summary>Opens the raid boss points saved in the database (#996).</summary>
    public static RaidBossPointsManager Open(WorldStateStore state)
    {
        return new RaidBossPointsManager(RaidBossPointsCatalog.LoadDefault(), new StoredRaidBossPointsStore(state));
    }

    void Load()
    {
        var snapshot = _store.Load();
        lock (_gate)
        {
            _byCharacter.Clear();
            foreach (var row in snapshot.Characters)
            {
                if (row.CharacterId <= 0)
                    continue;
                row.ByBoss ??= [];
                _byCharacter[row.CharacterId] = row;
            }
        }

        ZoneLog.Info($"[raid-points] loaded {_byCharacter.Count} characters from store");
    }

    public void Save()
    {
        lock (_gate)
        {
            _store.Save(new RaidBossPointsSnapshot
            {
                Characters = _byCharacter.Values.Select(CloneRow).ToList()
            });
        }
    }

    public void AddPoints(int characterId, string name, int bossNpcId, int points)
    {
        if (characterId <= 0 || bossNpcId <= 0 || points == 0)
            return;
        lock (_gate)
        {
            if (!_byCharacter.TryGetValue(characterId, out var row))
            {
                row = new RaidBossPointsRow { CharacterId = characterId, Name = name ?? "" };
                _byCharacter[characterId] = row;
            }

            if (!string.IsNullOrWhiteSpace(name))
                row.Name = name;
            row.ByBoss[bossNpcId] = row.ByBoss.GetValueOrDefault(bossNpcId) + points;
            Save();
        }
    }

    public int GetTotalPoints(int characterId)
    {
        lock (_gate)
            return _byCharacter.TryGetValue(characterId, out var row) ? row.ByBoss.Values.Sum() : 0;
    }

    public int CalculateRanking(int characterId) => RankIndex(characterId);

    public IReadOnlyList<RaidBossPointsRankEntry> TopN(int count)
    {
        lock (_gate)
        {
            var totals = _byCharacter.Values
                .Select(r => (r.CharacterId, r.Name, Total: r.ByBoss.Values.Sum()))
                .Where(t => t.Total > 0)
                .OrderByDescending(t => t.Total)
                .ThenBy(t => t.CharacterId)
                .Take(Math.Max(0, count))
                .ToList();
            var list = new List<RaidBossPointsRankEntry>(totals.Count);
            int rank = 1;
            foreach (var (id, name, total) in totals)
                list.Add(new RaidBossPointsRankEntry(rank++, id, name, total));
            return list;
        }
    }

    int RankIndex(int characterId)
    {
        var ordered = TopN(int.MaxValue);
        for (int i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].CharacterId == characterId)
                return ordered[i].Rank;
        }

        return 0;
    }

    static RaidBossPointsRow CloneRow(RaidBossPointsRow row) => new()
    {
        CharacterId = row.CharacterId,
        Name = row.Name,
        ByBoss = new Dictionary<int, int>(row.ByBoss)
    };

    public static int PointsForKill(int bossLevel, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return Math.Max(0, bossLevel / 2 + random.Next(-5, 6));
    }
}
