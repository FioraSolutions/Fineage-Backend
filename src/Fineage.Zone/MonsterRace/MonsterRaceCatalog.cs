using System.Text.Json;
using Fineage.Protocol;
using Fineage.Zone.Geo;

namespace Fineage.Zone.MonsterRace;

public sealed class MonsterRaceSettings
{
    public bool Enabled { get; init; } = true;
    public int[] RunnerNpcIds { get; init; } = [];
    public int[] TicketPrices { get; init; } = MonsterRaceProtocol.TicketPrices;
    public int TicketItemId { get; init; } = MonsterRaceProtocol.TicketItemId;
    public int AdenaItemId { get; init; } = MonsterRaceProtocol.AdenaItemId;
    public int RaceManagerNpcId { get; init; } = MonsterRaceProtocol.RaceManagerNpcId;
    public int CycleSeconds { get; init; } = 1200;
    public int CountdownWrapAbove { get; init; } = 1200;
    public double MinOdd { get; init; } = 1.25;
    public double OddsPoolShare { get; init; } = 0.7;
    public double LoserPayoutRate { get; init; }
    public MonsterRaceTrack Track { get; init; } = new();
    public int[][] PacketCodes { get; init; } =
    [
        [-1, 0],
        [0, 15322],
        [13765, -1]
    ];
    public string[] BroadcastPeaceZones { get; init; } = [];
    public MonsterRaceSounds Sounds { get; init; } = new();

    public static MonsterRaceSettings Mobius { get; } = LoadDefault();

    public static bool FromEnvironmentDisabled() =>
        string.Equals(Environment.GetEnvironmentVariable("FINEAGE_MONSTER_RACE_ENABLED"), "0", StringComparison.Ordinal);

    public static MonsterRaceSettings LoadDefault()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "monster-race.json");
        if (!File.Exists(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), "data", "monster-race.json");
        if (!File.Exists(path))
        {
            ZoneLog.Error($"[monster-race] missing data file: data/monster-race.json");
            return new MonsterRaceSettings { Enabled = false };
        }

        try
        {
            var dto = JsonSerializer.Deserialize<MonsterRaceFileDto>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto == null)
                throw new InvalidDataException("empty monster-race.json");
            Validate(dto, path);
            return new MonsterRaceSettings
            {
                Enabled = dto.Enabled && !FromEnvironmentDisabled(),
                RunnerNpcIds = dto.RunnerNpcIds ?? [],
                TicketPrices = dto.TicketPrices ?? MonsterRaceProtocol.TicketPrices,
                TicketItemId = dto.TicketItemId,
                AdenaItemId = dto.AdenaItemId,
                RaceManagerNpcId = dto.RaceManagerNpcId,
                CycleSeconds = dto.CycleSeconds,
                CountdownWrapAbove = dto.CountdownWrapAbove,
                MinOdd = dto.MinOdd,
                OddsPoolShare = dto.OddsPoolShare,
                LoserPayoutRate = dto.LoserPayoutRate,
                Track = dto.Track ?? new MonsterRaceTrack(),
                PacketCodes = dto.PacketCodes ?? [[-1, 0], [0, 15322], [13765, -1]],
                BroadcastPeaceZones = dto.BroadcastPeaceZones ?? [],
                Sounds = dto.Sounds ?? new MonsterRaceSounds()
            };
        }
        catch (Exception ex)
        {
            ZoneLog.Error($"[monster-race] failed to load data/monster-race.json: {ex.Message}");
            return new MonsterRaceSettings { Enabled = false };
        }
    }

    static void Validate(MonsterRaceFileDto dto, string path)
    {
        if (dto.RunnerNpcIds is not { Length: >= 8 })
            throw new InvalidDataException($"{path}: runnerNpcIds must list at least 8 npc ids.");
        if (dto.TicketPrices is not { Length: 8 })
            throw new InvalidDataException($"{path}: ticketPrices must list 8 amounts.");
        if (dto.PacketCodes is not { Length: 3 })
            throw new InvalidDataException($"{path}: packetCodes must have 3 rows.");
        if (dto.LoserPayoutRate <= 0 || double.IsNaN(dto.LoserPayoutRate) || double.IsInfinity(dto.LoserPayoutRate))
            throw new InvalidDataException($"{path}: loserPayoutRate must be a positive finite number.");
    }

    public PeaceZone[] ResolveBroadcastZones(PeaceZones peace)
    {
        if (BroadcastPeaceZones.Length == 0)
            return [];
        var byName = peace.All.ToDictionary(z => z.Name, StringComparer.OrdinalIgnoreCase);
        var list = new List<PeaceZone>();
        foreach (string name in BroadcastPeaceZones)
        {
            if (byName.TryGetValue(name, out var zone))
                list.Add(zone);
            else
                ZoneLog.Error($"[monster-race] broadcastPeaceZones: unknown peace zone '{name}'");
        }
        return list.ToArray();
    }

    sealed class MonsterRaceFileDto
    {
        public bool Enabled { get; set; } = true;
        public int[]? RunnerNpcIds { get; set; }
        public int[]? TicketPrices { get; set; }
        public int TicketItemId { get; set; } = MonsterRaceProtocol.TicketItemId;
        public int AdenaItemId { get; set; } = MonsterRaceProtocol.AdenaItemId;
        public int RaceManagerNpcId { get; set; } = MonsterRaceProtocol.RaceManagerNpcId;
        public int CycleSeconds { get; set; } = 1200;
        public int CountdownWrapAbove { get; set; } = 1200;
        public double MinOdd { get; set; } = 1.25;
        public double OddsPoolShare { get; set; } = 0.7;
        public double LoserPayoutRate { get; set; }
        public MonsterRaceTrack? Track { get; set; }
        public int[][]? PacketCodes { get; set; }
        public string[]? BroadcastPeaceZones { get; set; }
        public MonsterRaceSounds? Sounds { get; set; }
    }
}

public sealed class MonsterRaceTrack
{
    public int OriginX { get; init; } = 14107;
    public int OriginYBase { get; init; } = 181875;
    public int LaneSpacing { get; init; } = 58;
    public int OriginZ { get; init; } = -3566;
    public int EndX { get; init; } = 12080;
    public int RunSpeedByte { get; init; } = 120;
}

public sealed class MonsterRaceSounds
{
    public string RaceMusic { get; init; } = "S_Race";
    public string RaceStart { get; init; } = "ItemSound2.race_start";
}