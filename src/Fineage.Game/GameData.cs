using System.Text.Json;

namespace Fineage.Game;

/// <summary>
/// Loads item templates, starter kits, and Interlude class templates from <c>data/</c>.
/// Ids and stats were checked against L2J Mobius Interlude; the JSON is original data, not a copy of Mobius XML.
/// </summary>
public sealed class GameData
{
    public const string ItemsFileName = "items.json";
    public const string StarterKitsFileName = "starter-kits.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    GameData(ItemCatalog items, StarterKits starterKits, ClassCatalog? classes)
    {
        Items = items;
        StarterKits = starterKits;
        Classes = classes;
    }

    public ItemCatalog Items { get; }

    public StarterKits StarterKits { get; }

    /// <summary>Null when the catalog was built in memory for an item test.</summary>
    public ClassCatalog? Classes { get; }

    public static GameData Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException($"Game data directory not found: {dataDirectory}");

        var items = ReadItems(Path.Combine(dataDirectory, ItemsFileName));
        var kits = ReadKits(Path.Combine(dataDirectory, StarterKitsFileName), items);
        var classes = ClassCatalog.Load(dataDirectory);
        return new GameData(items, kits, classes);
    }

    public static GameData LoadDefault()
    {
        var directory = FindDataDirectory(AppContext.BaseDirectory)
            ?? FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
        {
            throw new DirectoryNotFoundException(
                "Could not find data/items.json, data/starter-kits.json, and the class catalogs. Pass the data directory to GameData.Load.");
        }

        return Load(directory);
    }

    public static string? FindDataDirectory(string start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(start);
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        while (dir != null)
        {
            var data = Path.Combine(dir.FullName, "data");
            if (File.Exists(Path.Combine(data, ItemsFileName)) &&
                File.Exists(Path.Combine(data, StarterKitsFileName)) &&
                File.Exists(Path.Combine(data, ClassCatalog.ClassesFileName)) &&
                File.Exists(Path.Combine(data, ClassCatalog.ExperienceFileName)) &&
                File.Exists(Path.Combine(data, ClassCatalog.StatBonusesFileName)))
                return data;
            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>Builds game data from an in-memory catalog. Used by tests that need body slots the shipped catalog does not list.</summary>
    public static GameData From(ItemCatalog items, StarterKits starterKits)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(starterKits);
        return new GameData(items, starterKits, classes: null);
    }

    public PlayerInventory CreateStarterInventory(int classId, int firstObjectId = PlayerInventory.FirstObjectId) =>
        CreateStarterInventory(classId, new ItemObjectIdAllocator(firstObjectId));

    public PlayerInventory CreateStarterInventory(int classId, ItemObjectIdAllocator ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var inventory = new PlayerInventory(Items, ids);
        inventory.AddStarterKit(StarterKits.ForClass(classId));
        return inventory;
    }

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
                scroll));
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

    sealed class ItemFileDto
    {
        public List<ItemDto>? Items { get; set; }
    }

    sealed class ItemDto
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Kind { get; set; }
        public string? BodySlot { get; set; }
        public string? WeaponType { get; set; }
        public string? ArmorType { get; set; }
        public string? EtcType { get; set; }
        public string? Icon { get; set; }
        public bool Magic { get; set; }
        public bool Stackable { get; set; }
        public int PDef { get; set; }
        public string? Crystal { get; set; }
        public int CrystalCount { get; set; }
        public bool Enchantable { get; set; }
        public JsonElement Equip { get; set; }
        public JsonElement Scroll { get; set; }
    }

    sealed class KitFileDto
    {
        public List<KitDto>? Kits { get; set; }
    }

    sealed class KitDto
    {
        public int ClassId { get; set; }
        public List<KitItemDto>? Items { get; set; }
    }

    sealed class KitItemDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; } = 1;
        public bool Equipped { get; set; }
    }
}
