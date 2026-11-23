namespace Fineage.LoadTest.Models;

/// <summary>Ti Village economy hub defaults (Mobius spawn object ids from <c>data/spawns/ti_village.json</c>).</summary>
public sealed class EconomyHammerConfig
{
    public const int AdenaItemId = 57;

    public bool Enabled { get; set; }

    /// <summary>Fail the run when ledger invariants, unexplained rejects, or economy log errors appear.</summary>
    public bool RequireLedgerInvariant { get; set; } = true;

    public int HubX { get; set; } = -86300;
    public int HubY { get; set; } = 243250;
    public int HubZ { get; set; } = -3717;

    public int GludioManorX { get; set; } = -14288;
    public int GludioManorY { get; set; } = 122752;
    public int GludioManorZ { get; set; } = -3000;
    public int GludioCastleId { get; set; } = 1;

    public int MerchantNpcTemplateId { get; set; } = 30001;
    public int MerchantObjectId { get; set; } = 60023;
    public int WarehouseNpcTemplateId { get; set; } = 30005;
    public int WarehouseObjectId { get; set; } = 60010;
    public int ManorManagerTemplateId { get; set; } = 35103;
    public int ManorManagerObjectId { get; set; } = 60774;
    public int ManorSeedItemId { get; set; } = 5016;

    public int ShopBuyListId { get; set; } = 1;
    public int ShopBuyItemId { get; set; } = 1060;
    public int ShopBuyCount { get; set; } = 1;

    public int RecipeListId { get; set; } = 1;
    public long ManufactureFee { get; set; } = 50;

    public int ManufacturerRace { get; set; } = 4;
    public int ManufacturerClassId { get; set; } = 53;
    public int ManufacturerModulo { get; set; } = 6;
    public int ManufacturerRemainder { get; set; } = 0;

    public int TradeAdenaAmount { get; set; } = 100;
    public int WarehouseDepositAdena { get; set; } = 50;

    /// <summary>Per-flow wall-clock budget (issue #910).</summary>
    public int FlowTimeoutSeconds { get; set; } = 8;
}