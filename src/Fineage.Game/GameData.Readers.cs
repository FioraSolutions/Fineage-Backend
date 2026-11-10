using System.Text.Json;
using Fineage.Game.Quests;
using Fineage.Game.Recipes;
using Fineage.Game.Skills;

namespace Fineage.Game;

public sealed partial class GameData
{
    static ItemCatalog ReadItems(string path)
    {
        var file = Read<ItemFileDto>(path);
        if (file.Items == null || file.Items.Count == 0)
            throw new InvalidDataException($"{path} has no items.");

        var templates = new List<ItemTemplate>(file.Items.Count);
        foreach (var row in file.Items)
        {
            if (row.Id <= 0)
                throw new InvalidDataException($"{path} has an item id that is not positive.");
            if (string.IsNullOrWhiteSpace(row.Name))
                throw new InvalidDataException($"{path} item {row.Id} needs a name.");
            if (string.IsNullOrWhiteSpace(row.Icon))
                throw new InvalidDataException($"{path} item {row.Id} needs an icon.");
            if (row.PDef < 0)
                throw new InvalidDataException($"{path} item {row.Id} has a negative pDef.");

            var kind = ParseKind(row.Kind, path, row.Id);
            var body = ParseBody(row.BodySlot, path, row.Id);
            var weapon = ParseWeapon(row.WeaponType, path, row.Id);
            var armor = ParseArmor(row.ArmorType, path, row.Id);
            var etc = ParseEtc(row.EtcType, path, row.Id);

            if (kind == ItemKind.Weapon && weapon == WeaponType.None)
                throw new InvalidDataException($"{path} weapon {row.Id} needs a weaponType.");
            if (kind == ItemKind.Weapon && body == BodySlot.None)
                throw new InvalidDataException($"{path} weapon {row.Id} needs a bodySlot.");
            if (kind == ItemKind.Armor && body == BodySlot.None)
                throw new InvalidDataException($"{path} armor {row.Id} needs a bodySlot.");
            if (row.Magic && kind != ItemKind.Weapon)
                throw new InvalidDataException($"{path} item {row.Id} is marked magic but is not a weapon.");

            var crystal = ParseCrystal(row.Crystal, path, row.Id);
            var equip = EquipConditionReader.Read(row.Equip, path, row.Id);
            if (row.CrystalCount < 0)
                throw new InvalidDataException($"{path} item {row.Id} has a negative crystalCount.");
            var scroll = ParseScroll(row.Scroll, path, row.Id);
            if (scroll != null && kind != ItemKind.Etc)
                throw new InvalidDataException($"{path} item {row.Id} is a scroll but is not etc.");
            if (row.Enchantable && kind == ItemKind.Etc)
                throw new InvalidDataException($"{path} item {row.Id} is etc and cannot be enchantable.");
            if (row.Price < 0)
                throw new InvalidDataException($"{path} item {row.Id} has a negative price.");
            if (row.Weight < 0)
                throw new InvalidDataException($"{path} item {row.Id} has a negative weight.");

            templates.Add(new ItemTemplate(
                row.Id,
                row.Name.Trim(),
                kind,
                body,
                row.Icon.Trim(),
                weapon,
                armor,
                etc,
                row.Magic,
                row.Stackable,
                row.PDef,
                crystal,
                equip,
                row.CrystalCount,
                row.Enchantable,
                scroll,
                row.Price,
                row.Weight,
                row.Sellable ?? true,
                Math.Max(0, row.PAtk),
                Math.Max(0, row.MAtk),
                Math.Max(0, row.PAtkSpd),
                Math.Max(0, row.CritRate),
                Math.Max(0, row.AttackRange),
                Math.Max(0, row.RandomDamage),
                Math.Max(0, row.MDef),
                Math.Max(0, row.MaxMp),
                Math.Max(0, row.MaxHp),
                Math.Max(0, row.Soulshots),
                Math.Max(0, row.ShieldDef),
                Math.Max(0, row.ShieldRate),
                Math.Max(0, row.ShieldEvasionPenalty),
                row.Soulshot,
                ParseCrystal(row.ShotGrade, path, row.Id),
                Math.Max(0, row.Spiritshots),
                row.Spiritshot,
                row.BlessedSpiritshot,
                row.Quest));
        }

        try
        {
            return new ItemCatalog(templates);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"{path} is not a valid item catalog. {ex.Message}", ex);
        }
    }

    static StarterKits ReadKits(string path, ItemCatalog items)
    {
        var file = Read<KitFileDto>(path);
        var kits = new List<StarterKit>();
        foreach (var row in file.Kits ?? [])
        {
            if (row.ClassId < 0)
                throw new InvalidDataException($"{path} has a negative class id.");
            if (row.Items == null || row.Items.Count == 0)
                throw new InvalidDataException($"{path} class {row.ClassId} has no items.");

            var entries = new List<StarterKitEntry>(row.Items.Count);
            foreach (var item in row.Items)
            {
                if (item.Count < 1)
                    throw new InvalidDataException($"{path} class {row.ClassId} item {item.ItemId} needs a count of at least 1.");
                if (!items.TryGet(item.ItemId, out var template))
                    throw new InvalidDataException($"{path} class {row.ClassId} references unknown item {item.ItemId}.");
                if (item.Equipped && !template.Equippable)
                    throw new InvalidDataException($"{path} class {row.ClassId} equips item {item.ItemId}, which has no body slot.");
                entries.Add(new StarterKitEntry(item.ItemId, item.Count, item.Equipped));
            }

            kits.Add(new StarterKit(row.ClassId, entries));
        }

        try
        {
            return new StarterKits(kits);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"{path} is not a valid starter-kit file. {ex.Message}", ex);
        }
    }

    static T Read<T>(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Game data file not found: {path}", path);
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new InvalidDataException($"{path} was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Could not read {path}.", ex);
        }
    }
}
