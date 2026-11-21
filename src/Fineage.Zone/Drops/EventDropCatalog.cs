using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Drops;

public sealed class EventDropWindow
{
    public required string Id { get; init; }
    public long StartUnixMs { get; init; }
    public long EndUnixMs { get; init; }
    public int MaxKillerLevelGap { get; init; } = 9;
    public required IReadOnlyList<DropGroupDefinition> Groups { get; init; }
}

/// <summary>Mobius <c>EventDroplist</c>: time-bounded global drop rows merged into death loot.</summary>
public sealed class EventDropCatalog
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    readonly IReadOnlyList<EventDropWindow> _windows;

    public EventDropCatalog(IReadOnlyList<EventDropWindow> windows) => _windows = windows;

    public static EventDropCatalog Empty { get; } = new([]);

    public static EventDropCatalog LoadDefault()
    {
        string? path = TeleportCatalog.FindFile("event-drops.json");
        return path == null ? Empty : Parse(File.ReadAllText(path));
    }

    public static EventDropCatalog Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<FileDto>(json, JsonOptions) ?? new FileDto();
        var windows = new List<EventDropWindow>();
        foreach (var e in dto.Events ?? [])
        {
            if (string.IsNullOrWhiteSpace(e.Id))
                continue;
            var groups = new List<DropGroupDefinition>();
            foreach (var g in e.Groups ?? [])
            {
                var items = new List<DropItemDefinition>();
                foreach (var row in g.Items ?? [])
                {
                    if (row.ItemId <= 0)
                        continue;
                    items.Add(new DropItemDefinition
                    {
                        ItemId = row.ItemId,
                        Min = row.Min > 0 ? row.Min : 1,
                        Max = row.Max > 0 ? row.Max : row.Min,
                        Chance = row.Weight > 0 ? row.Weight : 100
                    });
                }
                if (items.Count == 0)
                    continue;
                double chance = g.ChancePerMillion > 0 ? g.ChancePerMillion / 10_000d : g.Chance;
                groups.Add(new DropGroupDefinition { Chance = chance, Items = items });
            }
            windows.Add(new EventDropWindow
            {
                Id = e.Id,
                StartUnixMs = e.StartUnixMs,
                EndUnixMs = e.EndUnixMs,
                MaxKillerLevelGap = e.MaxKillerLevelGap > 0 ? e.MaxKillerLevelGap : 9,
                Groups = groups
            });
        }
        return new EventDropCatalog(windows);
    }

    public IReadOnlyList<DropStack> RollExtra(Random random, DropRates rates, long nowUnixMs, int victimLevel, int killerLevel)
    {
        var stacks = new List<DropStack>();
        foreach (var window in _windows)
        {
            if (window.Groups.Count == 0)
                continue;
            if (window.EndUnixMs > window.StartUnixMs && (nowUnixMs < window.StartUnixMs || nowUnixMs > window.EndUnixMs))
                continue;
            if (killerLevel - victimLevel > window.MaxKillerLevelGap)
                continue;
            stacks.AddRange(DropRoller.RollEventDeath(window.Groups, random, rates, victimLevel, killerLevel, raid: false));
        }
        return stacks;
    }

    sealed class FileDto
    {
        public List<EventDto>? Events { get; set; }
    }

    sealed class EventDto
    {
        public string? Id { get; set; }
        public long StartUnixMs { get; set; }
        public long EndUnixMs { get; set; }
        public int MaxKillerLevelGap { get; set; }
        public List<GroupDto>? Groups { get; set; }
    }

    sealed class GroupDto
    {
        public double Chance { get; set; }
        public int ChancePerMillion { get; set; }
        public List<ItemDto>? Items { get; set; }
    }

    sealed class ItemDto
    {
        public int ItemId { get; set; }
        public int Min { get; set; }
        public int Max { get; set; }
        public double Weight { get; set; }
    }
}