using Fineage.Zone.Drops;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class GroundRulesTests
{
    [Fact]
    public void Drop_protection_allows_owner_party_or_expired()
    {
        Assert.True(GroundRules.CanPickupProtected(0, 100, 50, pickerObjectId: 9, samePartyAsOwner: false));
        Assert.True(GroundRules.CanPickupProtected(5, 100, 100, pickerObjectId: 9, samePartyAsOwner: false));
        Assert.True(GroundRules.CanPickupProtected(5, 100, 50, pickerObjectId: 5, samePartyAsOwner: false));
        Assert.True(GroundRules.CanPickupProtected(5, 100, 50, pickerObjectId: 9, samePartyAsOwner: true));
        Assert.False(GroundRules.CanPickupProtected(5, 100, 50, pickerObjectId: 9, samePartyAsOwner: false));
    }

    [Fact]
    public void Capacity_skips_herbs_and_honors_stack_slots()
    {
        Assert.True(GroundRules.HasRoomForGroundItem(isHerb: true, inventoryItemCount: 80, race: 0, stackable: false, alreadyCarried: false, count: 1));
        Assert.True(GroundRules.HasRoomForGroundItem(isHerb: false, inventoryItemCount: 79, race: 0, stackable: true, alreadyCarried: true, count: 5));
        Assert.False(GroundRules.HasRoomForGroundItem(isHerb: false, inventoryItemCount: 80, race: 0, stackable: false, alreadyCarried: false, count: 1));
    }
}
