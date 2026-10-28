using System.Globalization;
using System.Xml.Linq;

namespace Fineage.Game.Mobius;

public sealed record MobiusNpcRow(
    int Id,
    string Name,
    int Level,
    string Type,
    string? Race,
    int MaxHp,
    float WalkSpeed,
    float RunSpeed,
    bool Attackable);

public static class MobiusNpcXmlReader
{
    public static IReadOnlyList<MobiusNpcRow> ReadFile(string path)
    {
        var doc = XDocument.Load(path, LoadOptions.None);
        return doc.Root?.Elements("npc").Select(ParseNpc).ToList() ?? [];
    }

    public static MobiusNpcRow? TryReadById(string npcsDir, int npcId)
    {
        string fileName = $"{npcId / 100 * 100:D5}-{npcId / 100 * 100 + 99:D5}.xml";
        var path = Path.Combine(npcsDir, fileName);
        if (!File.Exists(path))
            return null;

        foreach (var row in ReadFile(path))
        {
            if (row.Id == npcId)
                return row;
        }

        return null;
    }

    public static MobiusNpcRow ParseNpc(XElement element)
    {
        int id = (int?)element.Attribute("id") ?? throw new InvalidDataException("npc missing id");
        string name = (string?)element.Attribute("name") ?? $"npc_{id}";
        int level = (int?)element.Attribute("level") ?? 1;
        string type = (string?)element.Attribute("type") ?? "Monster";
        string? race = element.Element("race")?.Value;

        var vitals = element.Descendants("vitals").FirstOrDefault();
        double hpRaw = ParseDouble(vitals?.Attribute("hp")?.Value) ?? 1;
        int maxHp = Math.Max(1, (int)Math.Ceiling(hpRaw));

        var speed = element.Descendants("speed").FirstOrDefault();
        float walk = MobiusWireSpeed.ParseGround(speed?.Element("walk"));
        float run = MobiusWireSpeed.ParseGround(speed?.Element("run"));

        bool attackable = !string.Equals(type, "Folk", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(type, "Warehouse", StringComparison.OrdinalIgnoreCase) &&
                          !string.Equals(type, "Merchant", StringComparison.OrdinalIgnoreCase);

        return new MobiusNpcRow(id, name, level, type, race, maxHp, walk, run, attackable);
    }

    static double? ParseDouble(string? text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
}

static file class MobiusWireSpeed
{
    /// <summary>Mobius ground speeds are half of Fineage wire speeds (see Gremlin run 50 → 80).</summary>
    public static float ParseGround(XElement? node)
    {
        if (node == null)
            return 0;
        var ground = node.Attribute("ground")?.Value;
        if (!float.TryParse(ground, NumberStyles.Float, CultureInfo.InvariantCulture, out var mobius))
            return 0;
        return mobius * 2f;
    }
}
