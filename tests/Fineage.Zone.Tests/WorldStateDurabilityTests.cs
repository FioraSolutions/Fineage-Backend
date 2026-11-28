using System.Text.Json;
using System.Text.RegularExpressions;
using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.Persistence;
using Fineage.Protocol;
using Fineage.Zone.Bosses;
using Fineage.Zone.Fishing;
using Fineage.Zone.Lottery;
using Fineage.Zone.Olympiad;
using Fineage.Zone.PcCafe;
using Fineage.Zone.Persistence;
using Fineage.Zone.Punishment;
using Fineage.Zone.RaidBossPoints;
using Fineage.Zone.SevenSigns;
using Xunit;
using static Fineage.Zone.Tests.StoredStateAuditTests;

namespace Fineage.Zone.Tests;

/// <summary>
/// #996: the world subsystems (bosses, seven signs, olympiad, lottery, raid points, punishments, monster race, fishing
/// championship, PC cafe) live in the store's <c>world_state</c> and ride the economy commit, so a value move and the
/// subsystem state it changes land together or not at all; the old sidecar files are imported once and archived.
/// </summary>
public sealed class WorldStateDurabilityTests
{
    static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    static string TempStateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "fineage-world-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static void Delete(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void Graceful_shutdown_commits_every_stateful_system_to_the_store_and_writes_no_file()
    {
        string directory = TempStateDirectory();
        try
        {
            var store = MemoryAccountStore.InMemory();
            var state = WorldStateStore.Open(store);
            var festival = new SevenSignsFestivalRuntime(SevenSignsFestivalCatalog.LoadDefault());
            long nowUnixMs = Now.ToUnixTimeMilliseconds();
            var bench = new WorldBench(
                store: store,
                bosses: BossSchedule.Open(state, Now),
                sevenSigns: SevenSignsEngine.Open(state, Now, festival),
                festivalRuntime: festival,
                olympiad: OlympiadBoard.Open(state, Now),
                punishments: PunishmentManager.Open(state, directory),
                raidBossPoints: RaidBossPointsManager.Open(state));
            bench.World.UseWorldState(state);
            bench.World.UseLottery(LotteryBoard.Open(state, nowUnixMs));
            bench.World.UseFishingChampionship(FishingChampionshipBoard.Open(state, nowUnixMs));
            bench.World.UseStoredMonsterRace(state, nowUnixMs);
            bench.World.UsePcCafe(new PcCafePointsManager(new StoredPcCafeStore(state)));

            Assert.Equal(WorldStateSystems.All, bench.World.StateFlushSystems);

            bench.World.ShutdownGracefully();
            bench.World.FlushDirtyInventoriesForTests();
            Assert.True(bench.World.DrainEconomyPersistForTests());

            Assert.Empty(bench.World.LastStateFlushFailures);
            Assert.False(state.HasDirty);
            foreach (string system in WorldStateSystems.All)
            {
                var saved = store.LoadWorldState(system);
                Assert.True(saved != null, "shutdown did not commit " + system);
                Assert.Equal(1, saved!.SchemaVersion);
                using var json = JsonDocument.Parse(saved.Json);
                Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
            }

            foreach (var kind in WorldStateCatalog.All)
                Assert.False(File.Exists(Path.Combine(directory, kind.LegacyFileName)), kind.LegacyFileName + " was written");
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Registry_startup_guard_names_an_omitted_stateful_system()
    {
        var registry = new ShutdownFlushRegistry();
        foreach (string system in WorldStateSystems.All.Where(system => system != WorldStateSystems.FishingChampionship))
            registry.Register(system, static () => { });

        var ex = Assert.Throws<InvalidOperationException>(registry.AssertComplete);

        Assert.Contains(WorldStateSystems.FishingChampionship, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "{\"schemaVersion\":1,\"bosses\":{}}", "invalid JSON")]
    [InlineData(2, "{\"schemaVersion\":2,\"bosses\":[]}", "schema version 2; this build reads 1")]
    public void Unreadable_or_unknown_version_document_refuses_startup_naming_the_system(int version, string json, string detail)
    {
        var store = MemoryAccountStore.InMemory();
        store.CommitEconomy([], [], [new WorldStateDocument(WorldStateSystems.BossSchedule, version, 1, json)]);

        var ex = Assert.ThrowsAny<InvalidOperationException>(() => BossSchedule.Open(WorldStateStore.Open(store), Now));

        Assert.Contains("boss-schedule", ex.Message, StringComparison.Ordinal);
        Assert.Contains(detail, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Document_whose_version_disagrees_with_its_column_is_refused_by_the_store()
    {
        var store = MemoryAccountStore.InMemory();
        var ex = Assert.Throws<ArgumentException>(() => store.CommitEconomy(
            [], [], [new WorldStateDocument(WorldStateSystems.Lottery, 1, 1, "{\"schemaVersion\":3}")]));
        Assert.Contains("schemaVersion 3", ex.Message, StringComparison.Ordinal);
        Assert.Null(store.LoadWorldState(WorldStateSystems.Lottery));
    }

    static string WriteLegacy<T>(string directory, WorldStateKind<T> kind, Action<T>? edit = null)
        where T : class, IVersionedStateSnapshot
    {
        var data = kind.CreateFresh();
        data.SchemaVersion = kind.SchemaVersion;
        kind.Normalize?.Invoke(data);
        edit?.Invoke(data);
        string path = Path.Combine(directory, kind.LegacyFileName);
        File.WriteAllText(path, JsonSerializer.Serialize(data, kind.Options));
        return path;
    }

    [Fact]
    public void Cutover_imports_a_sidecar_once_archives_it_and_a_rerun_does_nothing()
    {
        string directory = TempStateDirectory();
        try
        {
            var store = MemoryAccountStore.InMemory();
            string path = WriteLegacy(directory, WorldStateCatalog.Lottery, data =>
            {
                data.Number = 7;
                data.Prize = 123_456;
                data.EndUnixMs = Now.AddDays(3).ToUnixTimeMilliseconds();
            });

            var results = WorldStateCutover.Run(store, directory, () => Now);

            var lottery = Assert.Single(results, r => r.System == WorldStateSystems.Lottery);
            Assert.Equal(WorldStateCutoverAction.Imported, lottery.Action);
            Assert.All(results.Where(r => r.System != WorldStateSystems.Lottery), r => Assert.Equal(WorldStateCutoverAction.None, r.Action));
            Assert.False(File.Exists(path));
            Assert.True(File.Exists(lottery.ArchivedPath));
            var saved = store.LoadWorldState(WorldStateSystems.Lottery)!;
            Assert.Equal(1, saved.Revision);
            var board = LotteryBoard.Open(WorldStateStore.Open(store), Now.ToUnixTimeMilliseconds());
            Assert.Equal(7, board.Number);
            Assert.Equal(123_456, board.Prize);
            var import = Assert.Single(store.ListWorldStateImports());
            Assert.Equal(path, import.SourcePath);
            Assert.Equal(lottery.ArchivedPath, import.ArchivedPath);

            Assert.All(WorldStateCutover.Run(store, directory, () => Now), r => Assert.Equal(WorldStateCutoverAction.None, r.Action));
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Cutover_finishes_an_import_whose_archive_rename_was_lost()
    {
        string directory = TempStateDirectory();
        try
        {
            var store = MemoryAccountStore.InMemory();
            string path = WriteLegacy(directory, WorldStateCatalog.RaidBossPoints);
            var first = Assert.Single(WorldStateCutover.Run(store, directory, () => Now), r => r.System == WorldStateSystems.RaidBossPoints);
            // Killed between the database import and the rename: the file never moved.
            File.Move(first.ArchivedPath!, path);

            var again = Assert.Single(WorldStateCutover.Run(store, directory, () => Now.AddMinutes(1)),
                r => r.System == WorldStateSystems.RaidBossPoints);

            Assert.Equal(WorldStateCutoverAction.Archived, again.Action);
            Assert.False(File.Exists(path));
            Assert.Equal(1, store.LoadWorldState(WorldStateSystems.RaidBossPoints)!.Revision);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Cutover_refuses_two_sources_of_truth()
    {
        string directory = TempStateDirectory();
        try
        {
            var store = MemoryAccountStore.InMemory();
            store.CommitEconomy([], [], [new WorldStateDocument(WorldStateSystems.Olympiad, 1, 4,
                JsonSerializer.Serialize(WorldStateCatalog.Olympiad.CreateFresh(), WorldStateCatalog.Olympiad.Options))]);
            string path = WriteLegacy(directory, WorldStateCatalog.Olympiad);

            var ex = Assert.Throws<WorldStateCutoverException>(() => WorldStateCutover.Run(store, directory, () => Now));

            Assert.Equal(WorldStateSystems.Olympiad, ex.System);
            Assert.Equal(path, ex.FilePath);
            Assert.Contains("two sources of truth", ex.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(path));
            Assert.Equal(4, store.LoadWorldState(WorldStateSystems.Olympiad)!.Revision);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Fact]
    public void Cutover_leaves_the_fishing_settings_catalog_alone_when_the_state_directory_is_the_content_directory()
    {
        string directory = TempStateDirectory();
        try
        {
            var store = MemoryAccountStore.InMemory();
            string path = Path.Combine(directory, "fishing-championship.json");
            File.WriteAllText(path, "{\"enabled\":true,\"rewardItemId\":57,\"rewards\":[800000]}");

            var fishing = Assert.Single(WorldStateCutover.Run(store, directory, () => Now), r => r.System == WorldStateSystems.FishingChampionship);

            Assert.Equal(WorldStateCutoverAction.None, fishing.Action);
            Assert.True(File.Exists(path));
            Assert.Null(store.LoadWorldState(WorldStateSystems.FishingChampionship));

            // A real pre-#996 championship snapshot under the same name is still imported.
            WriteLegacy(directory, WorldStateCatalog.FishingChampionship);
            Assert.Equal(WorldStateCutoverAction.Imported,
                Assert.Single(WorldStateCutover.Run(store, directory, () => Now), r => r.System == WorldStateSystems.FishingChampionship).Action);
        }
        finally
        {
            Delete(directory);
        }
    }

    [Theory]
    [InlineData("{ truncated")]
    [InlineData("{\"Number\":3}")]
    [InlineData("{\"schemaVersion\":9,\"Number\":3}")]
    [InlineData("")]
    public void Cutover_fails_loudly_on_a_bad_sidecar_and_imports_nothing(string json)
    {
        string directory = TempStateDirectory();
        try
        {
            var store = MemoryAccountStore.InMemory();
            string path = Path.Combine(directory, "lottery.json");
            File.WriteAllText(path, json);

            var ex = Assert.Throws<WorldStateCutoverException>(() => WorldStateCutover.Run(store, directory, () => Now));

            Assert.Equal(WorldStateSystems.Lottery, ex.System);
            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(path));
            Assert.Null(store.LoadWorldState(WorldStateSystems.Lottery));
            Assert.Empty(store.ListWorldStateImports());
        }
        finally
        {
            Delete(directory);
        }
    }

    sealed class Lottery
    {
        public required List<LedgerEntry> Durable { get; init; }
        public required DurableStore Store { get; init; }
        public required WorldBench Bench { get; init; }
        public required WorldStateStore State { get; init; }
        public required LotteryBoard Board { get; init; }
    }

    // Wednesday 2027-01-13 12:00 UTC: lottery sales are open.
    static readonly DateTimeOffset Selling = new(2027, 1, 13, 12, 0, 0, TimeSpan.Zero);

    static Lottery OpenLottery(DurableStore store, List<LedgerEntry> durable)
    {
        var clock = new MutableClock { Now = Selling };
        var bench = new WorldBench(clock: () => clock.Now, accounts: store.Proxy, store: store.Inner, ledgerJournal: new TxJournal(durable));
        var state = WorldStateStore.Open(store.Proxy);
        bench.World.UseWorldState(state);
        var board = LotteryBoard.Open(state, Selling.ToUnixTimeMilliseconds());
        bench.World.UseLottery(board);
        return new Lottery { Durable = durable, Store = store, Bench = bench, State = state, Board = board };
    }

    static void Buy(Lottery rig, BenchPlayer player)
    {
        Assert.Null(rig.Bench.World.LotoForTest(player.Session, 0));
        foreach (int n in new[] { 1, 2, 3, 4, 5 })
            Assert.Null(rig.Bench.World.LotoForTest(player.Session, n));
        Assert.Null(rig.Bench.World.LotoForTest(player.Session, LotteryProtocol.PageConfirm));
    }

    static List<LotteryTicket> SavedTickets(DurableStore store) =>
        store.Inner.LoadWorldState(WorldStateSystems.Lottery) is { } doc ? WorldStateCatalog.Lottery.Parse(doc).Tickets : [];

    static int StoredTickets(DurableStore store, int characterId) =>
        store.Inner.FindByCharacterId(characterId)!.Items!.Count(row => row.ItemId == LotteryProtocol.TicketItemId);

    [Fact]
    public async Task Lottery_buy_killed_after_its_commit_restarts_with_the_ticket_in_both_the_bag_and_the_lottery()
    {
        var durable = new List<LedgerEntry>();
        var store = new DurableStore();
        var first = OpenLottery(store, durable);
        var player = await first.Bench.EnterAsync(items: [new StoredItem(57, 10_000, -1)]);
        Buy(first, player);
        Assert.True(first.Bench.World.FlushDirtyInventoriesForTests());
        Assert.True(first.Bench.World.DrainEconomyPersistForTests());

        // kill -9: no leave, no shutdown flush; the second world reads only what was committed.
        var second = OpenLottery(store, durable);

        Assert.Equal(1, StoredTickets(store, player.ObjectId));
        var ticket = Assert.Single(store.Inner.FindByCharacterId(player.ObjectId)!.Items!, row => row.ItemId == LotteryProtocol.TicketItemId);
        Assert.NotNull(second.Board.Ticket(ticket.EnchantLevel));
        Assert.Equal(1, second.Bench.World.LedgerForTests.TallyOf(LedgerTxKind.LotteryBuy, second.Board.Number).Moves);
        var report = second.Bench.World.AuditStoredState();
        Assert.DoesNotContain(report.Issues, issue => issue.Code.StartsWith("world-state", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Lottery_buy_whose_commit_dies_lands_neither_ledger_rows_nor_ticket_nor_lottery_state()
    {
        var durable = new List<LedgerEntry>();
        var store = new DurableStore();
        var first = OpenLottery(store, durable);
        var player = await first.Bench.EnterAsync(items: [new StoredItem(57, 10_000, -1)]);
        Assert.True(first.Bench.World.FlushDirtyInventoriesForTests());
        Assert.True(first.Bench.World.DrainEconomyPersistForTests());
        int rows = durable.Count;
        long revision = store.Inner.LoadWorldState(WorldStateSystems.Lottery)?.Revision ?? 0;

        store.FailCommits = true;
        Buy(first, player);
        first.Bench.World.FlushDirtyInventoriesForTests();
        first.Bench.World.DrainEconomyPersistForTests();

        Assert.True(store.Failed > 0);
        Assert.Equal(rows, durable.Count);
        Assert.Equal(0, StoredTickets(store, player.ObjectId));
        Assert.Equal(revision, store.Inner.LoadWorldState(WorldStateSystems.Lottery)?.Revision ?? 0);

        store.FailCommits = false;
        var second = OpenLottery(store, durable);
        Assert.Empty(SavedTickets(store));
        Assert.Equal(0, second.Bench.World.LedgerForTests.TallyOf(LedgerTxKind.LotteryBuy, second.Board.Number).Moves);
        Assert.DoesNotContain(second.Bench.World.AuditStoredState().Issues,
            issue => issue.Code.StartsWith("world-state", StringComparison.Ordinal));

        // The same world retries once the store is back: everything lands together.
        Assert.True(first.Bench.World.DrainEconomyPersistForTests());
        first.Bench.World.FlushDirtyInventoriesForTests();
        Assert.True(first.Bench.World.DrainEconomyPersistForTests());
        Assert.Equal(1, StoredTickets(store, player.ObjectId));
        Assert.Contains(durable, entry => entry.Transaction.Kind == LedgerTxKind.LotteryBuy);
        Assert.NotEmpty(SavedTickets(store));
    }

    [Fact]
    public async Task Audit_names_a_lottery_ticket_the_ledger_never_sold()
    {
        var durable = new List<LedgerEntry>();
        var store = new DurableStore();
        var first = OpenLottery(store, durable);
        await first.Bench.EnterAsync(items: [new StoredItem(57, 10_000, -1)]);
        var forged = WorldStateCatalog.Lottery.CreateFresh();
        forged.SchemaVersion = 1;
        forged.Number = first.Board.Number;
        forged.Tickets.Add(new LotteryTicket { Serial = 9, Number = forged.Number, Mask = 31 });
        store.Inner.CommitEconomy([], [], [new WorldStateDocument(WorldStateSystems.Lottery, 1, 1_000,
            JsonSerializer.Serialize(forged, WorldStateCatalog.Lottery.Options))]);

        var report = OpenLottery(store, durable).Bench.World.AuditStoredState();

        var issue = Assert.Single(report.Errors, i => i.Code == StoredStateIssue.WorldStateLedger);
        Assert.Equal(forged.Number, issue.Id);
        Assert.Contains("1 saved ticket(s)", issue.Detail, StringComparison.Ordinal);
        Assert.Contains("0 LotteryBuy", issue.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Audit_names_monster_race_bets_the_ledger_never_took_and_orphan_characters()
    {
        var ledger = new EconomyLedger(new MemoryLedgerJournal(), 1_000, WorldStateAudit.TalliedKinds);
        var race = new MonsterRace.MonsterRaceSnapshot { SchemaVersion = 1, RaceNumber = 4 };
        race.BetsPerLane[2] = 300;
        var points = new RaidBossPointsSnapshot { SchemaVersion = 1 };
        points.Characters.Add(new RaidBossPointsRow { CharacterId = 77, ByBoss = new() { [25001] = -5 } });

        var issues = WorldStateAudit.Check(
            [
                new WorldStateDocument(WorldStateSystems.MonsterRace, 1, 1, JsonSerializer.Serialize(race, WorldStateCatalog.MonsterRace.Options)),
                new WorldStateDocument(WorldStateSystems.RaidBossPoints, 1, 1, JsonSerializer.Serialize(points, WorldStateCatalog.RaidBossPoints.Options)),
            ],
            ledger,
            new HashSet<int>());

        Assert.Contains(issues, i => i.Code == StoredStateIssue.WorldStateLedger && i.Id == 4 && i.Detail.Contains("300", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Code == StoredStateIssue.WorldStateInvalid && i.Id == 77);
        Assert.Contains(issues, i => i.Code == StoredStateIssue.WorldStateOrphan && i.Id == 77 && i.Severity == StoredStateSeverity.Notice);
    }

    /// <summary>
    /// #996 acceptance: the subsystems that used to write sidecars from the command stage or the tick no longer touch
    /// the file system; their state goes through <see cref="WorldStateStore"/> and the economy commit.
    /// </summary>
    [Fact]
    public void World_subsystems_do_no_file_io()
    {
        string root = AppContext.BaseDirectory;
        while (!Directory.EnumerateFiles(root, "*.slnx").Any())
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("repository root (*.slnx) not found above " + AppContext.BaseDirectory);
        string zone = Path.Combine(root, "src", "Fineage.Zone");
        string[] folders = ["Bosses", "SevenSigns", "Olympiad", "Lottery", "RaidBossPoints", "MonsterRace", "Fishing", "PcCafe"];
        var io = new Regex(@"\b(File\.(Write|Append|Move|Create|Open|Replace|Delete)\w*|AtomicJsonFile\.\w+|FileStream|StreamWriter)\b");
        var offenders = new List<string>();
        foreach (string folder in folders)
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(zone, folder), "*.cs", SearchOption.AllDirectories))
            {
                foreach (var (line, n) in File.ReadLines(file).Select((line, i) => (line, i + 1)))
                {
                    if (io.IsMatch(line) && !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                        offenders.Add($"{Path.GetRelativePath(root, file)}:{n}: {line.Trim()}");
                }
            }
        }

        foreach (string file in Directory.EnumerateFiles(Path.Combine(zone, "World"), "ZoneWorld*.cs"))
        {
            foreach (var (line, n) in File.ReadLines(file).Select((line, i) => (line, i + 1)))
            {
                if (Regex.IsMatch(line, @"\bAtomicJsonFile\.|\bFile\.(Write|Move|Replace)"))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{n}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0, "world-state file I/O:\n" + string.Join('\n', offenders));
    }
}
