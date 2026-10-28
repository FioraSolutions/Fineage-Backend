using System.Globalization;
using System.Xml.Linq;

namespace Fineage.Game.Mobius;

public sealed record MobiusItemRow(
    int Id,
    string Name,
    string MobiusType,
    string Kind,
    string Icon,
    string? BodySlot,
    string? WeaponType,
    string? ArmorType,
    bool Stackable,
    bool Magic,
    int PDef);

public static class MobiusItemXmlReader
{
    public static MobiusItemRow? TryReadById(string itemsDir, int itemId)
    {
        string fileName = $"{itemId / 100 * 100:D5}-{itemId / 100 * 100 + 99:D5}.xml";
        var path = Path.Combine(itemsDir, fileName);
        if (!File.Exists(path))
            return null;

        var doc = XDocument.Load(path, LoadOptions.None);
        var item = doc.Root?.Elements("item").FirstOrDefault(e => (int?)e.Attribute("id") == itemId);
        return item == null ? null : ParseItem(item);
    }

    public static MobiusItemRow ParseItem(XElement element)
    {
        int id = (int?)element.Attribute("id") ?? throw new InvalidDataException("item missing id");
        string name = (string?)element.Attribute("name") ?? $"item_{id}";
        string mobiusType = (string?)element.Attribute("type") ?? "EtcItem";

        string icon = GetSet(element, "icon") ?? $"icon.item_{id}";
        bool stackable = ParseBool(GetSet(element, "is_stackable"));
        string? body = NormalizeBody(GetSet(element, "bodypart"));
        string? weapon = NormalizeToken(GetSet(element, "weapon_type"));
        string? armor = NormalizeToken(GetSet(element, "armor_type"));
        bool magic = ParseBool(GetSet(element, "is_magic_weapon"));

        int pDef = 0;
        foreach (var stat in element.Descendants("stat"))
        {
            if (string.Equals((string?)stat.Attribute("type"), "pDef", StringComparison.OrdinalIgnoreCase))
            {
                pDef = (int)Math.Round(ParseDouble(stat.Value) ?? 0);
                break;
            }
        }

        string kind = mobiusType switch
        {
            "Weapon" => "weapon",
            "Armor" => "armor",
            _ => "etc"
        };

        return new MobiusItemRow(id, name, mobiusType, kind, icon, body, weapon, armor, stackable, magic, pDef);
    }

    static string? GetSet(XElement item, string name) =>
        item.Elements("set").FirstOrDefault(e => string.Equals((string?)e.Attribute("name"), name, StringComparison.OrdinalIgnoreCase))
            ?.Attribute("val")?.Value;

    static bool ParseBool(string? text) =>
        string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1";

    static double? ParseDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    static string? NormalizeToken(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim().ToLowerInvariant();

    static string? NormalizeBody(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return text.Trim().ToLowerInvariant() switch
        {
            "onepiece" => "fullarmor",
            _ => text.Trim().ToLowerInvariant()
        };
    }
}
