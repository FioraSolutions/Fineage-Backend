using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The bag round-trip between the store and the zone: enter loads what was saved, leave writes it back, and a
/// character created before the starter kit existed is granted it exactly once. Ported off the V2 wire; the
/// assertions read the sheet <see cref="NetEnterWorldProjector"/> builds for the client.
/// </summary>
public sealed class NetInventoryPersistTests
{
    const int Adena = 57;
    const int StarterWeapon = 219;
    const int RightHand = 7;

    [Fact]
    public async Task Enter_loads_the_saved_bag_and_leave_writes_it_back()
    {
        var bench = new WorldBench();
        BenchPlayer actor = await bench.EnterAsync("alice", "Aria", [new StoredItem(Adena, 40, -1)]);

        var only = Assert.Single(EnterItems(bench, actor));
        Assert.Equal(Adena, only.ItemId);
        Assert.Equal(40, only.Count);
        Assert.Equal(-1, only.EquippedSlot);
        Assert.True(only.ObjectId >= 100_000);

        await bench.World.LeaveAsync(actor.Session, CancellationToken.None);
        var stored = Assert.Single(actor.Saved());
        Assert.Equal(Adena, stored.ItemId);
        Assert.Equal(40, stored.Count);
        Assert.Equal(-1, stored.EquippedSlot);
    }

    [Fact]
    public async Task Legacy_character_is_granted_the_kit_once()
    {
        var bench = new WorldBench();
        Assert.True(bench.Store.TryAddAccount("alice", "hash", "salt", 1, null) == AddAccountStatus.Created);
        var status = bench.Store.TryAddCharacter("alice", "OldOne", HumanClasses.Fighter, 0, 0, out var created);
        Assert.Equal(CreateCharacterStatus.Ok, status);
        Assert.Null(created!.Items);

        BenchPlayer actor = await EnterExisting(bench, created.Id, "alice", "OldOne");

        var granted = bench.Store.FindById("alice", created.Id)!;
        Assert.Contains(granted.Items!, item => item.ItemId == StarterWeapon && item.EquippedSlot == RightHand);
        int count = granted.Items!.Count;
        Assert.Equal(count, EnterItems(bench, actor).Length);

        await bench.World.LeaveAsync(actor.Session, CancellationToken.None);
        BenchPlayer again = await EnterExisting(bench, created.Id, "alice", "OldOne");

        var rows = EnterItems(bench, again);
        Assert.Equal(count, rows.Length);
        Assert.Equal(1, rows.Count(item => item.ItemId == StarterWeapon));
        Assert.Equal(count, bench.Store.FindById("alice", created.Id)!.Items!.Count);
    }

    /// <summary>The bag rows the client is handed on enter, straight from the live entity projection.</summary>
    static ItemListEntryNet[] EnterItems(WorldBench bench, BenchPlayer actor)
    {
        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var entity));
        var packets = NetEnterWorldProjector.FromEntity(
            entity, EntityId.FromObjectId(actor.ObjectId), bench.Now().ToUnixTimeMilliseconds());
        return packets.Items.Items;
    }

    /// <summary>
    /// Enters a character that already exists in the store, so the kit is decided by the world join rather than by
    /// whatever rows the bench would seed.
    /// </summary>
    static async Task<BenchPlayer> EnterExisting(WorldBench bench, int characterId, string account, string name)
    {
        var login = new LoginService(bench.Store, new HandoffTokens(HandoffTokens.DefaultSecret), bench.Now);
        var play = login.Select(account, characterId);
        Assert.True(play.Ok && play.Value != null, play.Message);
        HandoffTicket ticket = play.Value!;

        var session = new ZoneSession(new MemoryStream());
        session.Bind(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId, ticket.Race, ticket.Sex);
        Assert.True(await bench.World.TryEnterAsync(session, CancellationToken.None));
        Assert.True(bench.World.TryGetLivePlayer(session, out var player));
        bench.Rec.Calls.Clear();
        return new BenchPlayer(bench.World, bench.Store, session, player, ticket);
    }
}
