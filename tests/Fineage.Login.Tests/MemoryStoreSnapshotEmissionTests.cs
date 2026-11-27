using Fineage.Accounts;
using Fineage.Login;
using Xunit;

namespace Fineage.Login.Tests;

public sealed class MemoryStoreSnapshotEmissionTests
{
    [Fact]
    public void Silent_mutations_skip_the_snapshot_only_on_their_own_thread()
    {
        var store = MemoryAccountStore.InMemory();
        int snapshots = 0;
        store.AfterSaveSnapshot = _ => Interlocked.Increment(ref snapshots);
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));

        Assert.True(login.Register("loud", "pass1234").Ok);
        int afterLoud = Volatile.Read(ref snapshots);
        Assert.True(afterLoud >= 1);

        store.EmitSnapshots = false;
        try
        {
            Assert.True(login.Register("quiet", "pass1234").Ok);
            Assert.Equal(afterLoud, Volatile.Read(ref snapshots));

            // Another thread is not silenced by this thread's flag.
            bool otherOk = false;
            var other = new Thread(() => otherOk = login.Register("other", "pass1234").Ok);
            other.Start();
            other.Join();
            Assert.True(otherOk);
            Assert.True(Volatile.Read(ref snapshots) > afterLoud);
        }
        finally
        {
            store.EmitSnapshots = true;
        }
    }

    [Fact]
    public void Snapshot_callback_runs_after_the_memory_lock_is_released()
    {
        var store = MemoryAccountStore.InMemory();
        bool callbackHeldStoreLock = true;
        store.AfterSaveSnapshot = _ => callbackHeldStoreLock = Monitor.IsEntered(store.SyncRoot);

        Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("loud", "hash", "salt", 1, email: null));

        Assert.False(callbackHeldStoreLock);
    }

    /// <summary>
    /// Register persists the account through <see cref="MemoryAccountStore.AfterSaveSnapshot"/>; first character
    /// creation reserves starter-kit ids with the durable gate held and only then reads the memory counter
    /// (<see cref="PostgresAccountStore.ReserveItemObjectIds"/>). If the callback runs under the memory lock the two
    /// paths close a cycle. This forces the exact interleaving instead of relying on thread scheduling.
    /// </summary>
    [Fact]
    public async Task Concurrent_register_and_item_id_reservation_do_not_deadlock()
    {
        var store = MemoryAccountStore.InMemory();
        var persistGate = new object();
        using var callbackEntered = new ManualResetEventSlim();
        using var persistGateHeld = new ManualResetEventSlim();
        using var releaseCallback = new ManualResetEventSlim();
        store.AfterSaveSnapshot = _ =>
        {
            callbackEntered.Set();
            releaseCallback.Wait(TimeSpan.FromSeconds(5));
            lock (persistGate)
            {
            }
        };
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));

        var register = Task.Run(() => login.Register("deadlock", "pass1234"));
        Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(2)), "snapshot callback did not run");

        // Mirrors PostgresAccountStore.ReserveItemObjectIds: durable gate first, memory gate second.
        var reserve = Task.Run(() =>
        {
            lock (persistGate)
            {
                persistGateHeld.Set();
                _ = store.NextItemObjectId;
            }
        });
        Assert.True(persistGateHeld.Wait(TimeSpan.FromSeconds(2)), "reservation did not take the durable gate");

        releaseCallback.Set();

        var both = Task.WhenAll(register, reserve);
        try
        {
            await both.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            Assert.Fail("register and item-id reservation deadlocked");
        }

        var registered = await register;
        Assert.True(registered.Ok, registered.Message);
    }
}
