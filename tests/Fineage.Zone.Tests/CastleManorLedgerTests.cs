using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Manor;
using Fineage.Zone.Siege;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>P1-5a: castle treasury, upgrades, grants and manor seed buys on the auditable economy ledger.</summary>
public sealed class CastleManorLedgerTests
{
    const int Gludio = 1;
    const int Adena = PledgeProtocol.AdenaItemId;
    const int OuterGate = 19210007;
    const int DarkCodaSeed = 5016;
    const int Gx = -14288, Gy = 122752, Gz = -3000;

    static readonly LedgerAccount Vault = LedgerAccount.Treasury(Gludio);
    static readonly LedgerAccount Maintenance = LedgerAccount.Of(SystemAccount.CastleMaintenance);
    static readonly LedgerAccount Grants = LedgerAccount.Of(SystemAccount.CastleGrants);
    static readonly LedgerAccount Manor = LedgerAccount.Of(SystemAccount.Manor);
    static readonly CancellationToken Ct = CancellationToken.None;

    static LedgerTransaction Only(WorldBench bench, LedgerTxKind kind) =>
        Assert.Single(bench.Ledger.Entries, e => e.Transaction.Kind == kind).Transaction;

    static string Line(LedgerAccount account, int itemId, long amount, params int[] pieces) =>
        $"{account} {itemId} {amount} [{string.Join(",", pieces)}]";

    [Fact]
    public async Task Treasury_deposit_and_withdraw_journal_against_the_castle_vault()
    {
        var (bench, clock) = Open();
        var lord = await LeaderAsync(bench, "lord", "Lords", [new StoredItem(Adena, 2_000_000, -1)]);
        await OwnAsync(bench, clock, lord);
        Park(bench, lord);
        var bag = LedgerAccount.Inventory(lord.Player.ObjectId);

        Assert.Null(await Cmd(bench, lord, CastleCommandKind.Deposit, amount: 1_000_000));
        var deposit = Only(bench, LedgerTxKind.TreasuryDeposit);
        Assert.Equal(
            new[] { Line(bag, Adena, -1_000_000), Line(Vault, Adena, 1_000_000) }.Order(),
            deposit.Postings.Select(p => Line(p.Account, p.ItemId, p.Amount, p.InstanceIds.ToArray())).Order());

        Assert.Null(await Cmd(bench, lord, CastleCommandKind.Withdraw, amount: 400_000));
        var withdraw = Only(bench, LedgerTxKind.TreasuryWithdraw);
        Assert.Equal(
            new[] { Line(Vault, Adena, -400_000), Line(bag, Adena, 400_000) }.Order(),
            withdraw.Postings.Select(p => Line(p.Account, p.ItemId, p.Amount, p.InstanceIds.ToArray())).Order());
        Assert.Equal(600_000, bench.World.Sieges.Find(Gludio)!.Treasury);
        Assert.Equal(bench.World.Sieges.Find(Gludio)!.Treasury,
            bench.World.LedgerForTests.HoldingsOf(Vault).CountOf(Adena));
    }

    [Fact]
    public async Task Door_upgrade_pays_the_castle_maintenance_sink()
    {
        var (bench, clock) = Open();
        var lord = await LeaderAsync(bench, "lord", "Lords", [new StoredItem(Adena, 5_000_000, -1)]);
        await OwnAsync(bench, clock, lord);
        Park(bench, lord);
        var bag = LedgerAccount.Inventory(lord.Player.ObjectId);

        Assert.Null(await Cmd(bench, lord, CastleCommandKind.UpgradeDoor, a1: OuterGate, a2: 2));
        var upgrade = Only(bench, LedgerTxKind.CastleUpgrade);
        Assert.Equal(OuterGate, upgrade.Reference);
        Assert.Contains(upgrade.Postings, p => p.Account == Maintenance && p.ItemId == Adena && p.Amount == 750_000);
        Assert.Contains(upgrade.Postings, p => p.Account == bag && p.ItemId == Adena && p.Amount == -750_000);
    }

    [Fact]
    public async Task Circlet_grant_is_journaled_from_the_castle_grants_account()
    {
        var (bench, clock) = Open();
        var lord = await LeaderAsync(bench, "lord", "Lords");
        await OwnAsync(bench, clock, lord);
        Park(bench, lord);
        var bag = LedgerAccount.Inventory(lord.Player.ObjectId);

        Assert.Null(await Cmd(bench, lord, CastleCommandKind.TakeCirclet));
        var grant = Only(bench, LedgerTxKind.CastleGrant);
        Assert.Equal(Gludio, grant.Reference);
        int circlet = SiegeProtocol.CircletItemIds[Gludio];
        Assert.Contains(grant.Postings, p => p.Account == Grants && p.ItemId == circlet && p.Amount < 0);
        Assert.Contains(grant.Postings, p => p.Account == bag && p.ItemId == circlet && p.Amount > 0);
    }

