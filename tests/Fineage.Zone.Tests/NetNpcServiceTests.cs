using Fineage.Game.Services;
using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Spawns;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The gatekeeper and warehouse keeper services: the list pages, the adena fare with the Monday discount, the
/// loaded-map gate, and the private warehouse fee/weight/persistence rules. Ported off the V2 wire; the world is
/// driven through <see cref="WorldBench"/> and every result read from the recorded Net fanout.
/// </summary>
public sealed class NetNpcServiceTests
{
    const int GateObjectId = 61000;
    const int KeeperObjectId = 61001;
    const int Adena = 57;
    const int Scroll = 736;

    [Fact]
    public void Loaded_zone_keeps_talking_island_spots_and_rejects_gludin()
    {
        var village = SpawnCatalog.LoadDefault().GetMap(SpawnCatalog.TalkingIslandVillageMapId);
        var anchors = village.Npcs.Select(npc => (npc.X, npc.Y)).ToArray();
        Assert.True(NpcServiceProtocol.InLoadedZone(-99586, 237637, anchors));
        Assert.True(NpcServiceProtocol.InLoadedZone(-112367, 234703, anchors));
        Assert.True(NpcServiceProtocol.InLoadedZone(-111728, 244330, anchors));
        Assert.False(NpcServiceProtocol.InLoadedZone(-80749, 149834, anchors));
    }

    [Fact]
    public async Task Free_teleport_moves_inside_the_loaded_zone_and_refuses_the_rest()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gate", "Rook", x: 0, y: 0, z: 0);

        // The gatekeeper page carries the destinations and their fares as bypass links; Net has no separate list
        // opcode, so the HTML is the whole client-facing surface here.
        Assert.Null(await actor.BypassAsync(GateObjectId, "showTeleports"));
        string page = LastHtml(bench);
        Assert.Contains("Near", page, StringComparison.Ordinal);
        Assert.Contains($"npc_{GateObjectId}_teleport NORMAL 0", page, StringComparison.Ordinal);
        Assert.DoesNotContain("1000 Adena", page, StringComparison.Ordinal);

        Assert.Null(await actor.Teleport(GateObjectId, "NORMAL", 0));
        var moved = Assert.IsType<TeleportToLocationNetMsg>(Last(bench, FanoutTeleportToLocation)[1]);
        Assert.Equal(100, moved.X);
        Assert.Equal(0, moved.Y);
        Assert.Equal(NpcServiceProtocol.Teleported, LastSystemMessageId(bench));
        Assert.Equal(100, actor.Session.X);

        // A destination outside the loaded maps is refused, and the pawn does not move.
        Assert.Null(await actor.BypassAsync(GateObjectId, "goto 1"));
        Assert.Equal(NpcServiceProtocol.DestinationUnloaded, LastSystemMessageId(bench));
        Assert.Equal(100, actor.Session.X);

