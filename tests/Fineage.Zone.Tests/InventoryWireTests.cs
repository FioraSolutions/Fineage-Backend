using Fineage.Game;
using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class InventoryWireTests
{
    [Fact]
    public void Inventory_update_roundtrips_an_equip_change()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(ClassSpawn.HumanFighter, firstObjectId: 1);
        var sword = inventory.Items.Single(item => item.ItemId == 219);
        var falchion = inventory.Items.Single(item => item.ItemId == 68);
        inventory.Equip(falchion);

        var update = InventoryMessages.Update(InventoryChange.Modify, falchion);
        byte[] body = MessagePackSerializer.Serialize(update);
        var back = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(body);

        Assert.Equal(1015, (int)MsgType.InventoryUpdate);
        var change = Assert.Single(back.Changes);
        Assert.Equal(InventoryChange.Modify, change.Change);
        Assert.Equal(falchion.ObjectId, change.ObjectId);
        Assert.Equal(219, sword.ItemId);
        Assert.Equal(68, change.ItemId);
        Assert.Equal(1, change.Count);
        Assert.Equal(7, change.EquippedSlot);
        Assert.Equal("rhand", change.BodySlot);
        Assert.Equal("weapon", change.Kind);
        Assert.Equal("icon.weapon_falchion_i00", change.Icon);
        Assert.Equal("sword", change.WeaponType);
        Assert.Equal("", change.ArmorType);
        Assert.Null(sword.EquippedSlot);
    }

    [Fact]
    public void Item_list_marks_the_starter_weapon_equipped_in_the_right_hand()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(0);
        var list = InventoryMessages.ItemList(inventory);
        var sword = Assert.Single(list.Items, item => item.ItemId == 219);
        Assert.Equal(7, sword.EquippedSlot);
        Assert.Equal("rhand", sword.BodySlot);
        Assert.Equal("weapon", sword.Kind);
        Assert.Equal("dagger", sword.WeaponType);
        Assert.Equal("icon.weapon_sword_breaker_i00", sword.Icon);

        var dice = Assert.Single(list.Items, item => item.ItemId == 4627);
        Assert.Equal(-1, dice.EquippedSlot);
        Assert.Equal("etc", dice.Kind);
        Assert.Equal("none", dice.BodySlot);

        var slots = InventoryMessages.Paperdoll(inventory);
        Assert.Equal(17, slots.Length);
        Assert.Equal(sword.ObjectId, slots[7].ObjectId);
        Assert.Equal(219, slots[7].ItemId);
        Assert.Equal(0, slots[8].ItemId);
    }
}
