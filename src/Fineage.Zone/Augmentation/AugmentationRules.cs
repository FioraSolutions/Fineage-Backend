using Fineage.Game;

namespace Fineage.Zone.Augmentation;

/// <summary>Item variation (life stone refine).</summary>
public static class AugmentationRules
{
    static readonly int[] Blacklist = [6656, 6657, 6658, 6659, 6660, 6661, 6662, 8191, 10170, 10314];

    public static AugmentationCatalog? ActiveCatalog { get; private set; }

    public static void BindCatalog(AugmentationCatalog catalog)
    {
        ActiveCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public static bool CanRefine => ActiveCatalog is { IsLoaded: true };

    public static bool IsAugmented(ItemInstance item) => item.IsAugmented;

    public static int CombineVariationId(int option1, int option2) => (option2 << 16) | (option1 & 0xFFFF);

    /// <summary>Mobius packs option id into a single 32-bit variation id; decode when persisting.</summary>
    public static (int Option1, int Option2) SplitVariationId(int variationId) =>
        (variationId & 0xFFFF, variationId >> 16);

    public static bool IsAugmentableWeapon(ItemTemplate template) =>
        template.Kind == ItemKind.Weapon &&
        template.WeaponType is not (WeaponType.None or WeaponType.FishingRod) &&
        template.Crystal >= CrystalGrade.C &&
        !Blacklist.Contains(template.Id);

    public static bool IsAugmentableAccessory(ItemTemplate template)
    {
        if (template.Kind != ItemKind.Armor)
            return false;
        return template.BodySlot is BodySlot.LeftFinger or BodySlot.RightFinger or BodySlot.BothFingers
            or BodySlot.LeftEar or BodySlot.RightEar or BodySlot.BothEars or BodySlot.Neck;
    }

    public static bool ValidateTarget(ItemInstance? item)
    {
        if (item == null || item.Template.Stackable)
            return false;
        if (IsAugmented(item))
            return false;
        if (item.Template.QuestItem)
            return false;
        return IsAugmentableWeapon(item.Template) || IsAugmentableAccessory(item.Template);
    }

    public static int CancelAdenaFee(ItemTemplate template)
    {
        return template.Crystal switch
        {
            CrystalGrade.C => template.CrystalCount < 1720 ? 95_000 :
                template.CrystalCount < 2452 ? 150_000 : 210_000,
            CrystalGrade.B => template.CrystalCount < 1746 ? 240_000 : 270_000,
            CrystalGrade.A => template.CrystalCount < 2160 ? 330_000 :
                template.CrystalCount < 2824 ? 390_000 : 420_000,
            CrystalGrade.S => 480_000,
            _ => 0
        };
    }
}