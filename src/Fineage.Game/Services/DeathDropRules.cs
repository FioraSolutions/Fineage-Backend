using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>
/// Mobius <c>Player.onDieDropItem</c> rates (Rates.ini / PVP.ini). Pure selection of which inventory
/// rows to drop; the zone removes them and spawns ground stacks.
/// </summary>
public sealed class DeathDropRules
{
    public const string FileName = "death-drop.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    DeathDropRules(
        int playerDropLimit,
        int playerRateDrop,
        int playerRateDropItem,
        int playerRateDropEquip,
        int playerRateDropEquipWeapon,
        int karmaDropLimit,
        int karmaRateDrop,
        int karmaRateDropItem,
        int karmaRateDropEquip,
        int karmaRateDropEquipWeapon,
        int karmaPkLimit,
        HashSet<int> nonDroppable)
    {
        PlayerDropLimit = playerDropLimit;
        PlayerRateDrop = playerRateDrop;
        PlayerRateDropItem = playerRateDropItem;
        PlayerRateDropEquip = playerRateDropEquip;
        PlayerRateDropEquipWeapon = playerRateDropEquipWeapon;
        KarmaDropLimit = karmaDropLimit;
        KarmaRateDrop = karmaRateDrop;
        KarmaRateDropItem = karmaRateDropItem;
        KarmaRateDropEquip = karmaRateDropEquip;
        KarmaRateDropEquipWeapon = karmaRateDropEquipWeapon;
        KarmaPkLimit = karmaPkLimit;
        NonDroppableItemIds = nonDroppable;
    }

    public int PlayerDropLimit { get; }
    public int PlayerRateDrop { get; }
    public int PlayerRateDropItem { get; }
    public int PlayerRateDropEquip { get; }
    public int PlayerRateDropEquipWeapon { get; }
    public int KarmaDropLimit { get; }
    public int KarmaRateDrop { get; }
    public int KarmaRateDropItem { get; }
    public int KarmaRateDropEquip { get; }
    public int KarmaRateDropEquipWeapon { get; }
    public int KarmaPkLimit { get; }
    public IReadOnlySet<int> NonDroppableItemIds { get; }

    public readonly record struct DropCandidate(int ObjectId, int ItemId, int Count, bool Equipped, bool Weapon);

    public readonly record struct DropRates(int Percent, int Item, int Equip, int EquipWeapon, int Limit);

    /// <summary>
    /// Mobius gate: karma+PK uses karma rates; else NPC killer and level &gt; 4 use player rates; else none.
    /// Clan-war / PvP-zone exemptions are left to the caller (zone has no war/PVP zones yet).
    /// </summary>
    public DropRates? RatesFor(int karma, int pkKills, bool killerIsNpc, int victimLevel)
    {
        if (karma > 0 && pkKills >= KarmaPkLimit)
        {
            return new DropRates(KarmaRateDrop, KarmaRateDropItem, KarmaRateDropEquip, KarmaRateDropEquipWeapon,
                KarmaDropLimit);
        }

        if (killerIsNpc && victimLevel > 4)
        {
            return new DropRates(PlayerRateDrop, PlayerRateDropItem, PlayerRateDropEquip, PlayerRateDropEquipWeapon,
                PlayerDropLimit);
        }

        return null;
    }

    /// <summary>
    /// Walk inventory in order; roll the overall percent then per-item. Returns objectIds to drop (full stacks).
    /// </summary>
    public IReadOnlyList<int> SelectDrops(IReadOnlyList<DropCandidate> items, DropRates rates, Random rnd)
    {
        if (rates.Percent <= 0 || rates.Limit <= 0 || rnd.Next(100) >= rates.Percent)
            return [];

        var dropped = new List<int>();
        foreach (var row in items)
        {
            if (row.ItemId == WarehouseRules.AdenaItemId || NonDroppableItemIds.Contains(row.ItemId))
                continue;

            int chance = row.Equipped
                ? (row.Weapon ? rates.EquipWeapon : rates.Equip)
                : rates.Item;
            if (chance <= 0 || rnd.Next(100) >= chance)
                continue;

            dropped.Add(row.ObjectId);
            if (dropped.Count >= rates.Limit)
                break;
        }

        return dropped;
    }

    public static DeathDropRules LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName)
                   ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static DeathDropRules Parse(string json, string source = FileName)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, JsonOptions)
                  ?? throw new InvalidDataException($"{source} is empty.");
        var banned = new HashSet<int>(dto.NonDroppableItemIds ?? []);
        return new DeathDropRules(
            Math.Max(0, dto.PlayerDropLimit),
            Math.Clamp(dto.PlayerRateDrop, 0, 100),
            Math.Clamp(dto.PlayerRateDropItem, 0, 100),
            Math.Clamp(dto.PlayerRateDropEquip, 0, 100),
            Math.Clamp(dto.PlayerRateDropEquipWeapon, 0, 100),
            Math.Max(0, dto.KarmaDropLimit),
            Math.Clamp(dto.KarmaRateDrop, 0, 100),
            Math.Clamp(dto.KarmaRateDropItem, 0, 100),
            Math.Clamp(dto.KarmaRateDropEquip, 0, 100),
            Math.Clamp(dto.KarmaRateDropEquipWeapon, 0, 100),
            Math.Max(0, dto.KarmaPkLimit),
            banned);
    }

    sealed class Dto
    {
        public int PlayerDropLimit { get; set; } = 3;
        public int PlayerRateDrop { get; set; } = 5;
        public int PlayerRateDropItem { get; set; } = 70;
        public int PlayerRateDropEquip { get; set; } = 25;
        public int PlayerRateDropEquipWeapon { get; set; } = 5;
        public int KarmaDropLimit { get; set; } = 10;
        public int KarmaRateDrop { get; set; } = 70;
        public int KarmaRateDropItem { get; set; } = 50;
        public int KarmaRateDropEquip { get; set; } = 40;
        public int KarmaRateDropEquipWeapon { get; set; } = 10;
        public int KarmaPkLimit { get; set; } = 6;
        public List<int>? NonDroppableItemIds { get; set; }
    }
}
