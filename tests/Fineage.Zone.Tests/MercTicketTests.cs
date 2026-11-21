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
    public async Task Castle_lord_posts_a_mercenary_ticket_during_siege_prep()
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

    [Fact]
    public void Schuttgart_mercenary_hire_buylists_include_posting_tickets()
    {
        const int SchuttgartMercList = 355571;
        Assert.True(BuyListCatalog.LoadDefault().TryGet(SchuttgartMercList, out var list));
        Assert.Contains(list!.Products, p => p.ItemId == SampleTicketId);
        Assert.Contains(35557, list.Npcs);
    }

    [Fact]
    public async Task Hired_mercenary_does_not_respawn_when_killed()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        var bench = OpenWithSealValidation(clock);
        var lord = await OwnedLordAsync(bench, Schuttgart);
        lord.Player.X = SchuttgartCourt.X;
        lord.Player.Y = SchuttgartCourt.Y;
        lord.Player.Z = SchuttgartCourt.Z;
        var item = lord.Inventory.Add(SampleTicketId, 1);
        Assert.Null(await bench.World.TryUseItemAsync(lord.Session, item.ObjectId, CancellationToken.None));

        var attacker = await bench.EnterAsync("atk2", "Atk", level: 40, x: SchuttgartCourt.X + 80,
            y: SchuttgartCourt.Y, z: SchuttgartCourt.Z);
        var clan = bench.Store.InsertClan(
            new ClanRecord(0, "Atk", 4, attacker.Ticket.CharacterId, 0, 0, 0,
                [new ClanMemberRecord(attacker.Ticket.CharacterId, "atk2", "Atk", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("atk2", attacker.Ticket.CharacterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(bench.World.ReloadClansForTests());
        Assert.Null(await bench.World.TrySiegeRegisterAsync(attacker.Session,
            new SiegeRegisterRequestMsg { CastleId = Schuttgart, Attacker = 1, Joining = 1 }, CancellationToken.None));

        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Schuttgart)!.SiegeUnixMs);
        Assert.Equal(SiegeStartKind.Started, bench.World.StartSiege(Schuttgart));

        int mercNpc = MercTicketCatalog.Default.Find(SampleTicketId)!.Value.NpcId;
        var guard = Assert.Single(bench.World.SiegeNpcsForTests(Schuttgart),
            n => n.SiegeNpc!.Role == SiegeNpcRole.Guard && n.NpcId == mercNpc);
        bench.World.KillForTest(guard, attacker.Player);
        Assert.DoesNotContain(bench.World.SiegeNpcsForTests(Schuttgart),
            n => n.SiegeNpc!.Role == SiegeNpcRole.Guard && n.CurHp > 0);

        clock.Now += TimeSpan.FromMinutes(5);
        bench.World.TickSiegesForTests();
        Assert.DoesNotContain(bench.World.SiegeNpcsForTests(Schuttgart),
            n => n.SiegeNpc!.Role == SiegeNpcRole.Guard && n.CurHp > 0);
    }

    [Fact]
    public async Task Garrison_guard_respawn_is_blocked_when_its_control_tower_is_destroyed()
    {
        const int Gludio = 1;
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        var bench = new WorldBench(clock: () => clock.Now, siege: SiegeSettings.Mobius);
        var attacker = await bench.EnterAsync("tower", "Tower", level: 40, clearRecorder: false);
        int characterId = attacker.Ticket.CharacterId;
        var clan = bench.Store.InsertClan(
            new ClanRecord(0, "Tower", 4, characterId, 0, 0, 0,
                [new ClanMemberRecord(characterId, "tower", "Tower", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("tower", characterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(bench.World.ReloadClansForTests());
        attacker.ClanId = clan!.Id;
        Assert.Null(await bench.World.TrySiegeRegisterAsync(attacker.Session,
            new SiegeRegisterRequestMsg { CastleId = Gludio, Attacker = 1, Joining = 1 }, CancellationToken.None));

        var guardRow = CastleData.Default.Find(Gludio)!.Guards.First(g => g.RespawnSeconds == 60);
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Gludio)!.SiegeUnixMs);
        Assert.Equal(SiegeStartKind.Started, bench.World.StartSiege(Gludio));

        int towerIndex = 0;
        long bestSq = long.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            var ct = CastleData.Default.Find(Gludio)!.ControlTowers[i];
            long dx = ct.X - (long)guardRow.X, dy = ct.Y - (long)guardRow.Y, dz = ct.Z - (long)guardRow.Z;
            long sq = dx * dx + dy * dy + dz * dz;
            if (sq < bestSq)
            {
                bestSq = sq;
                towerIndex = i;
            }
        }

        static bool IsGuardAt(ZoneEntity n, CastlePlacement row) =>
            n.SiegeNpc!.Role == SiegeNpcRole.Guard
            && n.NpcId == row.NpcId
            && n.HomeX == row.X
            && n.HomeY == row.Y
            && n.HomeZ == row.Z;

        ZoneEntity? AliveGuard() => bench.World.SiegeNpcsForTests(Gludio)
            .FirstOrDefault(n => IsGuardAt(n, guardRow) && n.CurHp > 0);

        var guard = AliveGuard() ?? throw new InvalidOperationException("guard missing");
        bench.World.KillForTest(guard, attacker.Player);
        clock.Now += TimeSpan.FromSeconds(61);
        bench.World.TickSiegesForTests();
        guard = AliveGuard() ?? throw new InvalidOperationException("guard did not respawn");
        Assert.True(guard.CurHp > 0);

        var ctRow = CastleData.Default.Find(Gludio)!.ControlTowers[towerIndex];
        var tower = Assert.Single(bench.World.SiegeNpcsForTests(Gludio),
            n => n.SiegeNpc!.Role == SiegeNpcRole.ControlTower && n.X == ctRow.X && n.Y == ctRow.Y);
        Assert.True(bench.World.SiegeHitForTests(attacker.Player, tower, tower.MaxHp));
        guard = AliveGuard() ?? throw new InvalidOperationException("guard missing after tower");
        int aliveAtPost = bench.World.SiegeNpcsForTests(Gludio).Count(n => IsGuardAt(n, guardRow) && n.CurHp > 0);
        bench.World.KillForTest(guard, attacker.Player);

        clock.Now += TimeSpan.FromSeconds(61);
        bench.World.TickSiegesForTests();
        Assert.Equal(aliveAtPost - 1,
            bench.World.SiegeNpcsForTests(Gludio).Count(n => IsGuardAt(n, guardRow) && n.CurHp > 0));
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