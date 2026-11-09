using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Game;

/// <summary>One dye from Mobius <c>hennaList.xml</c> (data/henna.json).</summary>
public sealed record HennaDefinition(
    int DyeId,
    string Name,
    int ItemId,
    int Level,
    int Str,
    int Con,
    int Dex,
    int Int,
    int Men,
    int Wit,
    int WearCount,
    int WearFee,
    int CancelCount,
    int CancelFee,
    IReadOnlyList<int> ClassIds);

/// <summary>Sum of worn henna base-stat deltas (Mobius <c>Player.getHennaStat*</c>).</summary>
public readonly record struct HennaStatBonus(int Str, int Con, int Dex, int Int, int Men, int Wit)
{
    public static HennaStatBonus Zero => default;

    public HennaStatBonus Add(HennaDefinition dye) =>
        new(Str + dye.Str, Con + dye.Con, Dex + dye.Dex, Int + dye.Int, Men + dye.Men, Wit + dye.Wit);
}

/// <summary>Mobius henna dye catalog. Players wear up to three dyes; deltas fold before the STR/DEX bonus table.</summary>
public sealed class HennaCatalog
{
    public const string FileName = "henna.json";
    public const int SlotCount = 3;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    readonly Dictionary<int, HennaDefinition> _byId;

    HennaCatalog(Dictionary<int, HennaDefinition> byId) => _byId = byId;

    public static HennaCatalog Empty { get; } = new([]);

    public int Count => _byId.Count;

    public bool TryGet(int dyeId, out HennaDefinition dye) => _byId.TryGetValue(dyeId, out dye!);

    /// <summary>Sum STR/CON/… for up to <see cref="SlotCount"/> dye ids (0 = empty slot).</summary>
    public HennaStatBonus Sum(ReadOnlySpan<int> dyeIds)
    {
        var sum = HennaStatBonus.Zero;
        foreach (int id in dyeIds)
        {
            if (id <= 0)
                continue;
            if (_byId.TryGetValue(id, out var dye))
                sum = sum.Add(dye);
        }
        return sum;
    }

    public static HennaCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path), path);
    }

    public static HennaCatalog Parse(string json, string source = FileName)
    {
        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not a henna table.", ex);
        }

        var byId = new Dictionary<int, HennaDefinition>();
        foreach (var row in file?.Hennas ?? [])
        {
            if (row.DyeId <= 0)
                throw new InvalidDataException($"{source} has a henna row without a valid dyeId.");
            byId[row.DyeId] = new HennaDefinition(
                row.DyeId,
                row.Name ?? "",
                row.ItemId,
                row.Level,
                row.Str,
                row.Con,
                row.Dex,
                row.Int,
                row.Men,
                row.Wit,
                row.WearCount,
                row.WearFee,
                row.CancelCount,
                row.CancelFee,
                row.ClassIds ?? []);
        }
        return new HennaCatalog(byId);
    }

    sealed class FileDto
    {
        public List<RowDto>? Hennas { get; set; }
    }

    sealed class RowDto
    {
        public int DyeId { get; set; }
        public string? Name { get; set; }
        public int ItemId { get; set; }
        public int Level { get; set; }
        public int Str { get; set; }
        public int Con { get; set; }
        public int Dex { get; set; }
        public int Int { get; set; }
        public int Men { get; set; }
        public int Wit { get; set; }
        public int WearCount { get; set; }
        public int WearFee { get; set; }
        public int CancelCount { get; set; }
        public int CancelFee { get; set; }
        public List<int>? ClassIds { get; set; }
    }
}
