using Fineage.Accounts;
using Fineage.Protocol;

namespace Fineage.Zone.Manor;

/// <summary>A castle's seed sale row (Mobius <c>SeedProduction</c>): what is left, what the period started with, the price.</summary>
public sealed class ManorProduct(int seedId, long amount, long price, long startAmount)
{
    public int SeedId { get; } = seedId;
    public long Amount { get; set; } = amount;
    public long Price { get; } = price;
    public long StartAmount { get; } = startAmount;

    public bool TryDecrease(long count)
    {
        if (count <= 0 || Amount < count)
            return false;
        Amount -= count;
        return true;
    }
}

/// <summary>A castle's crop purchase row (Mobius <c>CropProcure</c>): reward type 1 / 2 picks the seed's reward item.</summary>
public sealed class ManorProcure(int cropId, long amount, int rewardType, long startAmount, long price)
{
    public int CropId { get; } = cropId;
    public long Amount { get; set; } = amount;
    public int RewardType { get; } = rewardType;
    public long StartAmount { get; } = startAmount;
    public long Price { get; } = price;

    public bool TryDecrease(long count)
    {
        if (count <= 0 || Amount < count)
            return false;
        Amount -= count;
        return true;
    }
}

/// <summary>
/// Manor state (Mobius <c>CastleManorManager</c> maps): current and next period seed sales and crop purchases per castle,
/// the mode and when it changes next. The world owns the transitions; this holds the rows and their saves.
/// </summary>
public sealed class ManorBoard
{
    readonly Dictionary<int, List<ManorProduct>> _production = [];
    readonly Dictionary<int, List<ManorProduct>> _productionNext = [];
    readonly Dictionary<int, List<ManorProcure>> _procure = [];
    readonly Dictionary<int, List<ManorProcure>> _procureNext = [];

    public ManorBoard(ManorData data, ManorSettings settings)
    {
        Data = data;
        Settings = settings;
        Mode = settings.Enabled ? ManorProtocol.ModeApproved : ManorProtocol.ModeDisabled;
    }

    public ManorData Data { get; }
    public ManorSettings Settings { get; }
    public int Mode { get; set; }
    public long NextChangeMs { get; set; }

    public bool Disabled => Mode == ManorProtocol.ModeDisabled;
    public bool UnderMaintenance => Mode == ManorProtocol.ModeMaintenance;
    public bool Modifiable => Mode == ManorProtocol.ModeModifiable;

    public int SeedLimit(ManorSeed seed) => seed.LimitSeed * Math.Max(1, Settings.Rate);
    public int CropLimit(ManorSeed seed) => seed.LimitCrop * Math.Max(1, Settings.Rate);

    public List<ManorProduct> Production(int castleId, bool next) => Get(next ? _productionNext : _production, castleId);
    public List<ManorProcure> Procure(int castleId, bool next) => Get(next ? _procureNext : _procure, castleId);

    public void SetProduction(int castleId, bool next, List<ManorProduct> rows) => (next ? _productionNext : _production)[castleId] = rows;
    public void SetProcure(int castleId, bool next, List<ManorProcure> rows) => (next ? _procureNext : _procure)[castleId] = rows;

    public ManorProduct? Product(int castleId, int seedId, bool next) => Production(castleId, next).FirstOrDefault(p => p.SeedId == seedId);
    public ManorProcure? CropProcure(int castleId, int cropId, bool next) => Procure(castleId, next).FirstOrDefault(p => p.CropId == cropId);

    /// <summary>Mobius <c>getManorCost</c>: seed reference price × start amount plus crop price × start amount.</summary>
    public long ManorCost(int castleId, bool next)
    {
        long total = 0;
        foreach (var p in Production(castleId, next))
            total += Data.Seed(p.SeedId) is { } s ? s.SeedPrice * p.StartAmount : 1;
        foreach (var c in Procure(castleId, next))
            total += c.Price * c.StartAmount;
        return total;
    }

    /// <summary>Mobius <c>resetManorData</c>: the castle lost its owner.</summary>
    public void Reset(int castleId)
    {
        _production.Remove(castleId);
        _productionNext.Remove(castleId);
        _procure.Remove(castleId);
        _procureNext.Remove(castleId);
    }

    public IEnumerable<int> Castles =>
        _production.Keys.Concat(_productionNext.Keys).Concat(_procure.Keys).Concat(_procureNext.Keys).Distinct().OrderBy(id => id);

    public void Import(IEnumerable<ManorProductionSave> production, IEnumerable<ManorProcureSave> procure)
    {
        foreach (var row in production)
        {
            if (Data.Seed(row.SeedId) is not { } seed || seed.CastleId != row.CastleId)
                continue;
            Production(row.CastleId, row.NextPeriod).Add(new ManorProduct(row.SeedId, row.Amount, row.Price, row.StartAmount));
        }

        foreach (var row in procure)
        {
            if (Data.SeedByCrop(row.CropId, row.CastleId) == null)
                continue;
            Procure(row.CastleId, row.NextPeriod).Add(new ManorProcure(row.CropId, row.Amount, row.RewardType, row.StartAmount, row.Price));
        }
    }

    public ManorProductionSave[] ExportProduction() =>
        [.. Rows(_production, false), .. Rows(_productionNext, true)];

    public ManorProcureSave[] ExportProcure() =>
        [.. Rows(_procure, false), .. Rows(_procureNext, true)];

    static IEnumerable<ManorProductionSave> Rows(Dictionary<int, List<ManorProduct>> map, bool next) =>
        map.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value.Select(p =>
            new ManorProductionSave(kv.Key, p.SeedId, p.Amount, p.StartAmount, p.Price, next)));

    static IEnumerable<ManorProcureSave> Rows(Dictionary<int, List<ManorProcure>> map, bool next) =>
        map.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value.Select(p =>
            new ManorProcureSave(kv.Key, p.CropId, p.Amount, p.StartAmount, p.Price, p.RewardType, next)));

    static List<T> Get<T>(Dictionary<int, List<T>> map, int castleId)
    {
        if (!map.TryGetValue(castleId, out var list))
            map[castleId] = list = [];
        return list;
    }
}
