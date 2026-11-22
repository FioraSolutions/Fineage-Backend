using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class AgitDecoInfoZoneTests
{
    const int HallId = 28;
    const int ManagerNpcId = 35384;

    [Fact]
    public void Encode_matches_mobius_hp_thresholds()
    {
        var hall = new ClanHallSave
        {
            Functions = [new ClanHallFunctionSave { Type = HallFunctionProtocol.FuncRestoreHp, Level = 80 }]
        };
        Assert.Equal(1, AgitDecoInfo.Encode(hall, grade: 2)[0]);
        hall.Functions[0].Level = 280;
        Assert.Equal(2, AgitDecoInfo.Encode(hall, grade: 2)[0]);
    }

    static ZoneEntity ManagerNpc(int x, int y) => new()
    {
        Kind = ZoneEntityKind.Npc,
        NpcId = ManagerNpcId,
        NpcType = "ClanHallManager",
        ObjectId = 910_050,
        X = x,
        Y = y,
        Z = -3150,
    };

    [Fact]
    public async Task AgitDecoInfo_fans_out_when_entering_hall_zone()
    {
        var bench = new WorldBench(mapId: "gludin");
        var leader = await bench.EnterAsync("decolord", "DecoLord");
        var clan = bench.Store.InsertClan(new ClanRecord(
            0, "DecoClan", 4, leader.Ticket.CharacterId, 0, 0, 0,
            [new ClanMemberRecord(leader.Ticket.CharacterId, "decolord", "DecoLord", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("decolord", leader.Ticket.CharacterId, 0, 0, 0));
        Assert.True(bench.World.ReloadClansForTests());
        bench.World.SetClanHallOwnerForTests(HallId, clan!.Id);
        bench.World.AddClanWarehouseItemForTest(clan.Id, PledgeProtocol.AdenaItemId, 1_000_000);

        var npc = ManagerNpc(-84495, 155208);
        bench.World.PlaceForTests(npc);
        leader.MoveTo(-84495, 155208, -3150);
        Assert.Null(bench.World.HallFunctionCommandForTest(
            leader.Session, leader.Player, npc, "manage recovery hp 80"));

        bench.World.TickHallDecoEnterForTest();
        var deco = Assert.IsType<AgitDecoInfoNetMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutAgitDecoInfo)).Last()[1]);
        Assert.Equal(HallId, deco.HallId);
        Assert.Equal(NetCodec.AgitDecoByteCount, deco.DecoBytes.Length);
        Assert.Equal(1, deco.DecoBytes[0]);

        var roundTrip = NetCodec.DecodeAgitDecoInfo(NetCodec.EncodeAgitDecoInfo(deco));
        Assert.Equal(deco.HallId, roundTrip.HallId);
        Assert.Equal(deco.DecoBytes, roundTrip.DecoBytes);
    }

    [Fact]
    public async Task Function_activation_rebroadcasts_AgitDecoInfo_in_hall()
    {
        var bench = new WorldBench(mapId: "gludin");
        var leader = await bench.EnterAsync("deco2", "DecoTwo");
        var member = await bench.EnterAsync("deco2mem", "DecoMem");
        var clan = bench.Store.InsertClan(new ClanRecord(
            0, "Deco2", 4, leader.Ticket.CharacterId, 0, 0, 0,
            [
                new ClanMemberRecord(leader.Ticket.CharacterId, "deco2", "DecoTwo", HumanClasses.Fighter, 40, 0),
                new ClanMemberRecord(member.Ticket.CharacterId, "deco2mem", "DecoMem", HumanClasses.Fighter, 40, 0)
            ]),
            new CharacterClanState("deco2", leader.Ticket.CharacterId, 0, 0, 0));
        Assert.True(bench.Store.TrySetCharacterClanState(
            new CharacterClanState("deco2mem", member.Ticket.CharacterId, clan!.Id, 0, 0)));
        Assert.True(bench.World.ReloadClansForTests());
        bench.World.SetClanHallOwnerForTests(HallId, clan.Id);
        bench.World.AddClanWarehouseItemForTest(clan.Id, PledgeProtocol.AdenaItemId, 2_000_000);

        var npc = ManagerNpc(-84495, 155208);
        bench.World.PlaceForTests(npc);
        leader.MoveTo(-84495, 155208, -3150);
        member.MoveTo(-84490, 155200, -3150);
        bench.World.TickHallDecoEnterForTest();
        int before = bench.Rec.Of(nameof(IWorldEventPublisher.FanoutAgitDecoInfo)).Count;

        Assert.Null(bench.World.HallFunctionCommandForTest(
            leader.Session, leader.Player, npc, "manage recovery mp 30"));
        Assert.True(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutAgitDecoInfo)).Count > before);
    }
}