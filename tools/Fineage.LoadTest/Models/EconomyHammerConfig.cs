namespace Fineage.LoadTest.Models;

/// <summary>Ti Village economy hub defaults (Mobius spawn object ids from <c>data/spawns/ti_village.json</c>).</summary>
public sealed class EconomyHammerConfig
{
    public const int AdenaItemId = 57;

    public bool Enabled { get; set; }

    /// <summary>Fail the run when ledger invariants, unexplained rejects, or economy log errors appear.</summary>
    public bool RequireLedgerInvariant { get; set; } = true;

    /// <summary>Next to the Talking Island grocer (<see cref="MerchantObjectId"/> at -84204, 240403).</summary>
    public int HubX { get; set; } = -84160;
    public int HubY { get; set; } = 240403;
    public int HubZ { get; set; } = -3720;

    /// <summary>Next to the Talking Island warehouse keeper (<see cref="WarehouseObjectId"/> at -81512, 243424).</summary>
    public int WarehouseX { get; set; } = -81470;
    public int WarehouseY { get; set; } = 243424;
    public int WarehouseZ { get; set; } = -3712;

    /// <summary>Next to the Gludio manor manager (<see cref="ManorManagerObjectId"/> at -12172, 122648).</summary>
    public int GludioManorX { get; set; } = -12130;
    public int GludioManorY { get; set; } = 122648;
    public int GludioManorZ { get; set; } = -3088;
    public int GludioCastleId { get; set; } = 1;

    /// <summary>Talking Island grocer; its buy list 3000400 sells <see cref="ShopBuyItemId"/>.</summary>
    public int MerchantNpcTemplateId { get; set; } = 30004;
    public int MerchantObjectId { get; set; } = 60014;
    public int WarehouseNpcTemplateId { get; set; } = 30005;
    public int WarehouseObjectId { get; set; } = 60010;
    public int ManorManagerTemplateId { get; set; } = 35103;
    public int ManorManagerObjectId { get; set; } = 60774;
    public int ManorSeedItemId { get; set; } = 5016;

    /// <summary>Full Mobius buy list id (<c>Buy 3000400</c> bypass, RequestBuyItem list id).</summary>
    public int ShopBuyListId { get; set; } = 3000400;
    public int ShopBuyItemId { get; set; } = 1060;
    public int ShopBuyCount { get; set; } = 1;

    public int RecipeListId { get; set; } = 1;
    public long ManufactureFee { get; set; } = 50;

    /// <summary>Every Nth bot is created as the dwarf crafter the manufacture flow needs.</summary>
    public int ManufacturerRace { get; set; } = 4;
    public int ManufacturerClassId { get; set; } = 53;
    public int ManufacturerModulo { get; set; } = 6;
    public int ManufacturerRemainder { get; set; } = 0;

    public int TradeAdenaAmount { get; set; } = 100;
    public int WarehouseDepositAdena { get; set; } = 50;

    /// <summary>Per-flow wall-clock budget (issue #910).</summary>
    public int FlowTimeoutSeconds { get; set; } = 8;
}