        // Noblesse teleport is still deferred; the gatekeeper says so instead of moving anyone.
        Assert.Null(await actor.BypassAsync(GateObjectId, "showTeleports NOBLES_ADENA"));
        Assert.Equal(NpcServiceProtocol.NoblesDeferred, LastSystemMessageId(bench));
    }

    [Fact]
    public async Task Paid_teleport_takes_adena_and_the_monday_discount()
    {
        // Monday 21:00 UTC is inside the Mobius discount window.
        var when = new DateTimeOffset(2026, 10, 26, 21, 0, 0, TimeSpan.Zero);
        var bench = Open(clock: () => when);
        BenchPlayer actor = await bench.EnterAsync(
            "fare", "Fare",
            items: [new StoredItem(Adena, 5000, -1)],
            level: 41,
            x: 0, y: 0, z: 0);

        Assert.Null(await actor.Teleport(GateObjectId, "NORMAL", 0));

        var update = Assert.IsType<InventoryUpdateNetMsg>(Last(bench, FanoutInventoryUpdate)[1]);
        var adena = Assert.Single(update.Changes);
        Assert.Equal(Adena, adena.ItemId);
        Assert.Equal(4500, adena.Count);
        Assert.Equal(100, Assert.IsType<TeleportToLocationNetMsg>(Last(bench, FanoutTeleportToLocation)[1]).X);
    }

    [Fact]
    public async Task Newbie_helper_buffs_fighters_and_mages_and_buffs_a_second_character_too()
    {
        var bench = Open();
        BenchPlayer fighter = await bench.EnterAsync(
            "buffs", "Fighter", classId: HumanClasses.Fighter, level: 16, x: 0, y: 0, z: 0);
        Assert.True(fighter.SavedRecord().Newbie);

        Assert.Null(await fighter.SupportMagic(GateObjectId));
        var effects = Assert.IsType<AbnormalStatusUpdateNetMsg>(Last(bench, FanoutAbnormalStatusUpdate)[1]).Effects;
        Assert.Contains(effects, effect => effect.Id == 4322 && effect.Value == 3 && effect.DurationSeconds == 3600);
        Assert.Contains(effects, effect => effect.Id == 4327);
        Assert.DoesNotContain(effects, effect => effect.Id == 4331);

        var casts = bench.Rec.Of(FanoutMagicSkillUse)
            .Select(args => ((MagicSkillUseNetMsg)args[1]).SkillId)
            .ToArray();
        Assert.Contains(4322, casts);
        Assert.Contains(4327, casts);

        // A mage on the same world gets its own set, mage buffs included.
        bench.Rec.Calls.Clear();
        BenchPlayer mage = await bench.EnterAsync(
            "buffs", "Mage", classId: HumanClasses.Mystic, level: 16, x: 0, y: 0, z: 0);
        Assert.False(mage.SavedRecord().Newbie);

        // Mobius CT0 SupportMagic has no isNewbie check: a non-newbie character is buffed too.
        Assert.Null(await mage.SupportMagic(GateObjectId));
        var mageCasts = bench.Rec.Of(FanoutMagicSkillUse)
            .Select(args => ((MagicSkillUseNetMsg)args[1]).SkillId)
            .ToArray();
        Assert.Contains(4331, mageCasts);

        bench.Rec.Calls.Clear();
        Assert.Null(await mage.GiveBlessing(GateObjectId));
        Assert.Contains(5182, bench.Rec.Of(FanoutMagicSkillUse)
            .Select(args => ((MagicSkillUseNetMsg)args[1]).SkillId));
    }

    [Fact]
    public async Task Support_magic_is_refused_for_a_character_above_the_newbie_band()
    {
        var bench = Open();
        BenchPlayer veteran = await bench.EnterAsync(
            "guide", "Veteran", classId: HumanClasses.Fighter, level: 60, x: 0, y: 0, z: 0);

        Assert.Null(await veteran.SupportMagic(GateObjectId));
        Assert.Equal(NpcServiceProtocol.BuffLevelHigh, LastSystemMessageId(bench));
        Assert.Empty(bench.Rec.Of(FanoutMagicSkillUse));
        Assert.Empty(bench.Rec.Of(FanoutAbnormalStatusUpdate));
    }

    [Fact]
    public async Task Private_warehouse_charges_the_fee_persists_and_checks_weight()
    {
        var bench = Open(weights: new ItemWeights(new Dictionary<int, int> { [Scroll] = 1_000_000 }));
        BenchPlayer actor = await bench.EnterAsync(
            "keep", "Keep",
            items: [new StoredItem(Adena, 200, -1), new StoredItem(Scroll, 10, -1)],
            x: 0, y: 0, z: 0);

        Assert.Null(await actor.BypassAsync(KeeperObjectId, "DepositP"));
        var window = Assert.IsType<WarehouseDepositListNetMsg>(Last(bench, FanoutWarehouseDepositList)[1]);
        Assert.Equal(30, window.FeePerLine);
        Assert.Equal(100, window.SlotsMax);
        Assert.Equal(200, window.Adena);
        var scrolls = Assert.Single(window.Items, item => item.ItemId == Scroll);
        var adena = Assert.Single(window.Items, item => item.ItemId == Adena);

        // Depositing charges the per-line fee out of the adena stack and moves the scrolls out of the bag.
        Assert.Null(await actor.Deposit(KeeperObjectId, (scrolls.ObjectId, 10)));
        var paid = Assert.IsType<InventoryUpdateNetMsg>(Last(bench, FanoutInventoryUpdate)[1]);
        Assert.Contains(paid.Changes, row => row.ItemId == Adena && row.Count == 170);
        Assert.Contains(paid.Changes, row => row.ItemId == Scroll && row.Count == 0);
        Assert.Equal(NpcServiceProtocol.TransactionComplete, LastSystemMessageId(bench));

        Assert.Null(await actor.Deposit(KeeperObjectId, (adena.ObjectId, 50)));
        var second = Assert.IsType<InventoryUpdateNetMsg>(Last(bench, FanoutInventoryUpdate)[1]);
        Assert.Contains(second.Changes, row => row.ItemId == Adena && row.Count == 90);

        // The keeper's storage is durable across a relog.
        await bench.World.LeaveAsync(actor.Session, CancellationToken.None);
        Assert.Contains(actor.SavedRecord().Warehouse!, row => row.ItemId == Scroll && row.Count == 10);
        Assert.Contains(actor.SavedRecord().Warehouse!, row => row.ItemId == Adena && row.Count == 50);

        BenchPlayer again = await bench.EnterAsync("keep", "Keep", items: [], x: 0, y: 0, z: 0);
        Assert.Null(await again.BypassAsync(KeeperObjectId, "WithdrawP"));
        var back = Assert.IsType<WarehouseWithdrawListNetMsg>(Last(bench, FanoutWarehouseWithdrawList)[1]);
        Assert.Equal(2, back.SlotsUsed);
        Assert.True(back.MaxWeight > 69000);
        var storedAdena = Assert.Single(back.Items, item => item.ItemId == Adena);
        var storedScrolls = Assert.Single(back.Items, item => item.ItemId == Scroll);

        // One scroll is a million weight units: the withdraw is refused, the adena works.
        Assert.Null(await again.Withdraw(KeeperObjectId, (storedScrolls.ObjectId, 1)));
        Assert.Equal(NpcServiceProtocol.WeightLimit, LastSystemMessageId(bench));

        Assert.Null(await again.Withdraw(KeeperObjectId, (storedAdena.ObjectId, 50)));
        var returned = Assert.IsType<InventoryUpdateNetMsg>(Last(bench, FanoutInventoryUpdate)[1]);
        Assert.Contains(returned.Changes, row => row.ItemId == Adena && row.Count == 140);
    }

    [Fact]
    public async Task Warehouse_commands_are_refused_on_a_non_warehouse_npc()
    {
        var bench = Open();
        BenchPlayer actor = await bench.EnterAsync("gate", "Rook", x: 0, y: 0, z: 0);

        Assert.Null(await actor.BypassAsync(GateObjectId, "DepositP"));
        Assert.Empty(bench.Rec.Of(FanoutWarehouseDepositList));
        Assert.Contains(NpcServiceProtocol.NotWarehouseText, LastHtml(bench), StringComparison.Ordinal);
    }

    /// <summary>
    /// A gatekeeper and a warehouse keeper on one map, both standing where the bench puts the player, so every
    /// service call passes the talk range gate.
    /// </summary>
    static WorldBench Open(
        ItemWeights? weights = null,
        Func<DateTimeOffset>? clock = null)
    {
        var map = new SpawnMapDefinition(
            SpawnCatalog.DefaultMapId,
            true,
            [
                new SpawnNpcDefinition(GateObjectId, 30006, "Roxxy", 0, 0, 0, 0, false, 100, 80f, 40f, 60, 0, "Gatekeeper", "Teleporter"),
                new SpawnNpcDefinition(KeeperObjectId, 30005, "Wilford", 0, 0, 0, 0, false, 100, 80f, 40f, 60, 0, "Warehouse Keeper", "Warehouse")
            ]);
        return new WorldBench(
            spawns: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { [SpawnCatalog.DefaultMapId] = map }),
            teleports: SampleTeleports(),
            teleportOffset: 0,
            weights: weights,
            clock: clock);
    }

    static TeleportCatalog SampleTeleports() => TeleportCatalog.Parse(
        """
        {
          "gatekeepers": [
            {
              "npcId": 30006,
              "lists": [
                {
                  "name": "NORMAL",
                  "type": "NORMAL",
                  "locations": [
                    { "id": 0, "name": "Near", "x": 100, "y": 0, "z": 0, "feeCount": 1000 },
                    { "id": 1, "name": "Far", "x": 200000, "y": 200000, "z": 0, "feeCount": 18000 }
                  ]
                },
                {
                  "name": "NOBLES_ADENA",
                  "type": "NOBLES_ADENA",
                  "locations": [
                    { "id": 0, "name": "Arena", "x": 100, "y": 0, "z": 0, "feeCount": 1000 }
                  ]
                }
              ]
            }
          ]
        }
        """,
        "test");

    static object?[] Last(WorldBench bench, string method) => bench.Rec.Of(method).Last();

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlNetMsg>(Last(bench, FanoutNpcHtml)[1]).Html;

    static int LastSystemMessageId(WorldBench bench) =>
        Assert.IsType<SystemMessageNetMsg>(Last(bench, FanoutSystemMessage)[1]).Id;

    const string FanoutNpcHtml = "FanoutNpcHtml";
    const string FanoutSystemMessage = "FanoutSystemMessage";
    const string FanoutInventoryUpdate = "FanoutInventoryUpdate";
    const string FanoutMagicSkillUse = "FanoutMagicSkillUse";
    const string FanoutAbnormalStatusUpdate = "FanoutAbnormalStatusUpdate";
    const string FanoutTeleportToLocation = "FanoutTeleportToLocation";
    const string FanoutWarehouseDepositList = "FanoutWarehouseDepositList";
    const string FanoutWarehouseWithdrawList = "FanoutWarehouseWithdrawList";
}
