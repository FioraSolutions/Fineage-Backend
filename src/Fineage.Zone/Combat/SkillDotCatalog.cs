using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Zone.Combat;

/// <summary>
/// Mobius <c>DamOverTime</c> / <c>DamOverTimePercent</c> rows from <c>data/skill-dots.json</c>.
/// </summary>
public sealed class SkillDotCatalog
{
    public const string FileName = "skill-dots.json";

    public readonly record struct Dot(bool Percent, double[] Powers, int TickMs)
    {
        public double PowerAt(int level) =>
            Powers.Length == 0 ? 0 : Powers[Math.Clamp(level, 1, Powers.Length) - 1];
    }

    readonly Dictionary<int, Dot> _byId;

    SkillDotCatalog(Dictionary<int, Dot> byId) => _byId = byId;

    public static readonly SkillDotCatalog Empty = new([]);

    public int Count => _byId.Count;

    public bool TryGet(int skillId, out Dot dot) => _byId.TryGetValue(skillId, out dot);

    public static SkillDotCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName);
        return path == null ? Empty : Parse(File.ReadAllText(path), path);
    }

    public static SkillDotCatalog Parse(string json, string source = FileName)
    {
        using var doc = JsonDocument.Parse(json);
        var byId = new Dictionary<int, Dot>();
        if (!doc.RootElement.TryGetProperty("skills", out var skills))
            throw new InvalidDataException($"{source} has no skills.");
        foreach (var row in skills.EnumerateArray())
        {
            int id = row.GetProperty("skillId").GetInt32();
            string kind = row.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
            int tickSeconds = row.TryGetProperty("tickSeconds", out var t) ? Math.Max(1, t.GetInt32()) : 1;
            var powers = row.GetProperty("powers").EnumerateArray().Select(v => v.GetDouble()).ToArray();
            if (id <= 0 || powers.Length == 0)
                continue;
            byId[id] = new Dot(
                Percent: kind.Equals("percent", StringComparison.OrdinalIgnoreCase),
                Powers: powers,
                TickMs: tickSeconds * 1000);
        }

        return new SkillDotCatalog(byId);
    }
}
