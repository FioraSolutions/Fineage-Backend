using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NetPresenceHubTests
{
    [Fact]
    public void Remote_spawn_and_despawn_codec_round_trip()
    {
        var entity = new ZoneEntity
        {
            ObjectId = 7,
            Kind = ZoneEntityKind.Player,
            Name = "Peer",
            ClassId = 0,
            Race = 0,
            Sex = 0,
            X = 10,
            Y = 20,
            Z = 30,
            RunSpeed = 120,
            WalkSpeed = 80,
            Running = true
        };
        var spawn = NetEnterWorldProjector.BuildRemoteSpawn(entity, EntityId.FromObjectId(7));
        Assert.False(spawn.IsSelf);
        Assert.Equal("Peer", spawn.Name);
        Assert.Equal(10, spawn.X);
        Assert.Equal(EntityKindNet.Player, spawn.Kind);

        var again = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(spawn));
        Assert.False(again.IsSelf);
        Assert.Equal(spawn.EntityValue, again.EntityValue);

        var despawn = new EntityDespawnMsg
        {
            EntityValue = spawn.EntityValue,
            EntityGeneration = spawn.EntityGeneration
        };
        var round = NetCodec.DecodeEntityDespawn(NetCodec.EncodeEntityDespawn(despawn));
        Assert.Equal(despawn.EntityValue, round.EntityValue);
        Assert.Equal(despawn.EntityGeneration, round.EntityGeneration);
    }

    [Fact]
    public void Hub_register_lookup_unregister()
    {
        var hub = new NetPresenceHub();
        Assert.False(hub.TryGet(1, out _, out _));
        // Without a live socket we only assert Unregister is safe.
        hub.Unregister(1);
        Assert.False(hub.TryGet(1, out _, out _));
    }

    [Fact]
    public void Warehouse_codec_round_trip()
    {
        var deposit = new WarehouseDepositListNetMsg
        {
            NpcObjectId = 1,
            FeePerLine = 30,
            Adena = 100,
            Items = [new ItemListEntryNet { ObjectId = 9, ItemId = 57, Count = 5, EnchantLevel = 0, EquippedSlot = -1 }]
        };
        var depositAgain = NetCodec.DecodeWarehouseDepositList(NetCodec.EncodeWarehouseDepositList(deposit));
        Assert.Equal(30, depositAgain.FeePerLine);
        Assert.Equal(57, depositAgain.Items[0].ItemId);

        var withdraw = new WarehouseWithdrawListNetMsg
        {
            NpcObjectId = 1,
            CurrentWeight = 10,
            MaxWeight = 100,
            Items = [new ItemListEntryNet { ObjectId = 3, ItemId = 1, Count = 1, EquippedSlot = -1 }]
        };
        Assert.Equal(10, NetCodec.DecodeWarehouseWithdrawList(NetCodec.EncodeWarehouseWithdrawList(withdraw)).CurrentWeight);

        var req = new WarehouseDepositRequestNetMsg
        {
            NpcObjectId = 1,
            Lines = [new ShopObjectCountNet { ObjectId = 9, Count = 2 }]
        };
        Assert.Equal(9, NetCodec.DecodeWarehouseDepositRequest(NetCodec.EncodeWarehouseDepositRequest(req)).Lines[0].ObjectId);
        Assert.Equal(2, NetCodec.DecodeWarehouseWithdrawRequest(NetCodec.EncodeWarehouseWithdrawRequest(
            new WarehouseWithdrawRequestNetMsg { NpcObjectId = 1, Lines = req.Lines })).Lines[0].Count);
    }

    [Fact]
    public void Shop_codec_round_trip()
    {
        var buy = new BuyListNetMsg
        {
            NpcObjectId = 1,
            ListId = 3001000,
            Items = [new BuyListItemNet { ItemId = 57, Price = 1, Count = -1 }]
        };
        var buyAgain = NetCodec.DecodeBuyList(NetCodec.EncodeBuyList(buy));
        Assert.Equal(3001000, buyAgain.ListId);
        Assert.Single(buyAgain.Items);

        var sell = new SellListNetMsg
        {
            NpcObjectId = 1,
            Items = [new SellListItemNet { ObjectId = 9, ItemId = 1, Count = 2, Price = 3, EnchantLevel = 0 }]
        };
        Assert.Equal(9, NetCodec.DecodeSellList(NetCodec.EncodeSellList(sell)).Items[0].ObjectId);

        var multi = new MultiSellListNetMsg
        {
            NpcObjectId = 1,
            ListId = 1,
            Entries =
            [
                new MultiSellEntryNet
                {
                    EntryId = 5,
                    Ingredients = [new MultiSellPartNet { ItemId = 57, Count = 10 }],
                    Products = [new MultiSellPartNet { ItemId = 17, Count = 1 }]
                }
            ]
        };
        var multiAgain = NetCodec.DecodeMultiSellList(NetCodec.EncodeMultiSellList(multi));
        Assert.Equal(5, multiAgain.Entries[0].EntryId);
        Assert.Equal(57, multiAgain.Entries[0].Ingredients[0].ItemId);

        Assert.Equal(100, NetCodec.DecodeShopBalance(NetCodec.EncodeShopBalance(new ShopBalanceNetMsg { Adena = 100 })).Adena);
        Assert.Equal(7, NetCodec.DecodeRequestBuyItem(NetCodec.EncodeRequestBuyItem(new RequestBuyItemNetMsg
        {
            NpcObjectId = 1,
            ListId = 2,
            Items = [new ShopCountNet { ItemId = 7, Count = 3 }]
        })).Items[0].ItemId);
    }

    [Fact]
    public void Npc_talk_bypass_html_codec_round_trip()
    {
        Assert.Equal(9, NetCodec.DecodeNpcTalk(NetCodec.EncodeNpcTalk(new NpcTalkNetMsg { NpcObjectId = 9 })).NpcObjectId);

        var bypass = new BypassRequestNetMsg { NpcObjectId = 9, Command = "Chat 1" };
        var bypassAgain = NetCodec.DecodeBypassRequest(NetCodec.EncodeBypassRequest(bypass));
        Assert.Equal("Chat 1", bypassAgain.Command);

        var html = new NpcHtmlNetMsg { NpcObjectId = 9, NpcId = 30006, Html = "<html/>", ItemId = 0 };
        var htmlAgain = NetCodec.DecodeNpcHtml(NetCodec.EncodeNpcHtml(html));
        Assert.Equal(30006, htmlAgain.NpcId);
        Assert.Equal("<html/>", htmlAgain.Html);

        var spawn = new EntitySpawnMsg
        {
            EntityValue = 1,
            EntityGeneration = 1,
            Kind = EntityKindNet.Npc,
            Name = "Guide",
            TemplateId = 30006,
            Attackable = false,
            Running = true
        };
        var spawnAgain = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(spawn));
        Assert.False(spawnAgain.Attackable);
        Assert.Equal(30006, spawnAgain.TemplateId);
    }

    [Fact]
    public void Abnormal_status_update_codec_round_trip()
    {
        var update = new AbnormalStatusUpdateNetMsg
        {
            ObjectId = 7,
            Effects =
            [
                new AbnormalEffectNet { Id = 1204, Value = 1, DurationSeconds = 1200 }
            ]
        };
        var again = NetCodec.DecodeAbnormalStatusUpdate(NetCodec.EncodeAbnormalStatusUpdate(update));
        Assert.Equal(7, again.ObjectId);
        Assert.Single(again.Effects);
        Assert.Equal(1204, again.Effects[0].Id);
        Assert.Equal(1200, again.Effects[0].DurationSeconds);
    }

    [Fact]
    public void Revive_and_teleport_codec_round_trip()
    {
        Assert.Equal(0, NetCodec.DecodeReviveRequest(NetCodec.EncodeReviveRequest(new ReviveRequestNetMsg { Action = 0 })).Action);
        Assert.Equal(42, NetCodec.DecodeRevive(NetCodec.EncodeRevive(new ReviveNetMsg { ObjectId = 42 })).ObjectId);

        var tp = new TeleportToLocationNetMsg { ObjectId = 42, X = 1, Y = 2, Z = 3, Heading = 90 };
        var tpAgain = NetCodec.DecodeTeleportToLocation(NetCodec.EncodeTeleportToLocation(tp));
        Assert.Equal(1, tpAgain.X);
        Assert.Equal(2, tpAgain.Y);
        Assert.Equal(3, tpAgain.Z);
        Assert.Equal(90, tpAgain.Heading);
    }

    [Fact]
    public void Use_skill_and_magic_skill_use_codec_round_trip()
    {
        var use = new UseSkillNetMsg { SkillId = 1147, TargetObjectId = 9, CtrlPressed = true };
        var useAgain = NetCodec.DecodeUseSkill(NetCodec.EncodeUseSkill(use));
        Assert.Equal(1147, useAgain.SkillId);
        Assert.Equal(9, useAgain.TargetObjectId);
        Assert.True(useAgain.CtrlPressed);

        var cast = new MagicSkillUseNetMsg
        {
            CasterObjectId = 1,
            TargetObjectId = 9,
            SkillId = 1147,
            SkillLevel = 1,
            HitTimeMs = 1200,
            ReuseDelayMs = 3000
        };
        var castAgain = NetCodec.DecodeMagicSkillUse(NetCodec.EncodeMagicSkillUse(cast));
        Assert.Equal(1147, castAgain.SkillId);
        Assert.Equal(1200, castAgain.HitTimeMs);
        Assert.Equal(3000, castAgain.ReuseDelayMs);
    }

    [Fact]
    public void Say_and_system_message_codec_round_trip()
    {
        var say = new SayNetMsg { Text = "hi", ChatType = 1 };
        var sayAgain = NetCodec.DecodeSay(NetCodec.EncodeSay(say));
        Assert.Equal("hi", sayAgain.Text);
        Assert.Equal(1, sayAgain.ChatType);

        var creature = new CreatureSayNetMsg { ObjectId = 3, Name = "Aria", Text = "yo", ChatType = 0 };
        var creatureAgain = NetCodec.DecodeCreatureSay(NetCodec.EncodeCreatureSay(creature));
        Assert.Equal("Aria", creatureAgain.Name);
        Assert.Equal("yo", creatureAgain.Text);

        var sys = new SystemMessageNetMsg { Id = 1, Text = "Welcome, Aria.", Params = ["Aria"] };
        var sysAgain = NetCodec.DecodeSystemMessage(NetCodec.EncodeSystemMessage(sys));
        Assert.Equal(1, sysAgain.Id);
        Assert.Equal("Welcome, Aria.", sysAgain.Text);
        Assert.Single(sysAgain.Params);
    }

    [Fact]
    public void Drop_use_unequip_codec_round_trip()
    {
        var drop = new DropItemRequestNetMsg { ObjectId = 5, Count = 2, X = 1, Y = 2, Z = 3 };
        var dropAgain = NetCodec.DecodeDropItemRequest(NetCodec.EncodeDropItemRequest(drop));
        Assert.Equal(5, dropAgain.ObjectId);
        Assert.Equal(2, dropAgain.Count);

        Assert.Equal(9, NetCodec.DecodeUseItem(NetCodec.EncodeUseItem(new UseItemNetMsg { ObjectId = 9 })).ObjectId);
        Assert.Equal(7, NetCodec.DecodeUnequipItem(NetCodec.EncodeUnequipItem(new UnequipItemNetMsg { PaperdollSlot = 7 })).PaperdollSlot);
    }

    [Fact]
    public void Pickup_inventory_update_codec_round_trip()
    {
        var update = new InventoryUpdateNetMsg
        {
            Changes =
            [
                new InventoryUpdateEntryNet
                {
                    Change = 0,
                    ObjectId = 11,
                    ItemId = 57,
                    Count = 40,
                    EnchantLevel = 0,
                    EquippedSlot = -1
                }
            ]
        };
        var again = NetCodec.DecodeInventoryUpdate(NetCodec.EncodeInventoryUpdate(update));
        Assert.Single(again.Changes);
        Assert.Equal(57, again.Changes[0].ItemId);
        Assert.Equal(40, again.Changes[0].Count);

        var pickup = new PickupRequestNetMsg { GroundObjectId = 99 };
        Assert.Equal(99, NetCodec.DecodePickupRequest(NetCodec.EncodePickupRequest(pickup)).GroundObjectId);
    }

    [Fact]
    public void Die_and_drop_codec_round_trip()
    {
        var die = new EntityDieMsg { EntityValue = 9001, EntityGeneration = 1, KillerObjectId = 7 };
        var dieAgain = NetCodec.DecodeEntityDie(NetCodec.EncodeEntityDie(die));
        Assert.Equal(9001ul, dieAgain.EntityValue);
        Assert.Equal(7, dieAgain.KillerObjectId);

        var drop = new DropItemNetMsg
        {
            DropperObjectId = 7,
            EntityValue = 55,
            EntityGeneration = 1,
            ItemId = 57,
            X = 1,
            Y = 2,
            Z = 3,
            Stackable = true,
            Count = 40
        };
        var dropAgain = NetCodec.DecodeDropItem(NetCodec.EncodeDropItem(drop));
        Assert.Equal(57, dropAgain.ItemId);
        Assert.Equal(40, dropAgain.Count);
        Assert.True(dropAgain.Stackable);
    }

    [Fact]
    public void Attack_and_status_codec_round_trip()
    {
        var attack = new AttackStateMsg
        {
            AttackerValue = 1,
            AttackerGeneration = 1,
            TargetValue = 9001,
            TargetGeneration = 1,
            Damage = 12,
            Flags = 0,
            AttackerX = 1,
            AttackerY = 2,
            AttackerZ = 3,
            TargetX = 4,
            TargetY = 5,
            TargetZ = 6
        };
        var again = NetCodec.DecodeAttackState(NetCodec.EncodeAttackState(attack));
        Assert.Equal(12, again.Damage);
        Assert.Equal(9001ul, again.TargetValue);

        var status = new StatusUpdateNetMsg
        {
            EntityValue = 9001,
            EntityGeneration = 1,
            AttributeIds = [9, 10],
            Values = [80, 100]
        };
        var statusAgain = NetCodec.DecodeStatusUpdate(NetCodec.EncodeStatusUpdate(status));
        Assert.Equal(2, statusAgain.AttributeIds.Length);
        Assert.Equal(80, statusAgain.Values[0]);
        Assert.Equal(100, statusAgain.Values[1]);
    }

    [Fact]
    public void Npc_presence_spawn_uses_npc_kind_and_template()
    {
        var entity = new ZoneEntity
        {
            ObjectId = 9001,
            Kind = ZoneEntityKind.Npc,
            NpcId = 4416,
            Name = "Gremlin",
            X = 100,
            Y = 200,
            Z = -3000,
            RunSpeed = 110,
            WalkSpeed = 50,
            Running = true,
            Attackable = true
        };
        var spawn = NetEnterWorldProjector.BuildNpcSpawn(entity, EntityId.FromObjectId(9001));
        Assert.Equal(EntityKindNet.Npc, spawn.Kind);
        Assert.False(spawn.IsSelf);
        Assert.Equal(4416, spawn.TemplateId);
        Assert.Equal("Gremlin", spawn.Name);

        var viaPresence = NetEnterWorldProjector.BuildPresenceSpawn(entity, EntityId.FromObjectId(9001));
        Assert.Equal(EntityKindNet.Npc, viaPresence.Kind);
        Assert.Equal(4416, viaPresence.TemplateId);

        var round = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(spawn));
        Assert.Equal(EntityKindNet.Npc, round.Kind);
        Assert.Equal(4416, round.TemplateId);
    }
}
