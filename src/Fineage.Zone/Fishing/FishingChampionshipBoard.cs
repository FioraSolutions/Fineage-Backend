using System.Globalization;
using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Fishing;

public sealed class FishingChampionshipSettings
{
    public bool Enabled { get; init; } = true;
    public int RewardItemId { get; init; } = 57;
    public long[] Rewards { get; init; } = [800_000, 500_000, 300_000, 200_000, 100_000];
    public long PeriodMs { get; init; } = 7 * 24 * 60 * 60 * 1000L;
    public int LureBonusMinId { get; init; } = 8484;
    public int LureBonusMaxId { get; init; } = 8486;
    public double LureBonusMax { get; init; } = 3.0;
    public static readonly long[] MobiusRewards = [800_000, 500_000, 300_000, 200_000, 100_000];
    public static FishingChampionshipSettings Default => Load();
    public static FishingChampionshipSettings Load()
    {
        string? path = TeleportCatalog.FindFile("fishing-championship.json");
        if (path == null) return new();
        var dto = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions) ?? new();
        var rewards = (dto.Rewards ?? []).Where(r => r > 0).Take(5).ToArray();
        if (rewards.Length == 0) rewards = MobiusRewards;
        return new() { Enabled = dto.Enabled ?? true, RewardItemId = dto.RewardItemId > 0 ? dto.RewardItemId : 57, Rewards = rewards, PeriodMs = dto.PeriodMs > 0 ? dto.PeriodMs : 7 * 24 * 60 * 60 * 1000L, LureBonusMinId = dto.LureBonusMinId > 0 ? dto.LureBonusMinId : 8484, LureBonusMaxId = dto.LureBonusMaxId > 0 ? dto.LureBonusMaxId : 8486, LureBonusMax = dto.LureBonusMax > 0 ? dto.LureBonusMax : 3.0 };
    }
    static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    sealed class FileDto
    {
        public bool? Enabled { get; set; }
        public int RewardItemId { get; set; }
        public long[]? Rewards { get; set; }
        public long PeriodMs { get; set; }
        public int LureBonusMinId { get; set; }
        public int LureBonusMaxId { get; set; }
        public double LureBonusMax { get; set; }
    }
}

public sealed class FishingChampionshipEntry { public string Name { get; set; } = ""; public double Length { get; set; } public bool Claimed { get; set; } }
public sealed class FishingChampionshipSnapshot { public int Version { get; set; } = 1; public long PeriodEndUnixMs; public List<FishingChampionshipEntry> Current { get; set; } = []; public List<FishingChampionshipEntry> Winners { get; set; } = []; }

public interface IFishingChampionshipStore { FishingChampionshipSnapshot Load(); void Save(FishingChampionshipSnapshot snapshot); }
public sealed class MemoryFishingChampionshipStore : IFishingChampionshipStore { string _json = ""; public FishingChampionshipSnapshot Load() => _json.Length == 0 ? new() : JsonSerializer.Deserialize<FishingChampionshipSnapshot>(_json) ?? new(); public void Save(FishingChampionshipSnapshot s) => _json = JsonSerializer.Serialize(s); }
public sealed class JsonFishingChampionshipStore(string path) : IFishingChampionshipStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true }; readonly object _gate = new();
    public FishingChampionshipSnapshot Load() { lock (_gate) { if (!File.Exists(path)) return new(); var data = JsonSerializer.Deserialize<FishingChampionshipSnapshot>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException(path); data.Current ??= []; data.Winners ??= []; return data; } }
    public void Save(FishingChampionshipSnapshot snapshot) { lock (_gate) { Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "."); var tmp = path + ".tmp"; File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, JsonOptions)); File.Move(tmp, path, true); } }
}

public sealed class FishingChampionshipBoard
{
    readonly FishingChampionshipSettings _settings; readonly IFishingChampionshipStore _store; readonly FishingChampionshipSnapshot _s;
    public FishingChampionshipBoard(long loadedAtUnixMs, FishingChampionshipSettings? settings = null, IFishingChampionshipStore? store = null)
    { _settings = settings ?? FishingChampionshipSettings.Default; _store = store ?? new MemoryFishingChampionshipStore(); _s = _store.Load(); if (_s.PeriodEndUnixMs == 0) { _s.PeriodEndUnixMs = loadedAtUnixMs + _settings.PeriodMs; Save(); } }
    public static FishingChampionshipBoard OpenInStateDirectory(string stateDirectory, long loadedAtUnixMs, FishingChampionshipSettings? settings = null) => new(loadedAtUnixMs, settings, new JsonFishingChampionshipStore(Path.Combine(string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory, "fishing-championship.json")));
    public FishingChampionshipSettings Settings => _settings; public long PeriodEndUnixMs => _s.PeriodEndUnixMs;
    public IReadOnlyList<FishingChampionshipEntry> CurrentRanking => SortDesc(_s.Current); public IReadOnlyList<FishingChampionshipEntry> LastWinners => SortDesc(_s.Winners);
    public long MinutesRemaining(long nowUnixMs) => Math.Max(0, (_s.PeriodEndUnixMs - nowUnixMs) / 60_000);
    public double RecordCatch(string playerName, int lureId, Random random)
    {
        if (!_settings.Enabled || playerName.Length == 0) return 0;
        double len = random.Next(60, 90) + random.Next(0, 1000) / 1000.0;
        if (lureId >= _settings.LureBonusMinId && lureId <= _settings.LureBonusMaxId) len += random.NextDouble() * _settings.LureBonusMax;
        var existing = _s.Current.Find(e => e.Name.Equals(playerName, StringComparison.OrdinalIgnoreCase));
        if (existing != null) { if (existing.Length < len) { existing.Length = len; Save(); } return len; }
        if (_s.Current.Count < 5) { _s.Current.Add(new() { Name = playerName, Length = len }); Save(); return len; }
        if (len <= _s.Current.Min(e => e.Length)) return len;
        _s.Current.Remove(_s.Current.OrderBy(e => e.Length).First()); _s.Current.Add(new() { Name = playerName, Length = len }); Save(); return len;
    }
    public long TryClaim(string playerName, out int rank) { rank = 0; var ordered = SortDesc(_s.Winners); for (int i = 0; i < ordered.Count; i++) { if (!ordered[i].Name.Equals(playerName, StringComparison.OrdinalIgnoreCase)) continue; rank = i + 1; if (ordered[i].Claimed || rank > _settings.Rewards.Length) return 0; ordered[i].Claimed = true; Save(); return _settings.Rewards[rank - 1]; } return 0; }
    public bool Tick(long nowUnixMs) { if (nowUnixMs < _s.PeriodEndUnixMs) return false; _s.Winners = SortDesc(_s.Current).Select(e => new FishingChampionshipEntry { Name = e.Name, Length = e.Length }).ToList(); _s.Current.Clear(); _s.PeriodEndUnixMs = nowUnixMs + _settings.PeriodMs; Save(); return true; }
    static List<FishingChampionshipEntry> SortDesc(List<FishingChampionshipEntry> list) => [.. list.OrderByDescending(e => e.Length).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)];
    void Save() => _store.Save(_s);
    public static string FormatLength(double length) => length.ToString("0.00", CultureInfo.InvariantCulture);
}
