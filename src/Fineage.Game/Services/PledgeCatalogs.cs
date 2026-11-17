using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>Mobius <c>SocialClass</c> ordinals (pledge skill requirement vs member pledge class).</summary>
public enum SocialClass
{
    Vagabond = 0,
    Vassal = 1,
    Apprentice = 2,
    Heir = 3,
    Knight = 4,
    Elder = 5,
    Baron = 6,
    Viscount = 7,
    Count = 8,
    Marquis = 9,
    Duke = 10,
    GrandDuke = 11,
    DistinguishedKing = 12,
    Emperor = 13
}

public sealed record PledgeSkillItem(int ItemId, int Count);

/// <summary>One pledge skill tree row: <see cref="ClanLevel"/> is Mobius <c>getLevel</c>, <see cref="Reputation"/> its <c>levelUpSp</c>.</summary>
public sealed record PledgeSkillRow(int SkillId, int Level, string Name, int ClanLevel, int Reputation, SocialClass SocialClass, IReadOnlyList<PledgeSkillItem> Items);

/// <summary>data/pledge-skills.json (Mobius pledgeSkillTree.xml, converted).</summary>
public sealed class PledgeSkillCatalog
{
    public const string FileName = "pledge-skills.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    readonly List<PledgeSkillRow> _rows;

    public PledgeSkillCatalog(IEnumerable<PledgeSkillRow> rows) => _rows = rows.ToList();

    public static readonly PledgeSkillCatalog Empty = new([]);

    public IReadOnlyList<PledgeSkillRow> Rows => _rows;

    public static PledgeSkillCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path));
    }

    public static PledgeSkillCatalog Parse(string json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions) ?? new FileDto();
        var rows = new List<PledgeSkillRow>();
        foreach (var s in file.Skills ?? [])
        {
            if (s.SkillId <= 0 || s.Level <= 0)
                continue;
            var social = Enum.TryParse<SocialClass>((s.SocialClass ?? "").Replace("_", ""), ignoreCase: true, out var parsed)
                ? parsed
                : SocialClass.Vagabond;
            rows.Add(new PledgeSkillRow(
                s.SkillId,
                s.Level,
                s.Name ?? "",
                s.ClanLevel,
                s.Reputation,
                social,
                (s.Items ?? []).Where(i => i.Id > 0 && i.Count > 0).Select(i => new PledgeSkillItem(i.Id, i.Count)).ToArray()));
        }

        return new PledgeSkillCatalog(rows);
    }

    /// <summary>
    /// Mobius <c>SkillTreeData.getAvailablePledgeSkills</c>: for each skill the next level the clan does not own yet,
    /// gated by the clan level.
    /// </summary>
    public IReadOnlyList<PledgeSkillRow> Available(int clanLevel, IReadOnlyDictionary<int, int> owned)
    {
        var result = new List<PledgeSkillRow>();
        foreach (var row in _rows)
        {
            if (row.ClanLevel > clanLevel)
                continue;
            int have = owned.TryGetValue(row.SkillId, out int lvl) ? lvl : 0;
            if (row.Level == have + 1)
                result.Add(row);
        }

        return result;
    }

    public PledgeSkillRow? Find(int skillId, int level) =>
        _rows.FirstOrDefault(r => r.SkillId == skillId && r.Level == level);

    public SocialClass RequiredClass(int skillId, int level) =>
        Find(skillId, level)?.SocialClass ?? SocialClass.Vagabond;

    sealed class FileDto
    {
        public List<SkillDto>? Skills { get; set; }
    }

    sealed class SkillDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public string? Name { get; set; }
        public int ClanLevel { get; set; }
        public int Reputation { get; set; }
        public string? SocialClass { get; set; }
        public List<ItemDto>? Items { get; set; }
    }

    sealed class ItemDto
    {
        public int Id { get; set; }
        public int Count { get; set; }
    }
}

public sealed record ClanHallInfo(int Id, string Name, long Lease, string Description, string Location, int Grade, long StartingBid);

/// <summary>data/clan-halls.json (Mobius clanhall.sql auctionable halls + ClanHallAuctionManager starting bids).</summary>
public sealed class ClanHallCatalog
{
    public const string FileName = "clan-halls.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    readonly Dictionary<int, ClanHallInfo> _halls;

    public ClanHallCatalog(IEnumerable<ClanHallInfo> halls) => _halls = halls.ToDictionary(h => h.Id);

    public static readonly ClanHallCatalog Empty = new([]);

    public IReadOnlyCollection<ClanHallInfo> Halls => _halls.Values;

    public bool TryGet(int id, out ClanHallInfo info) => _halls.TryGetValue(id, out info!);

    public static ClanHallCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path));
    }

    public static ClanHallCatalog Parse(string json)
    {
        var file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions) ?? new FileDto();
        return new ClanHallCatalog((file.Halls ?? [])
            .Where(h => h.Id > 0)
            .Select(h => new ClanHallInfo(h.Id, h.Name ?? "", h.Lease, h.Description ?? "", h.Location ?? "", h.Grade, h.StartingBid)));
    }

    sealed class FileDto
    {
        public List<HallDto>? Halls { get; set; }
    }

    sealed class HallDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public long Lease { get; set; }
        public string? Description { get; set; }
        public string? Location { get; set; }
        public int Grade { get; set; }
        public long StartingBid { get; set; }
    }
}
