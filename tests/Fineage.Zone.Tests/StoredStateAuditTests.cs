using System.Reflection;
using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Zone.Persistence;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #984: saved state vs data catalogs vs the replayed ledger. A restart is a new world over the same store and the
/// same durable journal; a crash is the old world dropped without a shutdown flush.
/// </summary>
public sealed class StoredStateAuditTests
{
    const int Adena = WarehouseRules.AdenaItemId;
    const int RemovedId = 999_999;

    [Fact]
    public async Task Saved_item_with_a_removed_template_refuses_enter_with_names_and_keeps_the_row()
    {
        var bench = new WorldBench();
        Assert.True(bench.Store.TryAddAccount("ghost", "hash", "salt", 1, null) == AddAccountStatus.Created);
        Assert.Equal(CreateCharacterStatus.Ok, bench.Store.TryAddCharacter("ghost", "Ghost", 0, 0, 0, out var created,
            [new StoredItem(Adena, 10, -1, ObjectId: 0), new StoredItem(RemovedId, 1, -1, ObjectId: 0)]));
        var session = new ZoneSession();
        session.Bind(created!.Id, "ghost", "Ghost", 0);

        Assert.False(await bench.World.TryEnterAsync(session, CancellationToken.None));
        Assert.NotNull(session.EnterRefusal);
        Assert.Contains("StoredStateException", session.EnterRefusal);
        Assert.Contains($"'Ghost'", session.EnterRefusal);
        Assert.Contains("inventory", session.EnterRefusal);
        Assert.Contains($"id={RemovedId}", session.EnterRefusal);
        // Never dropped: the row is still in the store for a data fix to bring back.
        Assert.Contains(bench.Store.FindById("ghost", created.Id)!.Items!, row => row.ItemId == RemovedId);
    }

    [Fact]
    public async Task Saved_skill_effect_recipe_and_henna_with_removed_ids_are_all_named()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        await bench.World.LeaveAsync(player.Session, CancellationToken.None);
        Assert.True(bench.World.FlushDirtyInventoriesForTests());
        Assert.True(bench.World.DrainEconomyPersistForTests());
        string account = player.Ticket.Account;
        int id = player.ObjectId;
        Assert.True(bench.Store.TryReplaceLearnedSkills(account, id, [new StoredSkill(RemovedId, 1)]));
        Assert.True(bench.Store.TryReplaceEffects(account, id, [new StoredEffect(RemovedId + 1, 1, 30)]));
        Assert.True(bench.Store.TryReplaceKnownRecipes(account, id, [RemovedId + 2]));

        var issues = StoredStateAudit.References(bench.Store.FindById(account, id)!, bench.Game, Fineage.Game.HennaCatalog.LoadDefault());
        Assert.Contains(issues, issue => issue.Container == "learned-skill" && issue.Id == RemovedId);
        Assert.Contains(issues, issue => issue.Container == "effect" && issue.Id == RemovedId + 1);
        Assert.Contains(issues, issue => issue.Container == "recipe" && issue.Id == RemovedId + 2);
        Assert.All(issues, issue => Assert.Equal(StoredStateIssue.MissingReference, issue.Code));

