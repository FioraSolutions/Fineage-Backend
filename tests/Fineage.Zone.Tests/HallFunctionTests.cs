using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;
using Fineage.Game.Shops;

namespace Fineage.Zone.Tests;

public sealed class HallFunctionTests
{
    const int HallId = 28;
    const int ManagerNpcId = 35384;

    static ZoneEntity ManagerNpc(int x, int y) => new()
    {
        Kind = ZoneEntityKind.Npc,
        NpcId = ManagerNpcId,
        NpcType = "ClanHallManager",
        ObjectId = 910_001,
        X = x,
        Y = y,
        Z = -3150,
        MaxMp = 2000,
        CurMp = 2000
    };

    [Fact]
    public async Task Activate_hp_regen_function_with_warehouse_adena()
    {
        var bench = new WorldBench(mapId: "gludin");
        var leader = await bench.EnterAsync("fnlord", "FnLord");
        var clan = bench.Store.InsertClan(new ClanRecord(
            0, "FnClan", 4, leader.Ticket.CharacterId, 0, 0, 0,
            [new ClanMemberRecord(leader.Ticket.CharacterId, "fnlord", "FnLord", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("fnlord", leader.Ticket.CharacterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(bench.World.ReloadClansForTests());
        bench.World.SetClanHallOwnerForTests(HallId, clan!.Id);
        bench.World.AddClanWarehouseItemForTest(clan.Id, PledgeProtocol.AdenaItemId, 1_000_000);

        var npc = ManagerNpc(-84495, 155208);
        bench.World.PlaceForTests(npc);
        leader.MoveTo(-84495, 155208, -3150);

        Assert.Null(bench.World.HallFunctionCommandForTest(
            leader.Session, leader.Player, npc, "manage recovery hp 80"));
        var fn = bench.World.HallFunctionForTest(HallId, HallFunctionProtocol.FuncRestoreHp);
        Assert.NotNull(fn);
        Assert.Equal(80, fn!.Level);
        Assert.True(fn.EndUnixMs > bench.Now().ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Privilege_denial_without_manage_functions()
    {
        var bench = new WorldBench(mapId: "gludin");
        var leader = await bench.EnterAsync("fnlead", "FnLead");
        var member = await bench.EnterAsync("fnmem", "FnMem");
        var clan = bench.Store.InsertClan(new ClanRecord(
            0, "PrivClan", 4, leader.Ticket.CharacterId, 0, 0, 0,
            [
                new ClanMemberRecord(leader.Ticket.CharacterId, "fnlead", "FnLead", HumanClasses.Fighter, 40, 0),
                new ClanMemberRecord(member.Ticket.CharacterId, "fnmem", "FnMem", HumanClasses.Fighter, 40, 0)
            ]),
            new CharacterClanState("fnlead", leader.Ticket.CharacterId, 0, 0, 0));
        Assert.True(bench.Store.TrySetCharacterClanState(
            new CharacterClanState("fnmem", member.Ticket.CharacterId, clan!.Id, 0, 0)));
        Assert.True(bench.World.ReloadClansForTests());
        bench.World.SetClanHallOwnerForTests(HallId, clan.Id);
        bench.World.AddClanWarehouseItemForTest(clan.Id, PledgeProtocol.AdenaItemId, 500_000);

        var npc = ManagerNpc(-84495, 155208);
        bench.World.PlaceForTests(npc);
        member.MoveTo(-84495, 155208, -3150);

        Assert.NotNull(bench.World.HallFunctionCommandForTest(
            member.Session, member.Player, npc, "manage recovery hp 40"));
        Assert.Null(bench.World.HallFunctionForTest(HallId, HallFunctionProtocol.FuncRestoreHp));
    }

    [Fact]
    public async Task Function_lease_expires_when_warehouse_empty()
    {
        var bench = new WorldBench(mapId: "gludin");
        var leader = await bench.EnterAsync("ticklord", "TickLord");
        var clan = bench.Store.InsertClan(new ClanRecord(
            0, "TickClan", 4, leader.Ticket.CharacterId, 0, 0, 0,
            [new ClanMemberRecord(leader.Ticket.CharacterId, "ticklord", "TickLord", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("ticklord", leader.Ticket.CharacterId, 0, 0, 0));
        Assert.True(bench.World.ReloadClansForTests());
        bench.World.SetClanHallOwnerForTests(HallId, clan!.Id);
        bench.World.AddClanWarehouseItemForTest(clan.Id, PledgeProtocol.AdenaItemId, 5_000);

        var npc = ManagerNpc(-84495, 155208);
        bench.World.PlaceForTests(npc);
        leader.MoveTo(-84495, 155208, -3150);
        Assert.Null(bench.World.HallFunctionCommandForTest(
            leader.Session, leader.Player, npc, "manage recovery mp 15"));

        bench.World.SetHallWarehouseAdenaForTest(clan.Id, 0);
        long end = bench.World.HallFunctionForTest(HallId, HallFunctionProtocol.FuncRestoreMp)!.EndUnixMs;
        bench.World.TickHallFunctionsForTest(end + 1);
        Assert.Null(bench.World.HallFunctionForTest(HallId, HallFunctionProtocol.FuncRestoreMp));
    }

    [Fact]
    public async Task Item_creation_opens_ledger_multisell_when_function_active()
    {
        var multisells = MultiSellCatalog.From(
        [
            new MultiSellListDefinition(
                HallFunctionProtocol.ItemCreateListId(HallId, 1),
                applyTaxes: false,
                maintainEnchantment: false,
                [ManagerNpcId],
                [new MultiSellEntry(1, [new MultiSellPart(57, 50)], [new MultiSellPart(1060, 1)])])
        ]);
        var bench = new WorldBench(mapId: "gludin", multisells: multisells);
        var leader = await bench.EnterAsync("itemlord", "ItemLord");
        var clan = bench.Store.InsertClan(new ClanRecord(
            0, "ItemClan", 4, leader.Ticket.CharacterId, 0, 0, 0,
            [new ClanMemberRecord(leader.Ticket.CharacterId, "itemlord", "ItemLord", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("itemlord", leader.Ticket.CharacterId, 0, 0, 0));
        Assert.True(bench.World.ReloadClansForTests());
        bench.World.SetClanHallOwnerForTests(HallId, clan!.Id);
        bench.World.AddClanWarehouseItemForTest(clan.Id, PledgeProtocol.AdenaItemId, 500_000);

        var npc = ManagerNpc(-84495, 155208);
        bench.World.PlaceForTests(npc);
        leader.MoveTo(-84495, 155208, -3150);
        Assert.Null(bench.World.HallFunctionCommandForTest(
            leader.Session, leader.Player, npc, "manage other item 1"));
        Assert.NotNull(bench.World.HallFunctionForTest(HallId, HallFunctionProtocol.FuncItemCreate));

        Assert.Null(bench.World.HallItemCreateCommandForTest(
            leader.Session, leader.Player, npc, "functions item_creation 28"));
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMultiSellList)));
    }
}