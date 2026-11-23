using Fineage.Accounts;
using Fineage.Game.Services;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.Geo.ZoneTypes;
using Fineage.Zone.Punishment;
using Fineage.Zone.Wedding;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>One zone-type registry loads every Interlude rule zone. Unknown types are logged and skipped.</summary>
public sealed class ZoneTypeRegistryTests
{
    static ZoneTypeRegistry Rules => ZoneTypeRegistry.SharedDefault;

    [Fact]
    public void Datapack_counts_match_the_imported_zone_file()
    {
        Assert.True(Rules.CountOf("NoStoreZone") >= 28);
        Assert.True(Rules.CountOf("NoSummonFriendZone") >= 28);
        Assert.True(Rules.CountOf("NoRestartZone") >= 69);
        Assert.True(Rules.CountOf("TaxZone") >= 122);
        Assert.True(Rules.CountOf("ConditionZone") >= 1080);
        Assert.True(Rules.CountOf("DerbyTrackZone") >= 8);
        Assert.True(Rules.CountOf("RespawnZone") >= 145);
        Assert.True(Rules.CountOf("TownZone") >= 18);
        Assert.True(Rules.CountOf("EffectZone") >= 1);
        Assert.True(Rules.CountOf("ScriptZone") >= 1);
        Assert.True(Rules.CountOf("ResidenceTeleportZone") >= 1);
        Assert.True(Rules.CountOf("ResidenceHallTeleportZone") >= 1);
        Assert.True(Rules.CountOf("BossZone") >= 1);
        Assert.True(Rules.CountOf("CastleZone") >= 1);
        Assert.Empty(Rules.UnknownTypes);
    }

    [Fact]
    public void Unknown_type_is_recorded_and_not_applied()
    {
        var registry = ZoneTypeRegistry.Parse("""
            {"zones":[
              {"type":"NotAZone","name":"ghost","shape":"Cuboid","minZ":0,"maxZ":1,"nodes":[[0,0],[1,1]]},
              {"type":"WaterZone","name":"dup","shape":"Cuboid","minZ":0,"maxZ":1,"nodes":[[0,0],[1,1]]}
            ]}
            """);
        Assert.Equal(["NotAZone"], registry.UnknownTypes);
        Assert.Equal(0, registry.CountOf("WaterZone"));
        Assert.Equal(0, registry.ZoneCount);
    }

    [Fact]
    public void Datapack_points_set_the_rule_for_that_type()
    {
        var elf = Rules.Query(NetTestKit.ElfX, NetTestKit.ElfY, NetTestKit.ElfZ, race: 1);
        Assert.False(elf.BlocksStore);
        Assert.False(elf.BlocksSummon);
        Assert.False(elf.BlocksRestart);
        Assert.Equal("Mothertree_Glade", elf.RespawnPoint);
        Assert.Equal(4, elf.TaxCastleId);

        Assert.True(Rules.Query(81540, 145625, -3475, 0).BlocksStore);
        Assert.True(Rules.Query(-81316, 85917, -5133, 0).BlocksSummon);
        var restart = Rules.Query(51635, 214531, -2565, 0);
        Assert.True(restart.BlocksRestart);
        Assert.Equal(1_800_000, restart.RestartTimeMs);
        Assert.True(Rules.Query(200559, -130845, 14507, 0).BlocksBookmark);
        Assert.True(Rules.Query(18252, 244221, 11456, 0).BlocksItemDrop);
        Assert.True(Rules.Query(11714, 181706, -3552, 0).InDerby);
        Assert.Equal(1, Rules.Query(-119232, 98344, -2036, 0).TaxCastleId);
        Assert.True(Rules.Query(200918, -126815, 1428, 0).InBoss);
        Assert.Equal("Pagans mark", Rules.Query(-16600, -37100, -10728, 0).ScriptZone);
        Assert.Equal(9, Rules.Query(77170, 147550, -3550, 0).TownId);
    }

    [Fact]
    public async Task Private_store_is_refused_inside_a_no_store_zone()
    {
        var kit = new NetTestKit();
        var (seller, player) = await kit.EnterAsync(classId: 0);
        kit.World.TeleportPlayerForTests(seller, 81540, 145625, -3475);
        var offered = player.Inventory!.Items.First(item =>
            !item.IsEquipped && item.Template.CanDropTradeStore && item.ItemId != ShopTrade.AdenaItemId);
        Assert.Equal(PrivateStoreProtocol.NoStore, await kit.World.TrySetPrivateStoreSellAsync(seller, new SetPrivateStoreSellMsg
        {
            Message = "WTS",
            Items = [new PrivateStoreSellRowMsg { InventoryObjectId = offered.ObjectId, Count = 1, Price = 10 }]
        }, default));
        Assert.Equal(0, player.PrivateStoreType);
    }