        var again = new ZoneSession();
        again.Bind(id, account, player.Session.Name, player.Session.ClassId);
        Assert.False(await bench.World.TryEnterAsync(again, CancellationToken.None));
        Assert.Contains($"id={RemovedId}", again.EnterRefusal);
        Assert.Contains("learned-skill", again.EnterRefusal);
        Assert.Single(bench.Store.FindById(account, id)!.LearnedSkills!);
    }

    [Fact]
    public async Task Crash_after_commit_restart_reload_matches_the_ledger()
    {
        var durable = new List<LedgerEntry>();
        var store = new DurableStore();
        var first = new WorldBench(accounts: store.Proxy, store: store.Inner, ledgerJournal: new TxJournal(durable));
        var player = await first.EnterAsync(items: [new StoredItem(Adena, 100, -1)]);
        Assert.Null(await first.World.TryGiveItemAsync(player.Session, Adena, 50, CancellationToken.None));
        Assert.True(first.World.FlushDirtyInventoriesForTests());
        Assert.True(first.World.DrainEconomyPersistForTests());
        Assert.NotEmpty(durable);

        // Crash: the first world is abandoned, no leave, no shutdown flush.
        var second = new WorldBench(accounts: store.Proxy, store: store.Inner, ledgerJournal: new TxJournal(durable));
        var report = second.World.AuditStoredState();
        Assert.Empty(report.Errors);
        Assert.DoesNotContain(report.Issues, issue => issue.Owner.Contains($"character {player.ObjectId} ", StringComparison.Ordinal)
                                                      && issue.Container == "inventory");
        Assert.Equal(150, second.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(player.ObjectId)).CountOf(Adena));
    }

    [Fact]
    public async Task Crash_during_commit_restart_reload_has_no_divergence()
    {
        var durable = new List<LedgerEntry>();
        var store = new DurableStore();
        var first = new WorldBench(accounts: store.Proxy, store: store.Inner, ledgerJournal: new TxJournal(durable));
        var player = await first.EnterAsync(items: [new StoredItem(Adena, 100, -1)]);
        Assert.Null(await first.World.TryGiveItemAsync(player.Session, Adena, 5, CancellationToken.None));
        Assert.True(first.World.FlushDirtyInventoriesForTests());
        Assert.True(first.World.DrainEconomyPersistForTests());
        int committed = durable.Count;

        // The next transaction dies inside the database commit: neither the journal rows nor the bag land.
        store.FailCommits = true;
        Assert.Null(await first.World.TryGiveItemAsync(player.Session, Adena, 7, CancellationToken.None));
        first.World.FlushDirtyInventoriesForTests();
        Assert.True(first.World.DrainEconomyPersistForTests());
        Assert.Equal(committed, durable.Count);
        Assert.Equal(105, store.Inner.FindByCharacterId(player.ObjectId)!.Items!.Where(row => row.ItemId == Adena).Sum(row => row.Count));

        var second = new WorldBench(accounts: store.Proxy, store: store.Inner, ledgerJournal: new TxJournal(durable));
        store.FailCommits = false;
        var report = second.World.AuditStoredState();
        Assert.Empty(report.Errors);
        Assert.Equal(105, second.World.LedgerForTests.HoldingsOf(LedgerAccount.Inventory(player.ObjectId)).CountOf(Adena));
    }

    [Fact]
    public async Task Store_edit_behind_the_ledger_is_a_named_divergence_and_block_refuses_start()
    {
        var durable = new List<LedgerEntry>();
        var store = new DurableStore();
        var first = new WorldBench(accounts: store.Proxy, store: store.Inner, ledgerJournal: new TxJournal(durable));
        var player = await first.EnterAsync(items: [new StoredItem(Adena, 100, -1)]);
        Assert.Null(await first.World.TryGiveItemAsync(player.Session, Adena, 1, CancellationToken.None));
        Assert.True(first.World.FlushDirtyInventoriesForTests());
        Assert.True(first.World.DrainEconomyPersistForTests());
        var rows = store.Inner.FindByCharacterId(player.ObjectId)!.Items!
            .Select(row => row.ItemId == Adena ? row with { Count = row.Count + 1_000 } : row).ToList();
        Assert.True(store.Inner.TryReplaceItems(player.Ticket.Account, player.ObjectId, rows));

        var second = new WorldBench(accounts: store.Proxy, store: store.Inner, ledgerJournal: new TxJournal(durable));
        var report = second.World.AuditStoredState();
        var divergence = Assert.Single(report.Errors);
        Assert.Equal(StoredStateIssue.LedgerDivergence, divergence.Code);
        Assert.Equal("inventory", divergence.Container);
        Assert.Contains($"character {player.ObjectId} ", divergence.Owner, StringComparison.Ordinal);
        Assert.Contains("1101", divergence.Detail, StringComparison.Ordinal);

        second.World.AuditStoredStateAtStartup(_ => "log");
        var blocked = Assert.Throws<StoredStateException>(() => second.World.AuditStoredStateAtStartup(_ => "block"));
        Assert.Contains(StoredStateIssue.LedgerDivergence, blocked.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => StoredStateAudit.BlockOnError(_ => "maybe"));
    }

    /// <summary>
    /// #992 injection for the divergence alerts: a store row edited behind the ledger must reach /metrics as
    /// <c>fineage_economy_reconcile_violations_total{code="store-divergence"}</c> (FineageReconcileViolations) and
    /// <c>fineage_state_audit_issues_total{severity="error"}</c> (FineageStoredStateAuditErrors).
    /// </summary>
    [Fact]
    public async Task Store_edit_behind_the_ledger_reaches_the_divergence_alert_metrics()
    {
        var durable = new List<LedgerEntry>();
        var store = new DurableStore();
        var first = new WorldBench(accounts: store.Proxy, store: store.Inner, ledgerJournal: new TxJournal(durable));
        var player = await first.EnterAsync(items: [new StoredItem(Adena, 100, -1)]);
        Assert.Null(await first.World.TryGiveItemAsync(player.Session, Adena, 1, CancellationToken.None));
        Assert.True(first.World.FlushDirtyInventoriesForTests());
        Assert.True(first.World.DrainEconomyPersistForTests());
        using var metrics = new Fineage.Protocol.Telemetry.MetricsCollector();
        string phase = "inject-" + Guid.NewGuid().ToString("N")[..8];

        Assert.True(first.World.ReconcileEconomyAndLog(phase, _ => "log").Ok);
        var rows = store.Inner.FindByCharacterId(player.ObjectId)!.Items!
            .Select(row => row.ItemId == Adena ? row with { Count = row.Count + 1_000 } : row).ToList();
        Assert.True(store.Inner.TryReplaceItems(player.Ticket.Account, player.ObjectId, rows));
        var tampered = first.World.ReconcileEconomyAndLog(phase, _ => "log");
        Assert.Contains(tampered.Violations, v => v.Code == EconomyReconcileViolation.StoreDivergence);

        var second = new WorldBench(accounts: store.Proxy, store: store.Inner, ledgerJournal: new TxJournal(durable));
        second.World.AuditStoredStateAtStartup(_ => "log");

        string text = Fineage.Protocol.Telemetry.MetricsText.Prometheus(metrics.Snapshot(), 0, 0, Fineage.Protocol.Telemetry.MetricsRole.Zone);
        Assert.Contains($"fineage_economy_reconcile_runs_total{{phase=\"{phase}\",result=\"ok\"}} 1\n", text, StringComparison.Ordinal);
        Assert.Contains($"fineage_economy_reconcile_runs_total{{phase=\"{phase}\",result=\"violations\"}} 1\n", text, StringComparison.Ordinal);
        Assert.Contains($"fineage_economy_reconcile_violations_total{{phase=\"{phase}\",code=\"store-divergence\"}} ", text, StringComparison.Ordinal);
        Assert.Matches(
            $@"(?m)^fineage_state_audit_issues_total\{{code=""{StoredStateIssue.LedgerDivergence}"",container=""inventory"",severity=""error""\}} [1-9]",
            text);
    }

    [Fact]
    public void Ledger_account_without_a_stored_owner_is_an_orphan()
    {
        var journal = new MemoryLedgerJournal();
        var ledger = new EconomyLedger(journal, 3_600_000);
        Assert.True(ledger.Submit(new LedgerTransaction(new LedgerTxId(1, 0), LedgerTxKind.Adjustment, 0, 0, 0,
            [
                new LedgerPosting(LedgerAccount.Of(SystemAccount.Untracked), Adena, -3),
                new LedgerPosting(LedgerAccount.Warehouse(424242), Adena, 3),
            ])).Ok);
        var issues = StoredStateAudit.Ledger(ledger, [], [], [], TestCatalogs.Game.Items);
        var orphan = Assert.Single(issues);
        Assert.Equal(StoredStateIssue.LedgerOrphan, orphan.Code);
        Assert.Equal(424242, orphan.Id);
    }

    [Fact]
    public void Pre_ledger_bag_is_a_notice_not_an_error()
    {
        var bench = new WorldBench();
        Assert.True(bench.Store.TryAddAccount("old", "hash", "salt", 1, null) == AddAccountStatus.Created);
        Assert.Equal(CreateCharacterStatus.Ok, bench.Store.TryAddCharacter("old", "Old", 0, 0, 0, out _, [new StoredItem(Adena, 9, -1)]));
        var report = bench.World.AuditStoredState();
        Assert.Empty(report.Errors);
        var notice = Assert.Single(report.Issues);
        Assert.Equal(StoredStateIssue.Unjournaled, notice.Code);
        Assert.Equal(StoredStateSeverity.Notice, notice.Severity);
    }

    /// <summary>
    /// Journal with the Postgres contract: <see cref="ILedgerJournal.Append"/> buffers, and only a successful
    /// <see cref="ILedgerJournal.CommitWith"/> makes the rows durable in <c>durable</c>, which outlives the world.
    /// </summary>
    internal sealed class TxJournal(List<LedgerEntry> durable) : MemoryLedgerJournal, ILedgerJournal
    {
        readonly List<LedgerEntry> _buffer = [];
        readonly Lock _sync = new();

        IReadOnlyList<LedgerEntry> ILedgerJournal.ReadAll()
        {
            lock (durable)
                return durable.ToList();
        }

        public override void Append(LedgerEntry entry)
        {
            lock (_sync)
                _buffer.Add(entry);
        }

        public override int PendingFlushCount
        {
            get
            {
                lock (_sync)
                    return _buffer.Count;
            }
        }

        public override void Flush() => ((ILedgerJournal)this).CommitWith(_ => { });

        void ILedgerJournal.CommitWith(Action<IReadOnlyList<LedgerEntry>> write) =>
            ((ILedgerJournal)this).CommitThrough(long.MaxValue, write);

        /// <summary>The same prefix rule as the Postgres journal.</summary>
        void ILedgerJournal.CommitThrough(long position, Action<IReadOnlyList<LedgerEntry>> write)
        {
            List<LedgerEntry> pending;
            lock (_sync)
                pending = _buffer.TakeWhile(entry => entry.Position <= position).ToList();
            write(pending);
            lock (durable)
                durable.AddRange(pending);
            lock (_sync)
                _buffer.RemoveRange(0, pending.Count);
        }
    }

    /// <summary>Memory store whose economy commit takes the journal rows with the bags, all or nothing.</summary>
    internal sealed class DurableStore
    {
        public MemoryAccountStore Inner { get; } = MemoryAccountStore.InMemory();
        public IAccountStore Proxy { get; }
        public bool FailCommits { get; set; }

        /// <summary>When set, an economy commit waits here (after <see cref="Entered"/>) before it writes.</summary>
        public ManualResetEventSlim? Gate { get; set; }
        public ManualResetEventSlim Entered { get; } = new(false);

        /// <summary>A commit whose bags match dies inside the transaction.</summary>
        public Func<IReadOnlyList<EconomyBagWrite>, bool>? FailWhen { get; set; }

        int _failed;
        public int Failed => Volatile.Read(ref _failed);

        public DurableStore()
        {
            var proxy = DispatchProxy.Create<IAccountStore, Forward>()!;
            ((Forward)(object)proxy).Init(this);
            Proxy = proxy;
        }

        class Forward : DispatchProxy
        {
            DurableStore _owner = null!;

            public void Init(DurableStore owner) => _owner = owner;

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                ArgumentNullException.ThrowIfNull(targetMethod);
                if (targetMethod.Name == nameof(IAccountStore.CommitEconomy))
                {
                    if (_owner.Gate is { } gate)
                    {
                        _owner.Entered.Set();
                        gate.Wait();
                    }

                    if (_owner.FailCommits || _owner.FailWhen?.Invoke((IReadOnlyList<EconomyBagWrite>)args![1]!) == true)
                    {
                        Interlocked.Increment(ref _owner._failed);
                        throw new InvalidOperationException("simulated crash inside the economy commit");
                    }

                    // The entries are the journal's: TxJournal keeps them once this returns.
                    _owner.Inner.CommitEconomy([], (IReadOnlyList<EconomyBagWrite>)args![1]!, (IReadOnlyList<WorldStateDocument>?)args[2]);
                    return null;
                }

                try
                {
                    return targetMethod.Invoke(_owner.Inner, args);
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    throw ex.InnerException;
                }
            }
        }
    }
}

