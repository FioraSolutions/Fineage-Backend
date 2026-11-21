using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Clan hall live doors and doorman bypasses (Mobius Door / Doorman / ClanHallDoorman).</summary>
public sealed class LiveDoorTests
{
    const int HallId = 28;
    const int HallDoorId = 17220001;

    static DoorTable HallDoorTable(bool closeTime = false) => new([
        new DoorInstance
        {
            Id = HallDoorId,
            Name = "test_gate",
            X = -84495,
            Y = 155208,
            Z = -3150,
            Nodes = [(-84494, 155208), (-84494, 155246), (-84497, 155246), (-84496, 155208)],
            MinZ = -3192,
            MaxZ = -2742,
            MinX = -84497,
            MaxX = -84494,
            MinY = 155208,
            MaxY = 155246,
            ClanHallId = HallId,
            CloseTimeSec = closeTime ? 2 : 0
        }
    ]);

    [Fact]
    public void Clan_hall_door_entity_tracks_open_state_on_net_and_geo()
    {
        var bench = new WorldBench(mapId: "gludin");
        bench.World.LoadCastleDoorsForTests(HallDoorTable());
        var door = bench.World.CastleDoorForTests(HallDoorId);
        Assert.NotNull(door);
        Assert.Equal(HallId, door!.SiegeNpc!.HallId);
        Assert.False(bench.World.GeoDoorOpenForTests(HallDoorId));

        Assert.True(bench.World.TrySetDoorOpen(HallDoorId, open: true));
        Assert.True(bench.World.GeoDoorOpenForTests(HallDoorId));
        var doorStatus = bench.World.DoorStatusForTests(HallDoorId);
        Assert.NotNull(doorStatus);
        Assert.True(doorStatus!.Open);
        Assert.Equal(HallId, doorStatus.HallId);
    }

    [Fact]
    public async Task Clan_hall_doorman_opens_every_hall_gate_for_the_owner()
    {
        var bench = new WorldBench(mapId: "gludin");
        bench.World.LoadCastleDoorsForTests(HallDoorTable());
        var leader = await bench.EnterAsync("doorlord", "DoorLord");
        var clan = bench.Store.InsertClan(new ClanRecord(
            0, "DoorClan", 4, leader.Ticket.CharacterId, 0, 0, 0,
            [new ClanMemberRecord(leader.Ticket.CharacterId, "doorlord", "DoorLord", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("doorlord", leader.Ticket.CharacterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(bench.World.ReloadClansForTests());
        bench.World.SetClanHallOwnerForTests(HallId, clan!.Id);

        var npc = new ZoneEntity
        {
            Kind = ZoneEntityKind.Npc,
            NpcId = 30772,
            NpcType = "ClanHallDoorman",
            ObjectId = 900_001,
            X = -84495,
            Y = 155208,
            Z = -3150
        };
        bench.World.PlaceForTests(npc);
        leader.MoveTo(-84495, 155208, -3150);

        Assert.Null(bench.World.TryDoormanDoors(leader.Session, leader.Player, npc, "open_doors", open: true));
        Assert.True(bench.World.GeoDoorOpenForTests(HallDoorId));
        Assert.Null(bench.World.TryDoormanDoors(leader.Session, leader.Player, npc, "close_doors", open: false));
        Assert.False(bench.World.GeoDoorOpenForTests(HallDoorId));
    }

    [Fact]
    public void Door_auto_closes_after_datapack_close_time()
    {
        var bench = new WorldBench(mapId: "gludin");
        bench.World.LoadCastleDoorsForTests(HallDoorTable(closeTime: true));
        Assert.True(bench.World.TrySetDoorOpen(HallDoorId, open: true));
        bench.World.TickDoorAutoCloseForTests(bench.Now().ToUnixTimeMilliseconds() + 2500);
        Assert.False(bench.World.GeoDoorOpenForTests(HallDoorId));
    }
}