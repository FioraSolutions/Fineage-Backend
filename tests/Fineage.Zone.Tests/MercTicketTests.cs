using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Siege;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class MercTicketTests
{
    const int Schuttgart = 9;
    const int SampleTicketId = 7918;
    static readonly (int X, int Y, int Z) SchuttgartCourt = (80334, -152898, -8);

    [Fact]
    public async Task Castle_lord_posts_a_mercenary_ticket_during_seal_validation()
    {
        var bench = OpenWithSealValidation();
        var leader = await OwnedLordAsync(bench, Schuttgart);
        leader.Player.X = SchuttgartCourt.X;
        leader.Player.Y = SchuttgartCourt.Y;
        leader.Player.Z = SchuttgartCourt.Z;

        int ticketId = SampleTicketId;
        var item = leader.Inventory.Add(ticketId, 1);

        Assert.Null(await bench.World.TryUseItemAsync(leader.Session, item.ObjectId, CancellationToken.None));
        var castle = bench.World.Sieges.Find(Schuttgart)!;
        Assert.Single(castle.HiredGuards);
        Assert.Equal(ticketId, castle.HiredGuards[0].ItemId);
        Assert.Equal(0, leader.Inventory.CountOf(ticketId));
        Assert.True(bench.World.GroundItemCountForTest() >= 1);
    }

    [Fact]
    public async Task Siege_start_spawns_hired_guards_and_clears_ground_tickets()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        var bench = OpenWithSealValidation(clock);
        var leader = await OwnedLordAsync(bench, Schuttgart);
        leader.Player.X = SchuttgartCourt.X;
        leader.Player.Y = SchuttgartCourt.Y;
        leader.Player.Z = SchuttgartCourt.Z;
        var item = leader.Inventory.Add(SampleTicketId, 1);
        Assert.Null(await bench.World.TryUseItemAsync(leader.Session, item.ObjectId, CancellationToken.None));
        int groundBefore = bench.World.GroundItemCountForTest();

        var attacker = await bench.EnterAsync("atk", "Attacker", level: 40, x: SchuttgartCourt.X + 50, y: SchuttgartCourt.Y, z: SchuttgartCourt.Z);
        var atkClan = bench.Store.InsertClan(
            new ClanRecord(0, "Attack", 4, attacker.Ticket.CharacterId, 0, 0, 0,
                [new ClanMemberRecord(attacker.Ticket.CharacterId, "atk", "Attacker", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("atk", attacker.Ticket.CharacterId, 0, 0, 0));
        Assert.NotNull(atkClan);
        Assert.True(bench.World.ReloadClansForTests());
        Assert.Null(await bench.World.TrySiegeRegisterAsync(attacker.Session,
            new SiegeRegisterRequestMsg { CastleId = Schuttgart, Attacker = 1, Joining = 1 }, CancellationToken.None));

        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Schuttgart)!.SiegeUnixMs);
        Assert.Equal(SiegeStartKind.Started, bench.World.StartSiege(Schuttgart));

        Assert.Equal(0, bench.World.GroundItemCountForTest());
        Assert.True(groundBefore > 0);
    }

    static WorldBench OpenWithSealValidation(MutableClock? clock = null)
    {
        clock ??= new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        var engine = new SevenSignsEngine(clock.Now);
        engine.ForcePeriodForTests(SevenSignsPeriod.SealValidation);
        return new WorldBench(clock: () => clock.Now, sevenSigns: engine, siege: SiegeSettings.Mobius);
    }

    static async Task<BenchPlayer> OwnedLordAsync(WorldBench bench, int castleId)
    {
        var leader = await EnterClanLeaderAsync(bench);
        bench.World.Sieges.Find(castleId)!.OwnerClanId = leader.ClanId;
        return leader;
    }

    static async Task<BenchPlayer> EnterClanLeaderAsync(WorldBench bench)
    {
        BenchPlayer leader = await bench.EnterAsync("lord", "Lord", level: 40, x: SchuttgartCourt.X, y: SchuttgartCourt.Y, z: SchuttgartCourt.Z);
        int characterId = leader.Ticket.CharacterId;
        var clan = bench.Store.InsertClan(
            new ClanRecord(
                0, "Castle", 4, characterId, 0, 0, 0,
                [new ClanMemberRecord(characterId, "lord", "Lord", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("lord", characterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(bench.World.ReloadClansForTests());
        leader.ClanId = clan!.Id;
        return leader;
    }
}