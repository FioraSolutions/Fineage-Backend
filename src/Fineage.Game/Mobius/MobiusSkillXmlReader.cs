using System.Xml.Linq;

namespace Fineage.Game.Mobius;

public sealed record MobiusSkillRow(int Id, string Name, int Level);

/// <summary>Minimal skill XML reader for future <c>data/skills.json</c> expansion.</summary>
public static class MobiusSkillXmlReader
{
    public static IReadOnlyList<MobiusSkillRow> ReadFile(string path)
    {
        var doc = XDocument.Load(path, LoadOptions.None);
        return doc.Root?.Elements("skill").Select(ParseSkill).ToList() ?? [];
    }

    public static MobiusSkillRow ParseSkill(XElement element)
    {
        int id = (int?)element.Attribute("id") ?? throw new InvalidDataException("skill missing id");
        int level = (int?)element.Attribute("level") ?? 1;
        string name = (string?)element.Attribute("name") ?? $"skill_{id}";
        return new MobiusSkillRow(id, name, level);
    }
}
