using System.Text.Json;
using Fineage.Game.Services;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.RaidBossPoints;

public sealed class RaidBossPointsCatalog
{
    public const string FileName = "raid-boss-points.json";
    public const int DefaultTopN = 10;
    public const string DefaultRankingPage = "raid/raid_boss_ranking.htm";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public int TopN { get; init; } = DefaultTopN;
    public IReadOnlyList<int> RankingNpcIds { get; init; } = [];
    public string RankingPage { get; init; } = DefaultRankingPage;

    public bool IsRankingNpc(int npcId) => RankingNpcIds.Contains(npcId);

    public static RaidBossPointsCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName)
            ?? throw new InvalidDataException($"missing data/{FileName}; raid boss ranking NPC cannot load");

        var catalog = Parse(File.ReadAllText(path), path);
        var npcs = NpcCatalog.LoadDefault();
        foreach (int id in catalog.RankingNpcIds)
        {
            if (!npcs.ById.ContainsKey(id))
                ZoneLog.Warn($"[raid-points] ranking npcId={id} missing from npc catalog ({NpcCatalog.FileName})");
        }

        return catalog;
    }

    internal static RaidBossPointsCatalog ForTests(int topN = 5, params int[] npcIds) =>
        new() { TopN = topN, RankingNpcIds = npcIds.Length > 0 ? npcIds : [31603] };

    static RaidBossPointsCatalog Parse(string json, string source)
    {
        var dto = JsonSerializer.Deserialize<RaidBossPointsCatalogDto>(json, JsonOptions)
            ?? throw new InvalidDataException($"empty raid boss points catalog: {source}");
        return new RaidBossPointsCatalog
        {
            TopN = dto.TopN switch
            {
                null => DefaultTopN,
                > 0 and <= 100 => dto.TopN.Value,
                _ => throw new InvalidDataException($"{source}: topN must be 1..100")
            },
            RankingNpcIds = dto.RankingNpcIds ?? [],
            RankingPage = string.IsNullOrWhiteSpace(dto.RankingPage) ? DefaultRankingPage : dto.RankingPage.Trim()
        };
    }

    sealed class RaidBossPointsCatalogDto
    {
        public int? TopN { get; set; }
        public List<int>? RankingNpcIds { get; set; }
        public string? RankingPage { get; set; }
    }
}
