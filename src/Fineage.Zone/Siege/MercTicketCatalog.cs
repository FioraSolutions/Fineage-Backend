using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Siege;

/// <summary>Mobius <c>MercTicketManager.java</c> item→npc mapping and castle limits (<c>data/merc_tickets.json</c>).</summary>
public sealed class MercTicketCatalog
{
    public const string FileName = "merc_tickets.json";
    public const int MinItemId = 3960;
    public const int MaxItemId = 8030;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    static readonly Lazy<MercTicketCatalog> Shared = new(Load);

    readonly Dictionary<int, MercTicketRow> _byItem = [];
    readonly Dictionary<int, int> _maxPerCastle = [];
    readonly int _dawnTicketMinItemId;

    public MercTicketCatalog(IEnumerable<MercTicketRow> tickets, IReadOnlyDictionary<int, int> maxPerCastle, int dawnTicketMinItemId)
    {
        foreach (var row in tickets)
            _byItem[row.ItemId] = row;
        foreach (var (castleId, cap) in maxPerCastle)
            _maxPerCastle[castleId] = cap;
        _dawnTicketMinItemId = dawnTicketMinItemId;
    }

    public static MercTicketCatalog Default => Shared.Value;

    public bool IsMercTicket(int itemId) => _byItem.ContainsKey(itemId);

    public MercTicketRow? Find(int itemId) => _byItem.TryGetValue(itemId, out var row) ? row : null;

    public int TicketCastleId(int itemId) => Find(itemId)?.CastleId ?? 0;

    public int MaxPerCastle(int castleId) =>
        _maxPerCastle.TryGetValue(castleId, out int cap) ? cap : 0;

    /// <summary>Mobius <c>checkIsDawnPostingTicket</c> (non-rookie / Dawn-only bands).</summary>
    public bool IsDawnPostingTicket(int itemId)
    {
        if (itemId is > 6114 and < 6175)
            return true;
        if (itemId is > 6801 and < 6812)
            return true;
        if (itemId is > 7997 and < 8008)
            return true;
        if (itemId is > 7940 and < 7951)
            return true;
        if (itemId is > 6294 and < 6307)
            return true;
        if (itemId is > 6831 and < 6834)
            return true;
        if (itemId is > 8027 and < 8030)
            return true;
        return itemId is > 7970 and < 7973;
    }

    /// <summary>Mobius <c>checkIsRookiePostingTicket</c> (posting allowed while Dusk holds Strife).</summary>
    public bool IsRookiePostingTicket(int itemId) =>
        itemId >= _dawnTicketMinItemId && !IsDawnPostingTicket(itemId);

    static MercTicketCatalog Load()
    {
        string? path = TeleportCatalog.FindFile(FileName);
        if (path == null)
            return new MercTicketCatalog([], new Dictionary<int, int>(), 6175);
        var file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions) ?? new FileDto();
        var max = new Dictionary<int, int>();
        if (file.MaxPerCastle != null)
        {
            foreach (var (key, value) in file.MaxPerCastle)
            {
                if (int.TryParse(key, out int castleId) && castleId > 0)
                    max[castleId] = value;
            }
        }

        var rows = (file.Tickets ?? [])
            .Where(t => t.ItemId > 0 && t.NpcId > 0 && t.CastleId > 0)
            .Select(t => new MercTicketRow(t.ItemId, t.NpcId, t.CastleId, t.MaxPerType > 0 ? t.MaxPerType : 1))
            .ToArray();
        return new MercTicketCatalog(rows, max, file.DawnTicketMinItemId > 0 ? file.DawnTicketMinItemId : 6175);
    }

    sealed class FileDto
    {
        public Dictionary<string, int>? MaxPerCastle { get; set; }
        public int DawnTicketMinItemId { get; set; }
        public List<TicketDto>? Tickets { get; set; }
    }

    sealed class TicketDto
    {
        public int ItemId { get; set; }
        public int NpcId { get; set; }
        public int CastleId { get; set; }
        public int MaxPerType { get; set; }
    }
}

public readonly record struct MercTicketRow(int ItemId, int NpcId, int CastleId, int MaxPerType);

/// <summary>One hired defender placement (<c>castle_siege_guards</c> isHired = 1).</summary>
public sealed class HiredSiegeGuard
{
    public int ItemId { get; init; }
    public int NpcId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
    public int Heading { get; init; }
    public int GroundObjectId { get; set; }
}