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
        var sword = inventory.Items.Single(item => item.ItemId == 2369);
        var dagger = inventory.Items.Single(item => item.ItemId == 10);
        inventory.Equip(dagger);

        var update = InventoryMessages.Update(InventoryChange.Modify, dagger);
        byte[] body = MessagePackSerializer.Serialize(update);
        var back = MessagePackSerializer.Deserialize<InventoryUpdateMsg>(body);

        Assert.Equal(1015, (int)MsgType.InventoryUpdate);
        var change = Assert.Single(back.Changes);
        Assert.Equal(InventoryChange.Modify, change.Change);
        Assert.Equal(dagger.ObjectId, change.ObjectId);
        Assert.Equal(2369, sword.ItemId);
        Assert.Equal(10, change.ItemId);
        Assert.Equal(1, change.Count);
        Assert.Equal(7, change.EquippedSlot);
        Assert.Equal("rhand", change.BodySlot);
        Assert.Equal("weapon", change.Kind);
        Assert.Equal("icon.weapon_dagger_i00", change.Icon);
        Assert.Equal("dagger", change.WeaponType);
        Assert.Equal("", change.ArmorType);
        Assert.Null(sword.EquippedSlot);
    }

    [Fact]
    public void Item_list_marks_the_squire_sword_equipped_in_the_right_hand()
    {
        var inventory = GameData.LoadDefault().CreateStarterInventory(0);
        var list = InventoryMessages.ItemList(inventory);
        var sword = Assert.Single(list.Items, item => item.ItemId == 2369);
        Assert.Equal(7, sword.EquippedSlot);
        Assert.Equal("rhand", sword.BodySlot);
        Assert.Equal("weapon", sword.Kind);
        Assert.Equal("sword", sword.WeaponType);
        Assert.Equal("icon.weapon_squires_sword_i00", sword.Icon);

        var guide = Assert.Single(list.Items, item => item.ItemId == 5588);
        Assert.Equal(-1, guide.EquippedSlot);
        Assert.Equal("etc", guide.Kind);
        Assert.Equal("none", guide.BodySlot);

        var slots = InventoryMessages.Paperdoll(inventory);
        Assert.Equal(17, slots.Length);
        Assert.Equal(sword.ObjectId, slots[7].ObjectId);
        Assert.Equal(2369, slots[7].ItemId);
        Assert.Equal(0, slots[8].ItemId);
    }
}
