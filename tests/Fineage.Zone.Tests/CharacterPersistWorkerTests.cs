using System.Diagnostics;
using System.Reflection;
using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Login;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class CharacterPersistWorkerTests
{
    [Fact]
    public void Snapshots_write_off_the_caller_thread_and_coalesce_to_the_latest_version()
    {
        var store = MemoryAccountStore.InMemory();
        var created = Character(store, "alice", "Aria");
        var counting = CountingStore.Wrap(store);
        var warnings = new List<string>();
        var worker = new CharacterPersistWorker(counting.Proxy, warnings.Add, warnings.Add);
        var persist = new ZoneCharacterPersist(counting.Proxy, worker);
        worker.Start();
        worker.PauseForTests();

        persist.SavePresence(created.Account, created.Id, 1, 2, 3, null);
        persist.SavePresence(created.Account, created.Id, 4, 5, 6, null);
        persist.SavePresence(created.Account, created.Id, 7, 8, 9, null);
        Assert.Equal(1, worker.PendingCount);
        Assert.Equal(0, counting.PresenceWrites);
        Assert.NotEqual(7, store.FindById(created.Account, created.Id)!.X);

        worker.ResumeForTests();
        worker.Drain(TimeSpan.FromSeconds(5));
        worker.Stop();

        var saved = store.FindById(created.Account, created.Id);
        Assert.Equal(7, saved!.X);
        Assert.Equal(8, saved.Y);
        Assert.Equal(9, saved.Z);
        Assert.Equal(1, counting.PresenceWrites);
        Assert.NotEqual(Environment.CurrentManagedThreadId, worker.LastWriterThreadId);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Different_kinds_stay_in_version_order()
    {
        var store = MemoryAccountStore.InMemory();
        var created = Character(store, "bob", "Bram");
        var worker = new CharacterPersistWorker(store);
        var persist = new ZoneCharacterPersist(store, worker);
        worker.Start();
        worker.PauseForTests();

        persist.SaveVitals(created.Account, created.Id, hp: 11, mp: 22, cp: 33);
        persist.SaveProgress(created.Account, created.Id, level: 4, exp: 50, sp: 6,
            maxHp: 400, hp: 90, maxMp: 200, mp: 80, maxCp: 100, cp: 70);

        worker.ResumeForTests();
        worker.Drain(TimeSpan.FromSeconds(5));
        worker.Stop();

        var saved = store.FindById(created.Account, created.Id)!;
        Assert.Equal(4, saved.Level);
        Assert.Equal(90, saved.Hp);
        Assert.Equal(80, saved.Mp);
        Assert.Equal(70, saved.Cp);
    }

    [Fact]
    public void A_failed_snapshot_stays_pending_and_the_recovered_store_matches_the_latest()
    {
        var store = MemoryAccountStore.InMemory();
        var created = Character(store, "cara", "Cara");
        var flaky = FlakyPresenceStore.Wrap(store, failures: int.MaxValue);
        var errors = new List<string>();
        var worker = new CharacterPersistWorker(flaky.Proxy, errors.Add, errors.Add);
        var persist = new ZoneCharacterPersist(flaky.Proxy, worker);
        worker.Start();

        persist.SavePresence(created.Account, created.Id, 1, 2, 3, null);
        var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2;
        while (flaky.Attempts < 1 && Stopwatch.GetTimestamp() < until)
            Thread.Sleep(5);
        Assert.True(flaky.Attempts >= 1, "the store failure was not attempted");

        persist.SavePresence(created.Account, created.Id, 7, 8, 9, null);
        flaky.FailuresLeft = 0;
        Assert.True(worker.Drain(TimeSpan.FromSeconds(5)));
        worker.Stop();

        var saved = store.FindById(created.Account, created.Id);
        Assert.Equal(7, saved!.X);
        Assert.Equal(8, saved.Y);
        Assert.Equal(9, saved.Z);
        Assert.Contains(errors, line =>
            line.Contains($"character={created.Id}", StringComparison.Ordinal)
            && line.Contains("kind=SavePresence", StringComparison.Ordinal)
            && line.Contains("attempt=1", StringComparison.Ordinal)
            && line.Contains("IOException: presence update failed: disk full", StringComparison.Ordinal));
        Assert.DoesNotContain(errors, line => line.Contains("dropped", StringComparison.Ordinal));
    }

    [Fact]
    public void Pending_count_and_oldest_age_stay_visible_until_the_snapshot_lands()
    {
        var store = MemoryAccountStore.InMemory();
        var created = Character(store, "dara", "Dara");
        var worker = new CharacterPersistWorker(store);
        var persist = new ZoneCharacterPersist(store, worker);
        worker.Start();
        worker.PauseForTests();
        persist.SavePresence(created.Account, created.Id, 4, 5, 6, null);
        Thread.Sleep(40);

        Assert.Equal(1, worker.PendingCount);
        Assert.True(worker.OldestAgeMilliseconds >= 30);
        using var collector = new MetricsCollector();
        var snapshot = collector.Snapshot();
        Assert.Equal(1, snapshot.PersistPending);
        Assert.True(snapshot.PersistOldestAgeMs >= 30);
        string text = MetricsText.Prometheus(snapshot, 0, 0);
        Assert.Contains("fineage_persist_pending 1\n", text, StringComparison.Ordinal);
        Assert.Contains("fineage_persist_oldest_age_ms ", text, StringComparison.Ordinal);

        worker.ResumeForTests();
        worker.Stop();
        Assert.Equal(4, store.FindById(created.Account, created.Id)!.X);
        Assert.Equal(0, worker.PendingCount);
    }

    [Fact]
    public async Task Items_stay_on_the_caller_and_the_client_hears_success_only_after_commit()
    {
        var journal = new FailingFlushJournal();
        var bench = new WorldBench(ledgerJournal: journal);
        var player = await bench.EnterAsync(items: [new StoredItem(57, 50, -1)]);
        var other = await bench.EnterAsync();
        bench.World.StartCharacterSnapshotsForTests();
        bench.World.PauseCharacterSnapshotsForTests();
        journal.FailFlush = true;

        var destroyed = bench.World.ExecuteAsync(
            player.Session, new DestroyItemCommand(player.Slot(57), 1), CancellationToken.None);
        var selected = bench.World.ExecuteAsync(
            other.Session, new SelectTargetCommand(player.ObjectId), CancellationToken.None);
        bench.World.StepSimulation();

        Assert.Equal(ZoneWorld.EconomyCommitFailed, await destroyed);
        Assert.Null(await selected);
        Assert.Equal(50, player.Saved().Single(item => item.ItemId == 57).Count);
        bench.World.ResumeCharacterSnapshotsForTests();
        bench.World.StopCharacterSnapshotsForTests();
    }

    [Fact]
    public async Task Command_stage_records_each_phase()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        using var collector = new MetricsCollector();
        var reply = bench.World.ExecuteAsync(player.Session, new CancelTargetCommand(), CancellationToken.None);
        bench.World.StepSimulation();
        Assert.Null(await reply);

        var stages = collector.Snapshot().Stages.Select(stage => stage.Stage).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("commands.ingress_dequeue", stages);
        Assert.Contains("commands.apply", stages);
        Assert.Contains("commands.flush_dirty_inventory", stages);
        Assert.Contains("commands.fanout_seal", stages);
        Assert.Contains("commands.reply_completion", stages);
    }

    static CharacterRecord Character(MemoryAccountStore store, string account, string name)
    {
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play(account, "secret1", name, HumanClasses.Fighter).Value!;
        return store.FindById(ticket.Account, ticket.CharacterId)!;
    }

    sealed class FailingFlushJournal : MemoryLedgerJournal
    {
        int _pending;
        public bool FailFlush { get; set; }

        public override void Append(LedgerEntry entry)
        {
            base.Append(entry);
            _pending++;
        }

        public override int PendingFlushCount => _pending;

        public override void Flush()
        {
            if (FailFlush)
                throw new IOException("ledger insert failed: disk full");
            _pending = 0;
        }
    }

    class CountingStore : DispatchProxy
    {
        public IAccountStore Inner = null!;
        public int PresenceWrites;
        public IAccountStore Proxy => (IAccountStore)(object)this;

        public static CountingStore Wrap(IAccountStore inner)
        {
            var proxy = DispatchProxy.Create<IAccountStore, CountingStore>()!;
            ((CountingStore)(object)proxy).Inner = inner;
            return (CountingStore)(object)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IAccountStore.TrySavePresence))
                PresenceWrites++;
            return targetMethod.Invoke(Inner, args);
        }
    }

    class FlakyPresenceStore : DispatchProxy
    {
        public IAccountStore Inner = null!;
        public int FailuresLeft;
        public int Attempts;
        public IAccountStore Proxy => (IAccountStore)(object)this;

        public static FlakyPresenceStore Wrap(IAccountStore inner, int failures)
        {
            var proxy = DispatchProxy.Create<IAccountStore, FlakyPresenceStore>()!;
            var self = (FlakyPresenceStore)(object)proxy;
            self.Inner = inner;
            self.FailuresLeft = failures;
            return self;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IAccountStore.TrySavePresence))
            {
                Attempts++;
                if (FailuresLeft > 0)
                {
                    FailuresLeft--;
                    throw new IOException("presence update failed: disk full");
                }
            }

            return targetMethod.Invoke(Inner, args);
        }
    }
}