    [Fact]
    public async Task Village_restart_is_refused_inside_a_no_restart_zone()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        kit.World.TeleportPlayerForTests(session, 51635, 214531, -2565);
        int x = player.X, y = player.Y, z = player.Z;
        Assert.Null(await kit.World.TryAdminKillPlayerAsync(session, default));
        Assert.Equal(DeathProtocol.NoRestart, await kit.World.TryReviveAsync(session, DeathProtocol.ReviveToVillage, default));
        Assert.Equal((x, y, z), (player.X, player.Y, player.Z));
        Assert.True(player.Dead);
    }

    [Fact]
    public async Task No_restart_login_ousts_only_when_offline_and_uptime_both_elapsed()
    {
        var kit = new NetTestKit();
        var who = kit.Create(x: 51635, y: 214531, z: -2565);
        kit.Now = kit.Now.AddSeconds(700);
        var (session, stayed) = await kit.EnterAsync(who);
        Assert.Equal(51635, stayed.X);
        await kit.World.LeaveAsync(session, default);

        Assert.True(kit.Store.TrySetLastAccess(who.Account, who.CharacterId, kit.Now.AddSeconds(-2000)));
        var (_, ousted) = await kit.EnterAsync(who);
        Assert.NotEqual(51635, ousted.X);
    }

    [Fact]
    public async Task Wedding_summon_is_refused_inside_a_no_summon_zone()
    {
        var kit = new NetTestKit();
        var a = kit.Create(sex: 0);
        var b = kit.Create(sex: 1);
        var (sa, pa) = await kit.EnterAsync(a);
        var (sb, pb) = await kit.EnterAsync(b);
        Assert.Null(await kit.World.TryFriendInviteAsync(sa, new FriendInviteMsg { Name = b.Name }, default));
        Assert.Null(await kit.World.TryFriendAnswerAsync(sb, new FriendAnswerMsg { Response = 1 }, default));
        pa.Session!.SelectedTargetId = pb.ObjectId;
        Assert.Null(await kit.World.TryWeddingVoicedAsync(sa, WeddingProtocol.Engage, default));
        Assert.Null(await kit.World.TryWeddingEngageAnswerAsync(sb, 1, default));
        var npc = new ZoneEntity
        {
            Kind = ZoneEntityKind.Npc,
            NpcId = kit.World.WeddingSettingsForTests.ManagerNpcId,
            ObjectId = 8_000_001,
            X = pa.X + 40,
            Y = pa.Y,
            Z = pa.Z
        };
        kit.World.FanoutNewNpcForTests(npc);
        kit.World.PlaceForTests(npc);
        var settings = kit.World.WeddingSettingsForTests;
        Wear(kit, pa);
        Wear(kit, pb);
        pa.Inventory!.Add(ShopTrade.AdenaItemId, checked((int)(settings.WeddingPrice + settings.TeleportPrice)));
        pb.Inventory!.Add(ShopTrade.AdenaItemId, checked((int)settings.WeddingPrice));
        kit.World.WeddingNpcBypassForTests(sa, pa, npc, "ask");
        kit.World.WeddingNpcBypassForTests(sb, pb, npc, "accept");
        Assert.True(pa.Married);

        kit.World.TeleportPlayerForTests(sa, -81316, 85917, -5133);
        Assert.Null(await kit.World.TryWeddingVoicedAsync(sa, WeddingProtocol.GoToLove, default));
        Assert.Equal(0, pa.Intent.WeddingTeleportPartnerId);
        Assert.True(PlayerZoneChecks.IsInGrandBossZone(kit.World, pa.ObjectId) == false);
    }

    [Fact]
    public async Task Grand_boss_membership_follows_an_enabled_boss_zone()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        Assert.False(PlayerZoneChecks.IsInGrandBossZone(kit.World, player.ObjectId));
        kit.World.TeleportPlayerForTests(session, 200918, -126815, 1428);
        Assert.True(PlayerZoneChecks.IsInGrandBossZone(kit.World, player.ObjectId));
    }

    [Fact]
    public async Task No_item_drop_zone_keeps_the_inventory_on_death()
    {
        var rules = DeathDropRules.Parse("""
            {
              "playerDropLimit": 3, "playerRateDrop": 100, "playerRateDropItem": 100,
              "playerRateDropEquip": 0, "playerRateDropEquipWeapon": 0,
              "karmaDropLimit": 10, "karmaRateDrop": 0, "karmaRateDropItem": 0,
              "karmaRateDropEquip": 0, "karmaRateDropEquipWeapon": 0,
              "karmaPkLimit": 6, "nonDroppableItemIds": [57]
            }
            """);
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting",
            deathDrop: rules, deathRandom: new AlwaysLow());
        var mob = kit.World.AttackableForTest()!;
        var (session, player) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        player.Level = 10;
        player.Exp = kit.Game.Classes!.Experience.ExpToBeLevel(10);
        Assert.Null(await kit.World.TryGiveItemAsync(session, 1864, 2, default));
        kit.World.ZoneRules = ZoneTypeRegistry.Parse(
            "{\"zones\":[{\"type\":\"ConditionZone\",\"name\":\"no_drop\",\"shape\":\"Cuboid\",\"minZ\":"
            + (mob.Z - 500) + ",\"maxZ\":" + (mob.Z + 500)
            + ",\"nodes\":[[" + (mob.X - 2000) + "," + (mob.Y - 2000) + "],["
            + (mob.X + 2000) + "," + (mob.Y + 2000) + "]],\"stats\":{\"NoItemDrop\":\"true\"}}]}");
        long before = player.Inventory!.CountOf(1864);
        kit.World.KillPlayerByNpcForTest(player, mob);
        Assert.Equal(before, player.Inventory.CountOf(1864));
        Assert.Equal(0, kit.World.GroundItemCountForTest());
    }

    [Fact]
    public async Task Effect_zone_applies_its_skill_and_marks_the_danger_area()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        kit.World.ZoneRules = ZoneTypeRegistry.Parse("""
            {"zones":[
              {"type":"EffectZone","name":"wind","shape":"Cuboid","minZ":-4000,"maxZ":-3000,
               "nodes":[[45000,40000],[47000,42000]],
               "stats":{"skillIdLvl":"1204-1;","initialDelay":"0","bypassSkillConditions":"true","removeEffectsOnExit":"true"}},
              {"type":"EffectZone","name":"blank","shape":"Cuboid","minZ":-4000,"maxZ":-3000,
               "nodes":[[1,1],[2,2]]},
              {"type":"EffectZone","name":"missing","shape":"Cuboid","minZ":-4000,"maxZ":-3000,
               "nodes":[[45000,40000],[47000,42000]],
               "stats":{"skillIdLvl":"999999-1;","initialDelay":"0","bypassSkillConditions":"true","showDangerIcon":"false"}}
            ]}
            """);
        Assert.Equal(3, kit.World.ZoneRules.CountOf("EffectZone"));
        Assert.Empty(kit.World.ZoneRules.Profile<EffectZoneProfile>().Zones[1].Skills);
        kit.World.TeleportPlayerForTests(session, player.X, player.Y, player.Z);
        Assert.True(kit.World.InDangerArea(player));
        kit.World.TickRuleZonesForTest(kit.Now.ToUnixTimeMilliseconds());
        Assert.Contains(player.Abnormals, a => a.Id == 1204);
        Assert.DoesNotContain(player.Abnormals, a => a.Id == 999999);

        kit.World.TeleportPlayerForTests(session, 0, 0, 0);
        kit.World.TickRuleZonesForTest(kit.Now.ToUnixTimeMilliseconds());
        Assert.False(kit.World.InDangerArea(player));
        Assert.DoesNotContain(player.Abnormals, a => a.Id == 1204);
    }

    [Fact]
    public async Task Residence_hall_ousts_after_thirty_seconds_only_while_the_siege_runs()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        int hallId = kit.World.ChSieges.Halls[0].Id;
        kit.World.ZoneRules = ZoneTypeRegistry.Parse(
            "{\"zones\":[{\"type\":\"ResidenceHallTeleportZone\",\"name\":\"hall\",\"shape\":\"Cuboid\",\"minZ\":-4000,\"maxZ\":-2000,"
            + "\"nodes\":[[45000,40000],[47000,42000]],\"stats\":{\"residenceId\":\"" + hallId
            + "\"},\"spawns\":[{\"x\":10,\"y\":20,\"z\":-30}]}]}");
        long now = kit.Now.ToUnixTimeMilliseconds();
        kit.World.TickRuleZonesForTest(now);
        kit.World.TickRuleZonesForTest(now + 31_000);
        Assert.Equal(NetTestKit.ElfX, player.X);

        kit.World.ChSieges.Halls[0].InProgress = true;
        kit.World.TickRuleZonesForTest(now + 32_000);
        kit.World.TickRuleZonesForTest(now + 63_000);
        Assert.Equal((10, 20, -30), (player.X, player.Y, player.Z));
        Assert.NotNull(session);
    }

    [Fact]
    public void Bookmark_use_is_refused_inside_a_no_bookmark_zone()
    {
        var kit = new NetTestKit();
        var player = new ZoneEntity { Kind = ZoneEntityKind.Player, X = 200559, Y = -130845, Z = 14507, Race = 0 };
        Assert.Equal("bookmark_zone", kit.World.BookmarkUseError(player));
        player.X = NetTestKit.ElfX;
        player.Y = NetTestKit.ElfY;
        player.Z = NetTestKit.ElfZ;
        Assert.Null(kit.World.BookmarkUseError(player));
    }

    static void Wear(NetTestKit kit, ZoneEntity player)
    {
        var row = player.Inventory!.Add(kit.World.WeddingSettingsForTests.FormalWearItemId, 1);
        player.Inventory.Equip(row.ObjectId);
    }

    sealed class AlwaysLow : Random
    {
        public override int Next(int maxValue) => 0;
    }
}
