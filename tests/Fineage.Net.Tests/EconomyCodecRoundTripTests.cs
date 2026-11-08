using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// Encode/decode round-trips for inventory / combat / warehouse messages (#353 NetCodec + B6 economy track).
/// </summary>
public sealed class EconomyCodecRoundTripTests
{
    [Fact]
    public void InventoryUpdate_and_DropItem_roundtrip()
    {
        var update = NetCodec.DecodeInventoryUpdate(NetCodec.EncodeInventoryUpdate(new InventoryUpdateNetMsg
        {
            Changes =
            [
                new InventoryUpdateEntryNet
                {
                    Change = 1,
                    ObjectId = 44,
                    ItemId = 57,
                    Count = 100,
                    EnchantLevel = 0,
                    EquippedSlot = -1
                },
                new InventoryUpdateEntryNet
                {
                    Change = 2,
                    ObjectId = 45,
                    ItemId = 736,
                    Count = 0,
                    EnchantLevel = 3,
                    EquippedSlot = -1
                }
            ]
        }));
        Assert.Equal(2, update.Changes!.Length);
        Assert.Equal(57, update.Changes[0].ItemId);
        Assert.Equal(100, update.Changes[0].Count);
        Assert.Equal(736, update.Changes[1].ItemId);
        Assert.Equal(0, update.Changes[1].Count);

        var drop = NetCodec.DecodeDropItem(NetCodec.EncodeDropItem(new DropItemNetMsg
        {
            DropperObjectId = 9,
            EntityValue = 88,
            EntityGeneration = 2,
            ItemId = 57,
            X = 1,
            Y = 2,
            Z = 3,
            Stackable = true,
            Count = 25
        }));
        Assert.Equal(9, drop.DropperObjectId);
        Assert.Equal(88uL, drop.EntityValue);
        Assert.Equal(57, drop.ItemId);
        Assert.True(drop.Stackable);
        Assert.Equal(25, drop.Count);
    }

    [Fact]
    public void AttackState_StatusUpdate_and_EntityDie_roundtrip()
    {
        var attack = NetCodec.DecodeAttackState(NetCodec.EncodeAttackState(new AttackStateMsg
        {
            AttackerValue = 1,
            AttackerGeneration = 2,
            TargetValue = 3,
            TargetGeneration = 4,
            Damage = 55,
            Flags = 1,
            AttackerX = 10,
            AttackerY = 20,
            AttackerZ = 30,
            TargetX = 40,
            TargetY = 50,
            TargetZ = 60
        }));
        Assert.Equal(1uL, attack.AttackerValue);
        Assert.Equal(3uL, attack.TargetValue);
        Assert.Equal(55, attack.Damage);
        Assert.Equal(1, attack.Flags);
        Assert.Equal(60, attack.TargetZ);

        var status = NetCodec.DecodeStatusUpdate(NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = 11,
            EntityGeneration = 1,
            AttributeIds = [9, 10],
            Values = [100, 200]
        }));
        Assert.Equal(11uL, status.EntityValue);
        Assert.Equal(new[] { 9, 10 }, status.AttributeIds);
        Assert.Equal(new[] { 100, 200 }, status.Values);

        var die = NetCodec.DecodeEntityDie(NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = 7,
            EntityGeneration = 3,
            KillerObjectId = 99
        }));
        Assert.Equal(7uL, die.EntityValue);
        Assert.Equal(99, die.KillerObjectId);
    }

    [Fact]
    public void Warehouse_deposit_and_withdraw_lists_roundtrip()
    {
        var deposit = NetCodec.DecodeWarehouseDepositList(NetCodec.EncodeWarehouseDepositList(
            new WarehouseDepositListNetMsg
            {
                NpcObjectId = 61001,
                FeePerLine = 30,
                SlotsUsed = 2,
                SlotsMax = 100,
                Adena = 170,
                Items =
                [
                    new ItemListEntryNet
                    {
                        ObjectId = 5,
                        ItemId = 736,
                        Count = 10,
                        EnchantLevel = 0,
                        EquippedSlot = -1
                    }
                ]
            }));
        Assert.Equal(61001, deposit.NpcObjectId);
        Assert.Equal(30, deposit.FeePerLine);
        Assert.Equal(170, deposit.Adena);
        Assert.Equal(736, Assert.Single(deposit.Items!).ItemId);

        var withdraw = NetCodec.DecodeWarehouseWithdrawList(NetCodec.EncodeWarehouseWithdrawList(
            new WarehouseWithdrawListNetMsg
            {
                NpcObjectId = 61001,
                SlotsUsed = 1,
                SlotsMax = 100,
                InventorySlotsUsed = 3,
                InventorySlotsMax = 80,
                CurrentWeight = 1000,
                MaxWeight = 69000,
                Items =
                [
                    new ItemListEntryNet
                    {
                        ObjectId = 9,
                        ItemId = 57,
                        Count = 50,
                        EnchantLevel = 0,
                        EquippedSlot = -1
                    }
                ]
            }));
        Assert.Equal(1000, withdraw.CurrentWeight);
        Assert.Equal(57, Assert.Single(withdraw.Items!).ItemId);
        Assert.Equal(50, withdraw.Items![0].Count);

        var req = NetCodec.DecodeWarehouseDepositRequest(NetCodec.EncodeWarehouseDepositRequest(
            new WarehouseDepositRequestNetMsg
            {
                NpcObjectId = 61001,
                Lines = [new ShopObjectCountNet { ObjectId = 5, Count = 10 }]
            }));
        Assert.Equal(61001, req.NpcObjectId);
        Assert.Equal(5, Assert.Single(req.Lines!).ObjectId);
        Assert.Equal(10, req.Lines![0].Count);
    }
}
