using System.Text.Json;

namespace Fineage.Game.Services;

public sealed record HallFunctionLevel(int Level, int Fee);

public sealed record HallFunctionTypeSpec(int Type, long FeeRatioMs, IReadOnlyList<HallFunctionLevel> Levels);

/// <summary>data/clan-hall-functions.json — Mobius Feature.ini CH_* fee tables.</summary>
public sealed class HallFunctionCatalog
{
    public const string FileName = "clan-hall-functions.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    readonly Dictionary<int, HallFunctionTypeSpec> _byType;

    public HallFunctionCatalog(long hallLeasePeriodMs, bool buffMpFree, IEnumerable<HallFunctionTypeSpec> types)
    {
        HallLeasePeriodMs = hallLeasePeriodMs;
        BuffMpFree = buffMpFree;
        _byType = types.ToDictionary(t => t.Type);
    }

    public long HallLeasePeriodMs { get; }
    public bool BuffMpFree { get; }

    public static HallFunctionCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path));
    }

    public const long DefaultHallLeasePeriodMs = 604800000L;

    public static readonly HallFunctionCatalog Empty = new(DefaultHallLeasePeriodMs, buffMpFree: false, []);

    public bool TryGet(int type, out HallFunctionTypeSpec spec) => _byType.TryGetValue(type, out spec!);

    public bool TryFee(int type, int level, out int fee, out long ratioMs)
    {
        fee = 0;
        ratioMs = 0;
        if (!_byType.TryGetValue(type, out var spec))
            return false;
        ratioMs = spec.FeeRatioMs;
        foreach (var row in spec.Levels)
        {
            if (row.Level == level)
            {
                fee = row.Fee;
                return true;
            }
        }

        return false;
    }

    public static HallFunctionCatalog Parse(string json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions) ?? new FileDto();
        var types = (file.Functions ?? [])
            .Select(kv => new HallFunctionTypeSpec(
                kv.Value.Type,
                kv.Value.FeeRatioMs,
                (kv.Value.Levels ?? []).Select(l => new HallFunctionLevel(l.Level, l.Fee)).ToArray()))
            .ToArray();
        return new HallFunctionCatalog(file.HallLeasePeriodMs, file.BuffMpFree, types);
    }

    sealed class FileDto
    {
        public long HallLeasePeriodMs { get; set; } = DefaultHallLeasePeriodMs;
        public bool BuffMpFree { get; set; }
        public Dictionary<string, TypeDto>? Functions { get; set; }
    }

    sealed class TypeDto
    {
        public int Type { get; set; }
        public long FeeRatioMs { get; set; }
        public List<LevelDto>? Levels { get; set; }
    }

    sealed class LevelDto
    {
        public int Level { get; set; }
        public int Fee { get; set; }
    }
}