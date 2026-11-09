using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class ItemEquipSkillCatalogTests
{
    [Fact]
    public void Forgotten_blade_focus_maps_to_sa_focus()
    {
        var catalog = ItemEquipSkillCatalog.LoadDefault();
        Assert.True(catalog.TryGet(6583, out var skills));
        Assert.Contains(skills, s => s.Id == 3566 && s.Level == 1);
    }

    [Fact]
    public void Collect_equipped_merges_weapon_sa()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        bag.Equip(bag.Add(6583)); // Forgotten Blade - Focus
        var map = new Dictionary<int, int>();
        ItemEquipSkillCatalog.LoadDefault().CollectEquipped(bag, map);
        Assert.Equal(1, map[3566]);
    }
}
