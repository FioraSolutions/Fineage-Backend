using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneInventoryPersistTests
{
    [Fact]
    public async Task Enter_loads_the_saved_bag_and_leave_writes_it_back()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, tokens);
        var ticket = login.Play("alice", "secret1", "Aria", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);
        Assert.True(store.TryReplaceItems(ticket.Value.Account, ticket.Value.CharacterId, [new StoredItem(57, 40, -1)]));

        var world = new ZoneWorld(accounts: store);
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.Value.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);

        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        stream.Position = 0;
        var list = await ReadItemListAsync(stream);
        var only = Assert.Single(list.Items);
        Assert.Equal(57, only.ItemId);
        Assert.Equal(40, only.Count);
        Assert.Equal(-1, only.EquippedSlot);
        Assert.True(only.ObjectId >= 100_000);

        await world.LeaveAsync(session, CancellationToken.None);
        var saved = store.FindById(ticket.Value.Account, ticket.Value.CharacterId);
        var stored = Assert.Single(saved!.Items!);
        Assert.Equal(57, stored.ItemId);
        Assert.Equal(40, stored.Count);
        Assert.Equal(-1, stored.EquippedSlot);
    }

    [Fact]
    public async Task Legacy_character_is_granted_the_kit_once()
    {
        var store = JsonAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("alice", "secret1").Ok);
        var status = store.TryAddCharacter("alice", "OldOne", HumanClasses.Fighter, 0, 0, out var created);
        Assert.Equal(CreateCharacterStatus.Ok, status);
        Assert.Null(created!.Items);

        var world = new ZoneWorld(accounts: store);
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(created.Id, "alice", "OldOne", HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        var granted = store.FindById("alice", created.Id);
        Assert.Contains(granted!.Items!, item => item.ItemId == 2369 && item.EquippedSlot == 7);
        int count = granted.Items!.Count;

        await world.LeaveAsync(session, CancellationToken.None);
        long mark = stream.Length;
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        stream.Position = mark;
        var list = await ReadItemListAsync(stream);
        Assert.Equal(count, list.Items.Length);
        Assert.Equal(1, list.Items.Count(item => item.ItemId == 2369));
        Assert.Equal(count, store.FindById("alice", created.Id)!.Items!.Count);
    }

    static async Task<ItemListMsg> ReadItemListAsync(Stream stream)
    {
        while (true)
        {
            var frame = await FrameIO.ReadAsync(stream);
            Assert.NotNull(frame);
            if (frame!.Value.Type == (ushort)MsgType.ItemList)
                return MessagePackSerializer.Deserialize<ItemListMsg>(frame.Value.Body);
        }
    }
}
