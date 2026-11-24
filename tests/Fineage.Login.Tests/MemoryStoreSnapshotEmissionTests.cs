using Fineage.Accounts;
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
}
