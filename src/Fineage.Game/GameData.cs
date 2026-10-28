using System.Text.Json;

namespace Fineage.Game;

/// <summary>
/// Loads hand-written item templates and starter kits from <c>data/items.json</c>
/// and <c>data/starter-kits.json</c>. Ids and stats were checked against L2J Mobius
/// Interlude; the JSON is original data, not a copy of Mobius XML.
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

    GameData(ItemCatalog items, StarterKits starterKits)
    {
        Items = items;
        StarterKits = starterKits;
    }

    public ItemCatalog Items { get; }

    public StarterKits StarterKits { get; }

    public static GameData Load(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        if (!Directory.Exists(dataDirectory))
            throw new DirectoryNotFoundException($"Game data directory not found: {dataDirectory}");

        var items = ReadItems(Path.Combine(dataDirectory, ItemsFileName));
        var kits = ReadKits(Path.Combine(dataDirectory, StarterKitsFileName), items);
        return new GameData(items, kits);
    }

    public static GameData LoadDefault()
    {
        var directory = FindDataDirectory(AppContext.BaseDirectory)
            ?? FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
        {
            throw new DirectoryNotFoundException(
                "Could not find data/items.json and data/starter-kits.json. Pass the data directory to GameData.Load.");
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
                File.Exists(Path.Combine(data, StarterKitsFileName)))
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
        return new GameData(items, starterKits);
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
                equip));
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
        public JsonElement Equip { get; set; }
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
