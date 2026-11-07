using Fineage.Game;
using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The authoritative grant into the bag: an admin grant reaches the client as an InventoryUpdate, persists on
/// leave, and is refused before the character entered the world. Ported off the V2 wire; the assertions read the
/// recorded <see cref="INetPresenceBridge"/> fanout.
/// </summary>
public sealed class NetGrantItemTests
{
    const int Adena = 57;

    [Fact]
    public async Task Grant_adds_adena_and_persists_on_leave()
    {
        var bench = new WorldBench();
        BenchPlayer actor = await bench.EnterAsync("grant", "Merchant");

        Assert.Null(await bench.World.TryGrantItemAsync(
            actor.Session,
            new GrantItemRequestMsg { ItemId = Adena, Count = 25 },
            CancellationToken.None));

        var update = Assert.IsType<InventoryUpdateNetMsg>(
            bench.Rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate)).Last()[1]);
        var row = Assert.Single(update.Changes);
        Assert.Equal(InventoryChange.Add, row.Change);
        Assert.Equal(Adena, row.ItemId);
        Assert.Equal(25, row.Count);
        Assert.Equal(-1, row.EquippedSlot);

        // Grant does not write the bag eagerly (Mobius persists it on leave); the row must still be durable.
        await bench.World.LeaveAsync(actor.Session, CancellationToken.None);
        var saved = bench.Store.FindById(actor.Ticket.Account, actor.Ticket.CharacterId);
        Assert.Contains(saved!.Items!, item => item.ItemId == Adena && item.Count >= 25);
    }

    [Fact]
    public async Task Grant_before_enter_is_refused()
    {
        var bench = new WorldBench();
        var session = new ZoneSession();

        Assert.Equal("world", await bench.World.TryGrantItemAsync(
            session,
            new GrantItemRequestMsg { ItemId = Adena, Count = 1 },
            CancellationToken.None));
        Assert.Empty(bench.Rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate)));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(Adena, 0)]
    [InlineData(Adena, ZoneWorld.MaxGrantCount + 1)]
    [InlineData(999_999, 1)]
    public async Task Grant_rejects_bad_item_or_count(int itemId, int count)
    {
        var bench = new WorldBench();
        BenchPlayer actor = await bench.EnterAsync("grant", "Merchant");

        Assert.Equal("bad_request", await bench.World.TryGrantItemAsync(
            actor.Session,
            new GrantItemRequestMsg { ItemId = itemId, Count = count },
            CancellationToken.None));
        Assert.Empty(bench.Rec.Of(nameof(INetPresenceBridge.FanoutInventoryUpdate)));
    }
}
