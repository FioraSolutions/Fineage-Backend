using System.Text.Json;

namespace Fineage.Zone.PcCafe;

/// <summary>One redeemable PC Cafe coupon code → item grants.</summary>
public sealed class PcCafeCouponDefinition
{
    public string Code { get; init; } = "";
    public int Points { get; init; }
    public List<PcCafeCouponItem> Items { get; init; } = [];
}

public sealed class PcCafeCouponItem
{
    public int ItemId { get; init; }
    public int Count { get; init; } = 1;
}

/// <summary>Mobius has no Interlude coupon table — Fineage catalog under <c>data/pc-cafe-coupons.json</c>.</summary>
public sealed class PcCafeCouponCatalog
{
    public const string FileName = "pc-cafe-coupons.json";

    readonly Dictionary<string, PcCafeCouponDefinition> _byCode;

    PcCafeCouponCatalog(Dictionary<string, PcCafeCouponDefinition> byCode) => _byCode = byCode;

    public static PcCafeCouponCatalog Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase));

    public bool TryGet(string code, out PcCafeCouponDefinition def) =>
        _byCode.TryGetValue(code.Trim(), out def!);

    public static PcCafeCouponCatalog LoadDefault()
    {
        string? path = Fineage.Game.Services.TeleportCatalog.FindFile(FileName);
        if (path == null)
            return Empty;
        return Parse(File.ReadAllText(path), path);
    }

    public static PcCafeCouponCatalog Parse(string json, string source = FileName)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("coupons", out var arr) || arr.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{source}: missing coupons array");

        var map = new Dictionary<string, PcCafeCouponDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in arr.EnumerateArray())
        {
            string code = row.GetProperty("code").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(code))
            {
                ZoneLog.Error($"[pc-cafe] coupon missing code in {source}");
                continue;
            }

            int points = row.TryGetProperty("points", out var p) ? p.GetInt32() : 0;
            var items = new List<PcCafeCouponItem>();
            if (row.TryGetProperty("items", out var itemArr) && itemArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var it in itemArr.EnumerateArray())
                {
                    int itemId = it.GetProperty("itemId").GetInt32();
                    int count = it.TryGetProperty("count", out var c) ? c.GetInt32() : 1;
                    if (itemId <= 0 || count <= 0)
                    {
                        ZoneLog.Error($"[pc-cafe] coupon {code} bad item in {source}");
                        continue;
                    }

                    items.Add(new PcCafeCouponItem { ItemId = itemId, Count = count });
                }
            }

            if (points <= 0 && items.Count == 0)
            {
                ZoneLog.Error($"[pc-cafe] coupon {code} grants nothing in {source}");
                continue;
            }

            map[code.Trim()] = new PcCafeCouponDefinition
            {
                Code = code.Trim(),
                Points = Math.Max(0, points),
                Items = items
            };
        }

        return new PcCafeCouponCatalog(map);
    }
}
