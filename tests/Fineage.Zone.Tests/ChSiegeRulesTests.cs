using Fineage.Accounts;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.Siege;
using Fineage.Game.Economy;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ChSiegeRulesTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    static readonly long NowMs = Now.ToUnixTimeMilliseconds();

    [Fact]
    public void Catalog_lists_six_interlude_contestable_halls()
    {
        var halls = SiegableHallCatalog.Default.All.ToArray();
        Assert.Equal(6, halls.Length);
        Assert.Contains(halls, h => h.Id == 21 && h.AllowStartWithoutAttackers);
        Assert.All(halls.Where(h => h.Id != 21), h => Assert.False(h.AllowStartWithoutAttackers));
    }

    [Fact]
    public void Attacker_registration_and_close_registration_clears_owner()
    {
        var board = new ChSiegeBoard(SiegableHallCatalog.Default.All);
        var owner = Clan(1, 5);
        var attacker = Clan(2, 4);
        var hall = board.Find(34)!;
        hall.OwnerClanId = owner.Id;
        hall.NextSiegeUnixMs = NowMs + 86_400_000 * 2;

        Assert.Null(board.Register(Leader(attacker), attacker, 34, 0, 0, _ => false, NowMs));
        Assert.Equal(SiegeProtocol.AutoDefender, board.Register(Leader(owner), owner, 34, 0, 0, _ => false, NowMs));

        long regDeadline = hall.NextSiegeUnixMs - board.Settings.RegistrationCloseBeforeMs;
        Assert.True(board.CloseRegistration(34, regDeadline));
        Assert.True(hall.RegistrationClosed);
        Assert.Equal(0, hall.OwnerClanId);
        Assert.Contains(owner.Id, hall.Attackers);
        Assert.Contains(attacker.Id, hall.Attackers);
    }

    [Fact]
    public void Start_siege_capture_and_persist_round_trip()
    {
        var store = MemoryAccountStore.InMemory();
        Assert.True(new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret)).Register("leader", "secret1").Ok);
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
            "leader", "Leader", HumanClasses.Fighter, 0, 0, out var leader, level: 40));
        var clan = store.InsertClan(new ClanRecord(
            0, "HallRaiders", 4, leader!.Id, 0, 0, 0,
            [new ClanMemberRecord(leader.Id, "leader", "Leader", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("leader", leader.Id, 0, 0, 0));
        Assert.NotNull(clan);

        var world = new ZoneWorld(accounts: store, clock: () => Now, siege: SiegeSettings.Mobius,
            ledgerJournal: new MemoryLedgerJournal());
        var hall = world.ChSieges.Find(34)!;
        hall.NextSiegeUnixMs = NowMs + 7_200_000;

        Assert.Null(world.ChSieges.Register(
            new SiegeRequester(leader.Id),
            new SiegeClanInfo { Id = clan!.Id, Level = 4, LeaderId = leader.Id },
            34, 0, 0, _ => false, NowMs));

        long siegeAt = hall.NextSiegeUnixMs;
        Assert.Equal(ChSiegeStartKind.Started, world.ChSieges.StartSiege(34, siegeAt));
        Assert.Null(world.CaptureClanHall(clan.Id, 34, siegeAt + 30_000));

        Assert.Equal(clan.Id, world.ChSieges.Find(34)!.OwnerClanId);
        Assert.Equal(34, store.FindClan(clan.Id)!.Details?.HallId);
        Assert.Single(store.ListSiegableClanHalls(), row => row.Id == 34 && row.OwnerClanId == clan.Id);

        var reloaded = new ZoneWorld(
            accounts: MemoryAccountStore.FromSnapshot(store.ExportSnapshot()),
            clock: () => Now,
            siege: SiegeSettings.Mobius,
            ledgerJournal: new MemoryLedgerJournal());
        Assert.Equal(clan.Id, reloaded.ChSieges.Find(34)!.OwnerClanId);
        var snapshot = MemoryAccountStore.FromSnapshot(store.ExportSnapshot());
        Assert.Equal(34, snapshot.FindClan(clan.Id)!.Details?.HallId);
        Assert.Equal(clan.Id, reloaded.ChSieges.Find(34)!.OwnerClanId);
    }

    static SiegeClanInfo Clan(int id, int level) => new()
    {
        Id = id,
        Level = level,
        LeaderId = id * 10
    };

    static SiegeRequester Leader(SiegeClanInfo clan) => new(clan.LeaderId);
}