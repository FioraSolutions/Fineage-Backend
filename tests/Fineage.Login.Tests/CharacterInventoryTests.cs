using Fineage.Login;
using Xunit;

namespace Fineage.Login.Tests;

public class CharacterInventoryTests
{
    [Fact]
    public void Create_stores_the_starter_kit_once()
    {
        var store = JsonAccountStore.InMemory();
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register("alice", "secret1").Ok);

        var created = service.CreateHuman("alice", "Aria", HumanClasses.Fighter);
        Assert.True(created.Ok, created.Message);
        var items = created.Value!.Items;
        Assert.NotNull(items);
        Assert.Contains(items, item => item.ItemId == 2369 && item.Count == 1 && item.EquippedSlot == 7);
        Assert.Contains(items, item => item.ItemId == 1146 && item.EquippedSlot == 10);
        Assert.Contains(items, item => item.ItemId == 1147 && item.EquippedSlot == 11);
        Assert.Contains(items, item => item.ItemId == 10 && item.EquippedSlot == -1);
        Assert.Contains(items, item => item.ItemId == 5588 && item.EquippedSlot == -1);
        Assert.Equal(5, items.Count);

        var again = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter);
        Assert.True(again.Ok, again.Message);
        var reloaded = store.FindById("alice", created.Value.Id);
        Assert.Equal(5, reloaded!.Items!.Count);
        Assert.Equal(2369, reloaded.Items[0].ItemId);
    }

    [Fact]
    public void Existing_character_is_not_given_a_second_kit()
    {
        var store = JsonAccountStore.InMemory();
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var created = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter);
        Assert.True(created.Ok, created.Message);
        Assert.True(store.TryReplaceItems("alice", created.Value!.CharacterId, [new StoredItem(57, 40, -1)]));

        var again = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter);
        Assert.True(again.Ok, again.Message);
        var saved = store.FindById("alice", created.Value.CharacterId);
        var only = Assert.Single(saved!.Items!);
        Assert.Equal(57, only.ItemId);
        Assert.Equal(40, only.Count);
        Assert.Equal(-1, only.EquippedSlot);
    }

    [Fact]
    public void Kit_roundtrips_through_the_account_file()
    {
        var path = Path.Combine(Path.GetTempPath(), "fineage-accounts-" + Guid.NewGuid().ToString("n") + ".json");
        try
        {
            var service = new LoginService(JsonAccountStore.Open(path), new HandoffTokens(HandoffTokens.DefaultSecret));
            var created = service.Play("alice", "secret1", "MageOne", HumanClasses.Mystic);
            Assert.True(created.Ok, created.Message);

            var reopened = JsonAccountStore.Open(path);
            var saved = reopened.FindById("alice", created.Value!.CharacterId);
            Assert.NotNull(saved?.Items);
            Assert.Contains(saved.Items, item => item.ItemId == 6 && item.EquippedSlot == 7);
            Assert.Contains(saved.Items, item => item.ItemId == 425 && item.EquippedSlot == 10);
            Assert.Contains(saved.Items, item => item.ItemId == 461 && item.EquippedSlot == 11);
            Assert.Contains(saved.Items, item => item.ItemId == 5588 && item.EquippedSlot == -1);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
