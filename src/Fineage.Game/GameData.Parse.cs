using System.Text.Json;
using Fineage.Game.Quests;
using Fineage.Game.Recipes;
using Fineage.Game.Skills;

namespace Fineage.Game;

public sealed partial class GameData
{
    static ItemKind ParseKind(string? text, string path, int itemId) =>
        Token(text) switch
        {
            "weapon" => ItemKind.Weapon,
            "armor" => ItemKind.Armor,
            "etc" => ItemKind.Etc,
            _ => throw new InvalidDataException($"{path} item {itemId} has unknown kind '{text}'.")
        };

    static BodySlot ParseBody(string? text, string path, int itemId)
    {
        if (string.IsNullOrWhiteSpace(text))
            return BodySlot.None;
        return Token(text) switch
        {
            "none" => BodySlot.None,
            "underwear" => BodySlot.Underwear,
            "lear" => BodySlot.LeftEar,
            "rear" => BodySlot.RightEar,
            "both_ears" => BodySlot.BothEars,
            "neck" => BodySlot.Neck,
            "lfinger" => BodySlot.LeftFinger,
            "rfinger" => BodySlot.RightFinger,
            "both_fingers" => BodySlot.BothFingers,
            "head" => BodySlot.Head,
            "rhand" => BodySlot.RightHand,
            "lhand" => BodySlot.LeftHand,
            "lrhand" => BodySlot.LrHand,
            "gloves" => BodySlot.Gloves,
            "chest" => BodySlot.Chest,
            "legs" => BodySlot.Legs,
            "fullarmor" => BodySlot.FullArmor,
            "feet" => BodySlot.Feet,
            "back" => BodySlot.Back,
            "hair" => BodySlot.Hair,
            "hair2" => BodySlot.Hair2,
            "hairall" => BodySlot.HairAll,
            "alldress" => BodySlot.AllDress,
            _ => throw new InvalidDataException($"{path} item {itemId} has unknown bodySlot '{text}'.")
        };
    }

    static WeaponType ParseWeapon(string? text, string path, int itemId)
    {
        if (string.IsNullOrWhiteSpace(text))
            return WeaponType.None;
        return Token(text) switch
        {
            "none" => WeaponType.None,
            "sword" => WeaponType.Sword,
            "blunt" => WeaponType.Blunt,
            "dagger" => WeaponType.Dagger,
            "bow" => WeaponType.Bow,
            "pole" => WeaponType.Pole,
            "dual" => WeaponType.Dual,
            "fist" => WeaponType.Fist,
            "fishingrod" => WeaponType.FishingRod,
            "etc" => WeaponType.Etc,
            _ => throw new InvalidDataException($"{path} item {itemId} has unknown weaponType '{text}'.")
        };
    }

    static ArmorType ParseArmor(string? text, string path, int itemId)
    {
        if (string.IsNullOrWhiteSpace(text))
            return ArmorType.None;
        return Token(text) switch
        {
            "none" => ArmorType.None,
            "light" => ArmorType.Light,
            "heavy" => ArmorType.Heavy,
            "magic" => ArmorType.Magic,
            _ => throw new InvalidDataException($"{path} item {itemId} has unknown armorType '{text}'.")
        };
    }

    static CrystalGrade ParseCrystal(string? text, string path, int itemId)
    {
        if (string.IsNullOrWhiteSpace(text))
            return CrystalGrade.None;
        return Token(text) switch
        {
            "none" or "ng" or "no_grade" => CrystalGrade.None,
            "d" => CrystalGrade.D,
            "c" => CrystalGrade.C,
            "b" => CrystalGrade.B,
            "a" => CrystalGrade.A,
            "s" => CrystalGrade.S,
            _ => throw new InvalidDataException($"{path} item {itemId} has unknown crystal '{text}'.")
        };
    }

    static EtcItemType ParseEtc(string? text, string path, int itemId)
    {
        if (string.IsNullOrWhiteSpace(text))
            return EtcItemType.None;
        return Token(text) switch
        {
            "none" => EtcItemType.None,
            "arrow" => EtcItemType.Arrow,
            "lure" => EtcItemType.Lure,
            _ => throw new InvalidDataException($"{path} item {itemId} has unknown etcType '{text}'.")
        };
    }

    static EnchantScroll? ParseScroll(JsonElement element, string path, int itemId)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{path} item {itemId} scroll must be an object.");

        var target = RequiredString(element, "target", path, itemId);
        var fail = RequiredString(element, "fail", path, itemId);
        var grade = ParseCrystal(RequiredString(element, "grade", path, itemId), path, itemId);
        if (grade == CrystalGrade.None)
            throw new InvalidDataException($"{path} item {itemId} scroll grade cannot be none.");

        bool weapon = Token(target) switch
        {
            "weapon" => true,
            "armor" => false,
            _ => throw new InvalidDataException($"{path} item {itemId} scroll target '{target}' is unknown.")
        };
        var mode = Token(fail) switch
        {
            "crystallize" => EnchantFail.Crystallize,
            "reset" => EnchantFail.Reset,
            "keep" => EnchantFail.Keep,
            _ => throw new InvalidDataException($"{path} item {itemId} scroll fail '{fail}' is unknown.")
        };

        int max = EnchantScroll.DefaultMaxEnchant;
        if (element.TryGetProperty("maxEnchant", out var maxElement))
        {
            if (!maxElement.TryGetInt32(out max) || max < 1 || max > EnchantScroll.DefaultMaxEnchant)
                throw new InvalidDataException($"{path} item {itemId} scroll maxEnchant must be 1..65535.");
        }

        double bonus = 0;
        if (element.TryGetProperty("bonusRate", out var bonusElement))
        {
            if (!bonusElement.TryGetDouble(out bonus) || bonus < 0 || bonus > 100)
                throw new InvalidDataException($"{path} item {itemId} scroll bonusRate must be 0..100.");
        }

        return new EnchantScroll(weapon, grade, mode, max, bonus);
    }

    static string RequiredString(JsonElement element, string name, string path, int itemId)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{path} item {itemId} scroll needs {name}.");
        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"{path} item {itemId} scroll needs {name}.");
        return text;
    }

    static string Token(string? text) => (text ?? "").Trim().ToLowerInvariant();
}
