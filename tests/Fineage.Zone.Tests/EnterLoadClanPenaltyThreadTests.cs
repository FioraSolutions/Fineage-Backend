using Fineage.Accounts;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #1116: the enter load runs on the thread pool, several at once, and it wrote the stored clan join/create penalties
/// into the clan actor's plain dictionaries. A reconnect storm corrupted them (IndexOutOfRangeException in
/// Dictionary.set_Item) and every later enter failed. The penalties are now remembered when the player is inserted, on
/// the simulation thread, and a write from anywhere else in a driven world throws.
/// </summary>
public sealed class EnterLoadClanPenaltyThreadTests
{
    [Fact]
    public async Task Relog_in_a_driven_world_remembers_the_stored_clan_penalties_on_the_simulation_thread()
    {
        var bench = new WorldBench();
        var player = await bench.EnterAsync();
        await bench.World.LeaveAsync(player.Session, CancellationToken.None);
        long join = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds();
        long create = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeMilliseconds();
        Assert.True(bench.Store.TrySetCharacterClanState(
            new CharacterClanState(player.Ticket.Account, player.ObjectId, 0, join, create)));

        bench.World.DriveForTest();
        using var shutdown = new CancellationTokenSource();
        bench.World.StartSimulation(shutdown.Token);
        try
        {
            var relog = Relog(player);
            bool entered = await Task.Run(() => bench.World.TryEnterAsync(relog, CancellationToken.None))
                .WaitAsync(TimeSpan.FromSeconds(30));

            Assert.True(entered, relog.EnterRefusal);
            Assert.True(bench.World.TryGetClanCreateExpiryForTests(player.ObjectId, out long remembered));
            Assert.Equal(create, remembered);
        }
        finally
        {
            shutdown.Cancel();
        }
    }

    static ZoneSession Relog(BenchPlayer player)
    {
        var session = new ZoneSession();
        session.Bind(player.Ticket.CharacterId, player.Ticket.Account, player.Ticket.CharacterName,
            player.Ticket.ClassId, player.Ticket.Race, player.Ticket.Sex);
        return session;
    }
}
