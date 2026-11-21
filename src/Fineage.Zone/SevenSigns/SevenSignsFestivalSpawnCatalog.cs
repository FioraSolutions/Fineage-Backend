using System.Text.Json;

namespace Fineage.Zone.SevenSigns;

public sealed record FestivalMobSpawn(int X, int Y, int Z, int NpcId);

public enum FestivalMobWave
{
    Primary,
    Secondary,
    Chest
}

public sealed class SevenSignsFestivalSpawnCatalog
{
    public const string FileName = "seven-signs-festival-spawns.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<(SevenSignsCabal Cabal, int Tier, FestivalMobWave Wave), IReadOnlyList<FestivalMobSpawn>> _spawns = [];

    SevenSignsFestivalSpawnCatalog(
        Dictionary<(SevenSignsCabal, int, FestivalMobWave), IReadOnlyList<FestivalMobSpawn>> spawns)
    {
        foreach (var kv in spawns)
            _spawns[kv.Key] = kv.Value;
    }

    public IReadOnlyList<FestivalMobSpawn> Spawns(SevenSignsCabal cabal, int tierId, FestivalMobWave wave)
    {
        if (tierId is < 0 or > 4)
            return [];
        return _spawns.TryGetValue((cabal, tierId, wave), out var list) ? list : [];
    }

    public static SevenSignsFestivalSpawnCatalog Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Festival spawn catalog was not found.", path);
        var file = JsonSerializer.Deserialize<SpawnFile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Festival spawn catalog was empty.");
        if (file.Version != 1 || file.Waves == null)
            throw new InvalidDataException("Festival spawn catalog version is not supported.");

        var map = new Dictionary<(SevenSignsCabal, int, FestivalMobWave), IReadOnlyList<FestivalMobSpawn>>();
        foreach (var (cabalKey, waves) in file.Waves)
        {
            var cabal = cabalKey.Equals("dawn", StringComparison.OrdinalIgnoreCase)
                ? SevenSignsCabal.Dawn
                : SevenSignsCabal.Dusk;
            foreach (var (waveKey, tiers) in waves)
            {
                if (!Enum.TryParse<FestivalMobWave>(waveKey, ignoreCase: true, out var wave))
                    continue;
                for (int tier = 0; tier < tiers.Count && tier < 5; tier++)
                {
                    var rows = tiers[tier].Select(r => new FestivalMobSpawn(r.X, r.Y, r.Z, r.NpcId)).ToList();
                    map[(cabal, tier, wave)] = rows;
                }
            }
        }

        return new SevenSignsFestivalSpawnCatalog(map);
    }

    public static SevenSignsFestivalSpawnCatalog LoadDefault()
    {
        string baseDir = AppContext.BaseDirectory;
        string path = Path.Combine(baseDir, "data", FileName);
        if (!File.Exists(path))
            path = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "data", FileName));
        return Load(path);
    }

    sealed class SpawnFile
    {
        public int Version { get; set; }
        public Dictionary<string, Dictionary<string, List<List<SpawnRow>>>>? Waves { get; set; }
    }

    sealed class SpawnRow
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int NpcId { get; set; }
    }
}