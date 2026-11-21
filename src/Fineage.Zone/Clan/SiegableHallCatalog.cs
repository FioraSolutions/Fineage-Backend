using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Clan;

public readonly record struct SiegableHallTemplate(
    int Id,
    string Name,
    string Location,
    long SiegeLengthMs,
    int SchedulePeriodDays,
    int ScheduleHour,
    int ScheduleMinute,
    bool AllowStartWithoutAttackers = false);

public sealed class SiegableHallCatalog
{
    public const string FileName = "siegable-clan-halls.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    static readonly Lazy<SiegableHallCatalog> Shared = new(Load);

    readonly Dictionary<int, SiegableHallTemplate> _byId;

    public SiegableHallCatalog(IEnumerable<SiegableHallTemplate> rows)
    {
        _byId = rows.ToDictionary(r => r.Id);
    }

    public static SiegableHallCatalog Default => Shared.Value;

    public bool IsSiegable(int hallId) => _byId.ContainsKey(hallId);

    public SiegableHallTemplate? Find(int hallId) => _byId.TryGetValue(hallId, out var row) ? row : null;

    public IEnumerable<SiegableHallTemplate> All => _byId.Values.OrderBy(r => r.Id);

    static SiegableHallCatalog Load()
    {
        string? path = TeleportCatalog.FindFile(FileName);
        if (path == null)
            return new SiegableHallCatalog([]);
        var file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions) ?? new FileDto();
        var rows = (file.Halls ?? []).Where(h => h.Id > 0).Select(h => new SiegableHallTemplate(
            h.Id,
            h.Name ?? "",
            h.Location ?? "",
            h.SiegeLengthMs > 0 ? h.SiegeLengthMs : 3_600_000,
            h.SchedulePeriodDays > 0 ? h.SchedulePeriodDays : 14,
            h.ScheduleHour,
            h.ScheduleMinute,
            h.AllowStartWithoutAttackers)).ToArray();
        return new SiegableHallCatalog(rows);
    }

    sealed class FileDto
    {
        public List<HallDto>? Halls { get; set; }
    }

    sealed class HallDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Location { get; set; }
        public long SiegeLengthMs { get; set; }
        public int SchedulePeriodDays { get; set; }
        public int ScheduleHour { get; set; }
        public int ScheduleMinute { get; set; }
        public bool AllowStartWithoutAttackers { get; set; }
    }
}