using Fineage.Game;
using Xunit;

namespace Fineage.Game.Tests;

public class ItemCatalogTests
{
    [Fact]
    public void Paperdoll_uses_mobius_indexes()
    {
        Assert.Equal(0, (int)PaperdollSlot.Under);
        Assert.Equal(1, (int)PaperdollSlot.Lear);
        Assert.Equal(2, (int)PaperdollSlot.Rear);
        Assert.Equal(3, (int)PaperdollSlot.Neck);
        Assert.Equal(4, (int)PaperdollSlot.LFinger);
        Assert.Equal(5, (int)PaperdollSlot.RFinger);
        Assert.Equal(6, (int)PaperdollSlot.Head);
        Assert.Equal(7, (int)PaperdollSlot.RHand);
        Assert.Equal(8, (int)PaperdollSlot.LHand);
        Assert.Equal(9, (int)PaperdollSlot.Gloves);
        Assert.Equal(10, (int)PaperdollSlot.Chest);
        Assert.Equal(11, (int)PaperdollSlot.Legs);
        Assert.Equal(12, (int)PaperdollSlot.Feet);
        Assert.Equal(13, (int)PaperdollSlot.Cloak);
        Assert.Equal(14, (int)PaperdollSlot.Face);
        Assert.Equal(15, (int)PaperdollSlot.Hair);
        Assert.Equal(16, (int)PaperdollSlot.Hair2);
        Assert.Equal(17, Enum.GetValues<PaperdollSlot>().Length);
    }

    [Theory]
    [InlineData(2369, "Squire's Sword", ItemKind.Weapon, BodySlot.RightHand, WeaponType.Sword, ArmorType.None, false, false, 0, "icon.weapon_squires_sword_i00")]
    [InlineData(2368, "Training Gloves", ItemKind.Weapon, BodySlot.LrHand, WeaponType.Fist, ArmorType.None, false, false, 0, "icon.weapon_training_gloves_i00")]
    [InlineData(2370, "Guild Member's Club", ItemKind.Weapon, BodySlot.RightHand, WeaponType.Blunt, ArmorType.None, false, false, 0, "icon.weapon_club_i00")]
    [InlineData(10, "Dagger", ItemKind.Weapon, BodySlot.RightHand, WeaponType.Dagger, ArmorType.None, false, false, 0, "icon.weapon_dagger_i00")]
    [InlineData(6, "Apprentice's Wand", ItemKind.Weapon, BodySlot.RightHand, WeaponType.Blunt, ArmorType.None, true, false, 0, "icon.weapon_apprentices_wand_i00")]
    [InlineData(1146, "Squire's Shirt", ItemKind.Armor, BodySlot.Chest, WeaponType.None, ArmorType.Light, false, false, 33, "icon.armor_t01_u_i00")]
    [InlineData(1147, "Squire's Pants", ItemKind.Armor, BodySlot.Legs, WeaponType.None, ArmorType.Light, false, false, 20, "icon.armor_t01_l_i00")]
    [InlineData(425, "Apprentice's Tunic", ItemKind.Armor, BodySlot.Chest, WeaponType.None, ArmorType.Magic, false, false, 17, "icon.armor_t01_u_i00")]
    [InlineData(461, "Apprentice's Stockings", ItemKind.Armor, BodySlot.Legs, WeaponType.None, ArmorType.Magic, false, false, 10, "icon.armor_t01_l_i00")]
    [InlineData(5588, "Tutorial Guide", ItemKind.Etc, BodySlot.None, WeaponType.None, ArmorType.None, false, false, 0, "icon.etc_spellbook_gray_i00")]
    [InlineData(57, "Adena", ItemKind.Etc, BodySlot.None, WeaponType.None, ArmorType.None, false, true, 0, "icon.etc_adena_i00")]
    [InlineData(6651, "Noblesse Gate Pass", ItemKind.Etc, BodySlot.None, WeaponType.None, ArmorType.None, false, true, 0, "icon.etc_noblesse_gate_pass_i00")]
    public void Templates_match_the_hand_written_catalog(
        int id,
        string name,
        ItemKind kind,
        BodySlot body,
        WeaponType weapon,
        ArmorType armor,
        bool magic,
        bool stackable,
        int pDef,
        string icon)
    {
        var template = GameData.LoadDefault().Items.Get(id);
        Assert.Equal(name, template.Name);
        Assert.Equal(kind, template.Kind);
        Assert.Equal(body, template.BodySlot);
        Assert.Equal(weapon, template.WeaponType);
        Assert.Equal(armor, template.ArmorType);
        Assert.Equal(EtcItemType.None, template.EtcType);
        Assert.Equal(magic, template.MagicWeapon);
        Assert.Equal(stackable, template.Stackable);
        Assert.Equal(pDef, template.PDef);
        Assert.Equal(icon, template.Icon);
        Assert.Equal(kind != ItemKind.Etc, template.Equippable);
    }

    [Fact]
    public void Catalog_has_the_starter_set_and_adena()
    {
        var ids = GameData.LoadDefault().Items.Templates.Select(item => item.Id).ToArray();
        int[] starter =
        [
            2369, 2368, 2370, 10, 6, 1146, 1147, 425, 461, 5588, 57, 736, 1864, 6611, 7682, 8923, 7850, 7860, 6834, 6841,
            58, 73, 79, 98, 123, 911, 2390, 6364, 1458, 1459, 1460, 1461, 1462,
            955, 956, 957, 958, 951, 952, 953, 954, 947, 948, 949, 950, 729, 730, 731, 732,
            959, 960, 961, 962, 6569, 6570, 6571, 6572, 6573, 6574, 6575, 6576, 6577, 6578,
            6360, 6361, 6362, 6388, 6651
        ];
        Assert.Equal(starter, ids.Take(starter.Length));
        Assert.Contains(1, ids);
    }
}
