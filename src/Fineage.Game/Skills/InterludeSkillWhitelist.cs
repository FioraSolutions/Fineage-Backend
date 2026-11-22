namespace Fineage.Game.Skills;

/// <summary>
/// Skill ids present in Mobius <c>L2J_Mobius_CT_0_Interlude/dist/game/data/stats/skills</c> (committed snapshot: <c>data/interlude-skill-ids.tsv</c>).
/// </summary>
public sealed class InterludeSkillWhitelist
{
    public const string FileName = "interlude-skill-ids.tsv";

    readonly HashSet<int> _ids;

    InterludeSkillWhitelist(HashSet<int> ids) => _ids = ids;

    public bool Contains(int skillId) => _ids.Contains(skillId);

    public IReadOnlyCollection<int> Ids => _ids;

    public static InterludeSkillWhitelist Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Interlude skill whitelist not found: {path}", path);

        var ids = new HashSet<int>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("id\t", StringComparison.Ordinal))
                continue;
            int tab = line.IndexOf('\t');
            string idPart = tab >= 0 ? line[..tab] : line;
            if (!int.TryParse(idPart, out int skillId) || skillId <= 0)
                throw new InvalidDataException($"{path} has a bad skill id row: {line}");
            ids.Add(skillId);
        }

        if (ids.Count == 0)
            throw new InvalidDataException($"{path} contained no skill ids.");

        return new InterludeSkillWhitelist(ids);
    }

    public static InterludeSkillWhitelist LoadDefault()
    {
        string? data = GameData.FindDataDirectory(AppContext.BaseDirectory)
            ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory());
        if (data == null)
            throw new DirectoryNotFoundException($"Could not find {FileName} under data/.");
        return Load(Path.Combine(data, FileName));
    }
}