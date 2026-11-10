using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class GearRulesTests
{
    [Fact]
    public void Use_owned_and_unequip_slot_empty()
    {
        Assert.Equal(InventoryProtocol.NotOwned, GearRules.UseOwnedError(false));
        Assert.Null(GearRules.UseOwnedError(true));
        Assert.Equal(InventoryProtocol.BadSlot, GearRules.UnequipSlotError(-1, 17));
        Assert.Equal(InventoryProtocol.BadSlot, GearRules.UnequipSlotError(18, 17));
        Assert.Null(GearRules.UnequipSlotError(0, 17));
        Assert.Equal(InventoryProtocol.Empty, GearRules.UnequipEmptyError(false));
        Assert.Null(GearRules.UnequipEmptyError(true));
    }

    [Fact]
    public void Grant_rejects_bad_id_count_and_unknown()
    {
        Assert.Equal("bad_request", GearRules.GrantError(0, 1, 10_000, true));
        Assert.Equal("bad_request", GearRules.GrantError(1, 0, 10_000, true));
        Assert.Equal("bad_request", GearRules.GrantError(1, 10_001, 10_000, true));
        Assert.Equal("bad_request", GearRules.GrantError(1, 1, 10_000, false));
        Assert.Null(GearRules.GrantError(57, 1, 10_000, true));
    }
}
