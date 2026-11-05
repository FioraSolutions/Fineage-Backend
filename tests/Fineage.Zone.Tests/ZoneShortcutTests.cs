using Fineage.Game;
using Fineage.Login;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius ShortCuts on Net: register / delete persist per character and come back in ShortCutInit.</summary>
public sealed class ZoneShortcutTests
{
    static async Task<(ZoneWorld World, ZoneSession Session, ZoneEntity Player)> Enter(MemoryAccountStore store, int characterId, string account, string name)
    {
        var world = new ZoneWorld(accounts: store);
        var session = new ZoneSession();
        session.Bind(characterId, account, name, HumanClasses.Fighter);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session, out var player));
        return (world, session, player);
    }

    static (MemoryAccountStore Store, int CharacterId) NewCharacter()
    {
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("bar", "pass1234").Ok);
        var created = login.CreateHuman("bar", "Hotbar", HumanClasses.Fighter);
        return (store, created.Value!.Id);
    }

    [Fact]
    public async Task Skill_item_action_persist_across_relog()
    {
        var (store, id) = NewCharacter();
        var (world, session, player) = await Enter(store, id, "bar", "Hotbar");
        Assert.Empty(world.NetShortcutInit(session).Shortcuts);

        var skill = player.KnownSkills.First();
        var potion = player.Inventory!.Add(1060, 5);

        // Client claims level 99: server stores the known level.
        var reg = world.RegisterShortcut(session, StoredShortcut.TypeSkill, 0, skill.Key, 99);
        Assert.Null(reg.Error);
        Assert.Equal(skill.Value, reg.Register!.Level);
        Assert.Null(world.RegisterShortcut(session, StoredShortcut.TypeItem, 1, potion.ObjectId, 0).Error);
        Assert.Null(world.RegisterShortcut(session, StoredShortcut.TypeAction, 14, 2, 0).Error); // page 1 slot 2
        // Overwrite + delete.
        Assert.Null(world.RegisterShortcut(session, StoredShortcut.TypeAction, 5, 0x10, 0).Error);
        var del = world.DeleteShortcut(session, 5);
        Assert.Equal(5, del.DeleteSlot);

        await world.LeaveAsync(session, CancellationToken.None);

        var (world2, session2, _) = await Enter(store, id, "bar", "Hotbar");
        var init = world2.NetShortcutInit(session2).Shortcuts;
        Assert.Equal(3, init.Length);
        Assert.Equal((StoredShortcut.TypeSkill, 0, skill.Key, skill.Value), (init[0].Type, init[0].Slot, init[0].Id, init[0].Level));
        Assert.Equal((StoredShortcut.TypeItem, 1, potion.ObjectId), (init[1].Type, init[1].Slot, init[1].Id));
        Assert.Equal((StoredShortcut.TypeAction, 14, 2), (init[2].Type, init[2].Slot, init[2].Id));
        var saved = store.FindById("bar", id)!.Shortcuts!;
        Assert.Contains(saved, s => s.Page == 1 && s.Slot == 2);
    }

    [Fact]
    public async Task Rejects_unknown_skill_and_foreign_item_with_current_slot_state()
    {
        var (store, id) = NewCharacter();
        var (world, session, player) = await Enter(store, id, "bar", "Hotbar");

        var r1 = world.RegisterShortcut(session, StoredShortcut.TypeSkill, 3, 999_999, 1);
        Assert.NotNull(r1.Error);
        Assert.Equal(3, r1.DeleteSlot);

        var potion = player.Inventory!.Add(1060, 1);
        Assert.Null(world.RegisterShortcut(session, StoredShortcut.TypeItem, 3, potion.ObjectId, 0).Error);
        var r2 = world.RegisterShortcut(session, StoredShortcut.TypeItem, 3, 1, 0);
        Assert.NotNull(r2.Error);
        Assert.Equal(potion.ObjectId, r2.Register!.Id); // client rolls back to the saved slot

        Assert.NotNull(world.RegisterShortcut(session, StoredShortcut.TypeAction, 120, 2, 0).Error);
        Assert.NotNull(world.RegisterShortcut(session, 9, 0, 2, 0).Error);
    }

    [Fact]
    public async Task Item_shortcut_dropped_on_relog_when_item_is_gone()
    {
        var (store, id) = NewCharacter();
        var (world, session, player) = await Enter(store, id, "bar", "Hotbar");
        var potion = player.Inventory!.Add(1060, 1);
        Assert.Null(world.RegisterShortcut(session, StoredShortcut.TypeItem, 0, potion.ObjectId, 0).Error);
        player.Inventory.Remove(potion);
        await world.LeaveAsync(session, CancellationToken.None);

        var (world2, session2, _) = await Enter(store, id, "bar", "Hotbar");
        Assert.Empty(world2.NetShortcutInit(session2).Shortcuts);
        Assert.Empty(store.FindById("bar", id)!.Shortcuts!);
    }

    [Fact]
    public void Json_store_roundtrips_shortcuts_through_snapshot()
    {
        var (store, id) = NewCharacter();
        Assert.True(store.TryReplaceShortcuts("bar", id,
        [
            new StoredShortcut(0, 0, StoredShortcut.TypeSkill, 3, 1),
            new StoredShortcut(11, 4, StoredShortcut.TypeAction, 2, 0)
        ]));
        Assert.False(store.TryReplaceShortcuts("bar", id, [new StoredShortcut(12, 0, 1, 1, 0)]));
        Assert.False(store.TryReplaceShortcuts("bar", id,
            [new StoredShortcut(0, 0, 3, 1, 0), new StoredShortcut(0, 0, 3, 2, 0)]));

        var reloaded = MemoryAccountStore.FromSnapshot(store.ExportSnapshot());
        var rows = reloaded.FindById("bar", id)!.Shortcuts!;
        Assert.Equal(2, rows.Count);
        Assert.Equal(59, rows[1].WorldSlot);
    }
}
