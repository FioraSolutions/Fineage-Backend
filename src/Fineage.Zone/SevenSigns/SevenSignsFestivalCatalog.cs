using System.Text.Json;

namespace Fineage.Zone.SevenSigns;

public sealed record FestivalSpawnPoint(int X, int Y, int Z);

public sealed record FestivalTierSide(
    FestivalSpawnPoint PlayerSpawn,
    int WitchNpcId,
    int WitchX,
    int WitchY,
    int WitchZ);

public sealed record FestivalTier(
    int Id,
    string LevelLabel,
    FestivalTierSide Dawn,
    FestivalTierSide Dusk);

public sealed class SevenSignsFestivalCatalog
{
    public const string FileName = "seven-signs-festival.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, FestivalTier> _tiers;

    public SevenSignsFestivalCatalog(
        int offeringItemId,
        int offeringValue,
        int maxOffset,
        IReadOnlyList<int> levelScores,
        int festivalLengthMinutes,
        int cycleLengthMinutes,
        long firstSpawnMs,
        long firstSwarmMs,
        long secondSpawnMs,
        long secondSwarmMs,
        long chestSpawnMs,
        int minPartySize,
        int defaultRespawnSeconds,
        IEnumerable<FestivalTier> tiers)
    {
        OfferingItemId = offeringItemId;
        OfferingValue = offeringValue;
        MaxOffset = maxOffset;
        LevelScores = levelScores.ToArray();
        FestivalLengthMs = festivalLengthMinutes * 60_000L;
        CycleLengthMs = cycleLengthMinutes * 60_000L;
        FirstSpawnMs = firstSpawnMs;
        FirstSwarmMs = firstSwarmMs;
        SecondSpawnMs = secondSpawnMs;
        SecondSwarmMs = secondSwarmMs;
        ChestSpawnMs = chestSpawnMs;
        MinPartySize = minPartySize;
        DefaultRespawnSeconds = defaultRespawnSeconds;
        _tiers = [];
        foreach (var tier in tiers)
            _tiers.Add(tier.Id, tier);
        if (_tiers.Count != 5 || LevelScores.Count != 5)
            throw new InvalidDataException("Festival catalog expects 5 tiers and level scores.");
    }

    public int OfferingItemId { get; }
    public int OfferingValue { get; }
    public int MaxOffset { get; }
    public IReadOnlyList<int> LevelScores { get; }
    public long FestivalLengthMs { get; }
    public long CycleLengthMs { get; }
    /// <summary>Mobius <c>FESTIVAL_SIGNUP_TIME</c>: cycle length minus festival run minus one minute.</summary>
    public long SignupCloseMs => Math.Max(0, CycleLengthMs - FestivalLengthMs - 60_000);
    public long FirstSpawnMs { get; }
    public long FirstSwarmMs { get; }
    public long SecondSpawnMs { get; }
    public long SecondSwarmMs { get; }
    public long ChestSpawnMs { get; }
    public int MinPartySize { get; }
    public int DefaultRespawnSeconds { get; }

    public FestivalTier Tier(int id) => _tiers[id];

    public FestivalTierSide Side(FestivalTier tier, SevenSignsCabal cabal) =>
        cabal == SevenSignsCabal.Dawn ? tier.Dawn : tier.Dusk;

    public static SevenSignsFestivalCatalog Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Festival catalog was not found.", path);
        var file = JsonSerializer.Deserialize<FestivalFile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Festival catalog was empty.");
        if (file.Version != 1)
            throw new InvalidDataException("Festival catalog version is not supported.");
        var tiers = (file.Tiers ?? []).Select(MapTier).ToList();
        static long MinMs(int minutes, int fallbackMinutes) =>
            Math.Max(0, (minutes > 0 ? minutes : fallbackMinutes) * 60_000L);
        return new SevenSignsFestivalCatalog(
            file.OfferingItemId,
            file.OfferingValue,
            file.MaxOffset,
            file.LevelScores ?? [],
            file.FestivalLengthMinutes,
            file.CycleLengthMinutes,
            MinMs(file.FirstSpawnMinutes, 2),
            MinMs(file.FirstSwarmMinutes, 5),
            MinMs(file.SecondSpawnMinutes, 9),
            MinMs(file.SecondSwarmMinutes, 12),
            MinMs(file.ChestSpawnMinutes, 15),
            file.MinPartySize > 0 ? file.MinPartySize : SevenSignsFestivalGuide.MinPartySize,
            file.DefaultRespawnSeconds > 0 ? file.DefaultRespawnSeconds : 60,
            tiers);
    }

    public static SevenSignsFestivalCatalog LoadDefault()
    {
        string? dir = Environment.GetEnvironmentVariable("FINEAGE_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(dir))
            return Load(Path.Combine(dir.Trim(), FileName));
        foreach (string root in new[] { Path.Combine(Directory.GetCurrentDirectory(), "data"), Path.Combine(AppContext.BaseDirectory, "data") })
        {
            string path = Path.Combine(root, FileName);
            if (File.Exists(path))
                return Load(path);
        }
        string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", FileName));
        if (File.Exists(repo))
            return Load(repo);
        throw new DirectoryNotFoundException("Could not find data/seven-signs-festival.json.");
    }

    static FestivalTier MapTier(FestivalTierDto row) => new(
        row.Id,
        row.LevelLabel ?? "",
        MapSide(row.Dawn),
        MapSide(row.Dusk));

    static FestivalTierSide MapSide(FestivalSideDto? side)
    {
        var p = side?.PlayerSpawn ?? throw new InvalidDataException("playerSpawn missing");
        return new FestivalTierSide(new FestivalSpawnPoint(p.X, p.Y, p.Z), side.WitchNpcId, side.WitchX, side.WitchY, side.WitchZ);
    }

    sealed class FestivalFile
    {
        public int Version { get; set; }
        public int OfferingItemId { get; set; }
        public int OfferingValue { get; set; }
        public int MaxOffset { get; set; }
        public List<int>? LevelScores { get; set; }
        public int FestivalLengthMinutes { get; set; }
        public int CycleLengthMinutes { get; set; }
        public int FirstSpawnMinutes { get; set; }
        public int FirstSwarmMinutes { get; set; }
        public int SecondSpawnMinutes { get; set; }
        public int SecondSwarmMinutes { get; set; }
        public int ChestSpawnMinutes { get; set; }
        public int MinPartySize { get; set; }
        public int DefaultRespawnSeconds { get; set; }
        public List<FestivalTierDto>? Tiers { get; set; }
    }

    sealed class FestivalTierDto
    {
        public int Id { get; set; }
        public string? LevelLabel { get; set; }
        public FestivalSideDto? Dawn { get; set; }
        public FestivalSideDto? Dusk { get; set; }
    }

    sealed class FestivalSideDto
    {
        public FestivalSpawnDto? PlayerSpawn { get; set; }
        public int WitchNpcId { get; set; }
        public int WitchX { get; set; }
        public int WitchY { get; set; }
        public int WitchZ { get; set; }
    }

    sealed class FestivalSpawnDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }
}
