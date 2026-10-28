using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Siege;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public class SiegeRulesTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    static readonly long NowMs = Now.ToUnixTimeMilliseconds();

    [Fact]
    public void Catalog_is_the_nine_interlude_castles_with_zero_tax_and_a_two_week_sunday()
    {
        var board = new SiegeBoard(NowMs);
        Assert.Equal(9, board.Castles.Count);
        Assert.Equal(
            ["Gludio", "Dion", "Giran", "Oren", "Aden", "Innadril", "Goddard", "Rune", "Schuttgart"],
            board.Castles.Select(castle => castle.Name).ToArray());
        Assert.All(board.Castles, castle => Assert.Equal(0, castle.TaxPercent));
        Assert.All(board.Castles, castle => Assert.True(castle.ScheduleAccepted));
        Assert.All(board.Castles, castle => Assert.False(castle.RegistrationClosed));

        long gludio = new DateTimeOffset(2026, 11, 8, 16, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        long dion = new DateTimeOffset(2026, 11, 8, 20, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(gludio, board.Find(1)!.SiegeUnixMs);
        Assert.Equal(dion, board.Find(2)!.SiegeUnixMs);
        Assert.Equal(gludio, board.Find(3)!.SiegeUnixMs);
        Assert.Equal(dion, board.Find(6)!.SiegeUnixMs);
        Assert.Equal(gludio, board.Find(9)!.SiegeUnixMs);
        Assert.Equal(5, board.Castles.Count(castle => castle.SiegeUnixMs == gludio));
        Assert.Equal(4, board.Castles.Count(castle => castle.SiegeUnixMs == dion));
        Assert.Equal(DayOfWeek.Sunday, DateTimeOffset.FromUnixTimeMilliseconds(gludio).UtcDateTime.DayOfWeek);
    }

    [Fact]
    public void A_sixth_castle_on_the_same_timestamp_is_not_accepted()
    {
        var board = new SiegeBoard(NowMs);
        foreach (var castle in board.Castles)
            castle.MaxConcurrent = 1;
        board.ScheduleAll(NowMs);

        var atSixteen = board.Castles.Where(castle => castle.Hour == 16).ToList();
        Assert.Equal(5, atSixteen.Count);
        Assert.True(atSixteen[0].ScheduleAccepted);
        Assert.All(atSixteen.Skip(1), castle =>
        {
            Assert.False(castle.ScheduleAccepted);
            Assert.True(castle.RegistrationClosed);
        });
    }

    [Fact]
    public void Attacker_registration_requires_clan_level_4_and_rejects_duplicates()
    {
        var board = new SiegeBoard(NowMs);
        var low = Clan(7, level: 3);
        Assert.Equal(SiegeProtocol.Level, board.Register(Leader(low), low, 1, asAttacker: true, NowMs));

        var clan = Clan(7, level: 4);
        Assert.Null(board.Register(Leader(clan), clan, 1, asAttacker: true, NowMs));
        Assert.Equal(SiegeProtocol.Duplicate, board.Register(Leader(clan), clan, 1, asAttacker: true, NowMs));
        Assert.Equal(SiegeProtocol.SameDay, board.Register(Leader(clan), clan, 2, asAttacker: true, NowMs));
        Assert.Equal(SiegeProtocol.Attacker, Assert.Single(board.List(1, NowMs)!.Clans).Side);
        Assert.Null(board.Unregister(Leader(clan), clan, 1, NowMs));
        Assert.Equal(SiegeProtocol.None, board.Unregister(Leader(clan), clan, 1, NowMs));
    }

    [Fact]
    public void Level_4_is_accepted_and_a_configured_5_is_not()
    {
        var board = new SiegeBoard(NowMs, new SiegeSettings { MinClanLevel = 5 });
        var clan = Clan(7, level: 4);
        Assert.Equal(SiegeProtocol.Level, board.Register(Leader(clan), clan, 1, asAttacker: true, NowMs));
    }

    [Fact]
    public void Registration_closes_24_hours_before_the_siege_and_drops_waiting_defenders()
    {
        var board = new SiegeBoard(NowMs);
        var owner = Clan(1, level: 4);
        var waiting = Clan(2, level: 4);
        board.Find(1)!.OwnerClanId = owner.Id;
        Assert.Null(board.Register(Leader(waiting), waiting, 1, asAttacker: false, NowMs));
        Assert.Contains(board.List(1, NowMs)!.Clans, row => row.ClanId == waiting.Id && row.Side == SiegeProtocol.DefenderPending);

        long siege = board.Find(1)!.SiegeUnixMs;
        long deadline = siege - SiegeProtocol.RegistrationCloseMillis;
        Assert.Equal(SiegeProtocol.Closed, board.Register(Leader(waiting), waiting, 1, asAttacker: true, deadline));
        Assert.DoesNotContain(board.List(1, deadline)!.Clans, row => row.ClanId == waiting.Id);
        Assert.Contains(board.List(1, deadline)!.Clans, row => row.ClanId == owner.Id && row.Side == SiegeProtocol.Owner);
    }

    [Fact]
    public void Owner_is_an_automatic_defender_and_cannot_join_another_siege()
    {
        var board = new SiegeBoard(NowMs);
        var owner = Clan(4, level: 5);
        board.Find(1)!.OwnerClanId = owner.Id;
        Assert.Equal(SiegeProtocol.AutoDefender, board.Register(Leader(owner), owner, 1, asAttacker: true, NowMs));
        Assert.Equal(SiegeProtocol.AutoDefender, board.Unregister(Leader(owner), owner, 1, NowMs));
        Assert.Equal(SiegeProtocol.Owns, board.Register(Leader(owner), owner, 2, asAttacker: true, NowMs));
        Assert.Equal(SiegeProtocol.Npc, board.Register(Leader(Clan(9, level: 4)), Clan(9, level: 4), 2, asAttacker: false, NowMs));
    }

    [Fact]
    public void Owner_approves_or_refuses_defenders_and_the_cap_counts_the_waiting_clan()
    {
        var open = new SiegeBoard(NowMs);
        var owner = Clan(1, level: 4);
        var ally = Clan(2, level: 4);
        open.Find(1)!.OwnerClanId = owner.Id;
        Assert.Null(open.Register(Leader(ally), ally, 1, asAttacker: false, NowMs));
        Assert.Equal(SiegeProtocol.NotOwner, open.AnswerDefender(Leader(ally), ally, 1, ally.Id, approved: 1, NowMs));
        Assert.Null(open.AnswerDefender(Leader(owner), owner, 1, ally.Id, approved: 1, NowMs));
        Assert.Contains(open.List(1, NowMs)!.Clans, row => row.ClanId == ally.Id && row.Side == SiegeProtocol.Defender);
        Assert.Null(open.AnswerDefender(Leader(owner), owner, 1, ally.Id, approved: 0, NowMs));
        Assert.DoesNotContain(open.List(1, NowMs)!.Clans, row => row.ClanId == ally.Id);

        var tight = new SiegeBoard(NowMs, new SiegeSettings { MaxDefenders = 2 });
        tight.Find(1)!.OwnerClanId = owner.Id;
        Assert.Null(tight.Register(Leader(ally), ally, 1, asAttacker: false, NowMs));
        Assert.Equal(SiegeProtocol.Defenders, tight.AnswerDefender(Leader(owner), owner, 1, ally.Id, approved: 1, NowMs));
    }

    [Fact]
    public void CaptureCastle_gives_an_empty_castle_to_the_only_attacker_and_ends_the_siege()
    {
        var board = new SiegeBoard(NowMs);
        var clan = Clan(11, level: 4);
        Assert.Null(board.Register(Leader(clan), clan, 1, asAttacker: true, NowMs));
        long start = board.Find(1)!.SiegeUnixMs;
        Assert.Equal(SiegeStartKind.Started, board.StartSiege(1, start));
        Assert.True(board.Find(1)!.InProgress);
        Assert.Equal(start + 120 * 60_000L, board.Find(1)!.EndsUnixMs);

        Assert.Equal(SiegeProtocol.NotAttacker, board.CaptureCastle(99, start));
        Assert.Null(board.CaptureCastle(clan.Id, start + 60_000));
        var gludio = board.Find(1)!;
        Assert.Equal(clan.Id, gludio.OwnerClanId);
        Assert.False(gludio.InProgress);
        Assert.Equal(start + 14L * 24 * 60 * 60 * 1000, gludio.SiegeUnixMs);
        Assert.Equal(SiegeProtocol.Owner, Assert.Single(board.List(1, start + 60_000)!.Clans).Side);
        Assert.Equal(SiegeProtocol.Idle, board.CaptureCastle(clan.Id, start + 60_000));
    }

    [Fact]
    public void CaptureCastle_swaps_defenders_when_the_siege_continues()
    {
        var board = new SiegeBoard(NowMs);
        var owner = Clan(1, level: 4);
        var defender = Clan(2, level: 4);
        var attacker = Clan(3, level: 4);
        var second = Clan(4, level: 4);
        board.Find(3)!.OwnerClanId = owner.Id;
        Assert.Null(board.Register(Leader(defender), defender, 3, asAttacker: false, NowMs));
        Assert.Null(board.AnswerDefender(Leader(owner), owner, 3, defender.Id, approved: 1, NowMs));
        Assert.Null(board.Register(Leader(attacker), attacker, 3, asAttacker: true, NowMs));
        Assert.Null(board.Register(Leader(second), second, 3, asAttacker: true, NowMs));

        long start = board.Find(3)!.SiegeUnixMs;
        Assert.Equal(SiegeStartKind.Started, board.StartSiege(3, start));
        Assert.Null(board.CaptureCastle(attacker.Id, 3, start + 1_000));

        var giran = board.Find(3)!;
        Assert.True(giran.InProgress);
        Assert.Equal(attacker.Id, giran.OwnerClanId);
        var rows = board.List(3, start + 1_000)!.Clans;
        Assert.Contains(rows, row => row.ClanId == attacker.Id && row.Side == SiegeProtocol.Owner);
        Assert.Contains(rows, row => row.ClanId == defender.Id && row.Side == SiegeProtocol.Attacker);
        Assert.Contains(rows, row => row.ClanId == owner.Id && row.Side == SiegeProtocol.Attacker);
        Assert.Contains(rows, row => row.ClanId == second.Id && row.Side == SiegeProtocol.Attacker);
        Assert.Equal(0, board.CastleOwnedBy(owner.Id));
    }

    [Fact]
    public void Start_without_attackers_cancels_and_keeps_the_owner()
    {
        var board = new SiegeBoard(NowMs);
        board.Find(1)!.OwnerClanId = 4;
        long before = board.Find(1)!.SiegeUnixMs;
        Assert.Equal(SiegeStartKind.Cancelled, board.StartSiege(1, before + 1_000));
        Assert.False(board.Find(1)!.InProgress);
        Assert.Equal(4, board.Find(1)!.OwnerClanId);
        Assert.Equal(before + 14L * 24 * 60 * 60 * 1000, board.Find(1)!.SiegeUnixMs);
    }

    [Fact]
    public void Leader_dissolving_and_not_a_member_are_rejected()
    {
        var board = new SiegeBoard(NowMs);
        var clan = Clan(5, level: 4);
        Assert.Equal(SiegeProtocol.Clan, board.Register(new SiegeActor(1), null, 1, true, NowMs));
        Assert.Equal(SiegeProtocol.Leader, board.Register(new SiegeActor(99), clan, 1, true, NowMs));
        var dissolving = Clan(5, level: 4, dissolving: NowMs + 1);
        Assert.Equal(SiegeProtocol.Dissolving, board.Register(Leader(dissolving), dissolving, 1, true, NowMs));
        Assert.Equal(SiegeProtocol.Castle, board.Register(Leader(clan), clan, 40, true, NowMs));
    }

    [Fact]
    public void Castle_rows_and_hasCastle_round_trip_and_capture_updates_the_clan()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fineage-siege-{Guid.NewGuid():N}.json");
        try
        {
            var store = JsonAccountStore.Open(path);
            Assert.True(new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret)).Register("leader", "secret1").Ok);
            Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
                "leader", "Leader", HumanClasses.Fighter, 0, 0, out var leader, level: 40));
            var clan = store.InsertClan(new ClanRecord(
                0, "Siege", 4, leader!.Id, 0, 0, 0,
                [new ClanMemberRecord(leader.Id, "leader", "Leader", HumanClasses.Fighter, 40, 0)]),
                new CharacterClanState("leader", leader.Id, 0, 0, 0));
            Assert.NotNull(clan);

            var world = new ZoneWorld(accounts: store, clock: () => Now, siege: SiegeSettings.Mobius);
            Assert.Null(world.Sieges.Register(
                new SiegeActor(leader.Id),
                new SiegeClanInfo { Id = clan!.Id, Level = 4, LeaderId = leader.Id },
                1, true, NowMs));
            world.Sieges.Find(1)!.TaxPercent = 0;
            long start = world.Sieges.Find(1)!.SiegeUnixMs;
            Assert.Equal(SiegeStartKind.Started, world.StartSiege(1, DateTimeOffset.FromUnixTimeMilliseconds(start)));
            Assert.Null(world.CaptureCastle(clan.Id, DateTimeOffset.FromUnixTimeMilliseconds(start + 60_000)));

            Assert.Equal(clan.Id, store.FindClan(clan.Id)!.CastleId);
            Assert.Equal(clan.Id, store.ListCastles().Single(castle => castle.Id == 1).OwnerClanId);
            Assert.Equal(0, store.ListCastles().Single(castle => castle.Id == 1).TaxPercent);
            Assert.Empty(store.ListSiegeClans());

            var reloaded = new ZoneWorld(accounts: JsonAccountStore.Open(path), clock: () => Now, siege: SiegeSettings.Mobius);
            Assert.Equal(clan.Id, reloaded.Sieges.Find(1)!.OwnerClanId);
            Assert.Equal(clan.Id, reloaded.Sieges.CastleOwnedBy(clan.Id));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    static SiegeClanInfo Clan(int id, int level, long dissolving = 0) => new()
    {
        Id = id,
        Level = level,
        LeaderId = id * 10,
        DissolvingExpiryUnixMs = dissolving
    };

    static SiegeActor Leader(SiegeClanInfo clan) => new(clan.LeaderId);
}
