using Fineage.Game;
using Fineage.Login;
using Xunit;
using Fineage.Accounts;

namespace Fineage.Login.Tests;

public class StoredItemObjectIdTests
{
    [Fact]
    public void Create_assigns_durable_object_ids_and_restore_keeps_them()
    {
        var store = MemoryAccountStore.InMemory();
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register("alice", "secret1").Ok);

        var created = service.CreateHuman("alice", "Aria", HumanClasses.Fighter);
        Assert.True(created.Ok, created.Message);
        var items = created.Value!.Items!;
        Assert.All(items, item => Assert.True(item.ObjectId >= PlayerInventory.FirstObjectId));
        Assert.Equal(items.Count, items.Select(item => item.ObjectId).Distinct().Count());

        int swordOid = items.Single(item => item.ItemId == 219).ObjectId;
        var ids = new ItemObjectIdAllocator(PlayerInventory.FirstObjectId);
        var restored = CharacterInventory.Restore(GameData.LoadDefault().Items, ids, items);
        Assert.Equal(swordOid, restored.Items.Single(item => item.ItemId == 219).ObjectId);
        Assert.True(ids.PeekNext() > swordOid);
    }

    [Fact]
    public void Replace_items_without_object_id_allocates_once()
    {
        var store = MemoryAccountStore.InMemory();
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register("alice", "secret1").Ok);
        var created = service.CreateHuman("alice", "Aria", HumanClasses.Fighter);
        Assert.True(created.Ok);

        Assert.True(store.TryReplaceItems("alice", created.Value!.Id, [new StoredItem(57, 40, -1)]));
        var again = store.FindById("alice", created.Value.Id)!;
        Assert.Single(again.Items!);
        Assert.True(again.Items![0].ObjectId >= PlayerInventory.FirstObjectId);

        int oid = again.Items[0].ObjectId;
        Assert.True(store.TryReplaceItems("alice", created.Value.Id, [new StoredItem(57, 41, -1, 0, oid)]));
        Assert.Equal(oid, store.FindById("alice", created.Value.Id)!.Items![0].ObjectId);
        Assert.Equal(41, store.FindById("alice", created.Value.Id)!.Items![0].Count);
    }
}