    [Fact]
    public async Task Manor_seed_purchase_moves_adena_into_the_treasury_and_seeds_into_the_bag()
    {
        var (bench, clock) = OpenManor();
        var lord = await LordManorAsync(bench, 1_000_000);
        Assert.Null(await ManorCmd(bench, lord, ManorCommandKind.SetSeeds,
            new ManorRowNet { ItemId = DarkCodaSeed, Count = 100, Price = 10 }));
        bench.World.ChangeManorModeForTests();
        bench.World.ChangeManorModeForTests();
        bench.World.ChangeManorModeForTests();

        var farmer = await bench.EnterAsync("farmer", "Farmer", [new StoredItem(Adena, 10_000, -1)],
            level: 18, x: Gx, y: Gy, z: Gz);
        int manager = ManagerNpc(bench, farmer);
        long treasuryBefore = bench.World.FindCastle(Gludio)!.Treasury;
        var bag = LedgerAccount.Inventory(farmer.Player.ObjectId);

        Assert.Null(await bench.World.TryManorCommandAsync(
            farmer.Session,
            new ManorCommandNetMsg
            {
                Kind = ManorCommandKind.BuySeeds,
                CastleId = Gludio,
                NpcObjectId = manager,
                Rows = [new ManorRowNet { ItemId = DarkCodaSeed, Count = 2 }]
            },
            Ct));

        var buy = Only(bench, LedgerTxKind.ManorSeedBuy);
        Assert.Contains(buy.Postings, p => p.Account == bag && p.ItemId == Adena && p.Amount == -20);
        Assert.Contains(buy.Postings, p => p.Account == Vault && p.ItemId == Adena && p.Amount == 20);
        Assert.Contains(buy.Postings, p => p.Account == Manor && p.ItemId == DarkCodaSeed && p.Amount == -2);
        Assert.Equal(treasuryBefore + 20, bench.World.FindCastle(Gludio)!.Treasury);
    }

    static (WorldBench, MutableClock) Open()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        return (new WorldBench(siege: Fineage.Zone.Siege.SiegeSettings.Mobius, clock: () => clock.Now), clock);
    }

    static (WorldBench, MutableClock) OpenManor()
    {
        var clock = new MutableClock { Now = new DateTimeOffset(2027, 1, 15, 21, 0, 0, TimeSpan.Zero) };
        return (new WorldBench(clock: () => clock.Now, siege: Fineage.Zone.Siege.SiegeSettings.Mobius, manor: ManorSettings.Mobius), clock);
    }

    static async Task<BenchPlayer> LeaderAsync(WorldBench bench, string account, string clanName, StoredItem[]? items = null)
    {
        var leader = await bench.EnterAsync(account, account + "L", items, level: 40, x: 0, y: 0, z: 0, clearRecorder: false);
        int characterId = leader.Ticket.CharacterId;
        var clan = bench.Store.InsertClan(
            new ClanRecord(0, clanName, 4, characterId, 0, 0, 0,
                [new ClanMemberRecord(characterId, account, account + "L", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState(account, characterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(bench.World.ReloadClansForTests());
        leader.ClanId = clan!.Id;
        return leader;
    }

    static async Task<BenchPlayer> LordManorAsync(WorldBench bench, long treasury)
    {
        var lord = await bench.EnterAsync("lord", "LordL", level: 40, x: Gx, y: Gy, z: Gz, clearRecorder: false);
        int characterId = lord.Ticket.CharacterId;
        var clan = bench.Store.InsertClan(
            new ClanRecord(0, "Farmers", 5, characterId, 0, 0, 0,
                [new ClanMemberRecord(characterId, "lord", "LordL", HumanClasses.Fighter, 40, 0)]),
            new CharacterClanState("lord", characterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(bench.World.ReloadClansForTests());
        lord.ClanId = clan!.Id;
        var castle = bench.World.FindCastle(Gludio)!;
        castle.OwnerClanId = clan.Id;
        castle.Treasury = treasury;
        return lord;
    }

    static async Task OwnAsync(WorldBench bench, MutableClock clock, BenchPlayer leader)
    {
        Assert.Null(await bench.World.TrySiegeRegisterAsync(leader.Session,
            new SiegeRegisterRequestMsg { CastleId = Gludio, Attacker = 1, Joining = 1 }, Ct));
        clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(bench.World.Sieges.Find(Gludio)!.SiegeUnixMs);
        Assert.Equal(SiegeStartKind.Started, bench.World.StartSiege(Gludio));
        Assert.Null(bench.World.CaptureCastle(leader.ClanId));
        if (bench.World.Sieges.Find(Gludio)!.InProgress)
            Assert.Null(bench.World.EndSiege(Gludio));
    }

    static void Park(WorldBench bench, BenchPlayer player)
    {
        var chamberlain = bench.World.FindNpcTemplateForTest(35100);
        if (chamberlain != null)
            player.MoveTo(chamberlain.X + 20, chamberlain.Y, chamberlain.Z);
    }

    static int ManagerNpc(WorldBench bench, BenchPlayer player)
    {
        var npc = bench.World.FindNpcTemplateForTest(35103);
        if (npc == null)
            return 0;
        player.MoveTo(npc.X + 20, npc.Y, npc.Z);
        return npc.ObjectId;
    }

    static Task<string?> Cmd(WorldBench bench, BenchPlayer p, int kind, int a1 = 0, int a2 = 0, long amount = 0) =>
        bench.World.TryCastleCommandAsync(p.Session,
            new CastleCommandNetMsg { Kind = kind, CastleId = Gludio, IntArg1 = a1, IntArg2 = a2, Amount = amount },
            bench.Now(), Ct);

    static Task<string?> ManorCmd(WorldBench bench, BenchPlayer lord, int kind, params ManorRowNet[] rows) =>
        bench.World.TryManorCommandAsync(lord.Session,
            new ManorCommandNetMsg { Kind = kind, CastleId = Gludio, Next = true, Rows = rows }, Ct);
}