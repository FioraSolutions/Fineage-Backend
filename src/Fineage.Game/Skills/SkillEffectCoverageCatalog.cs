using System.Text.Json;
using Fineage.Game.Services;

namespace Fineage.Game.Skills;

/// <summary>
/// Mobius <c>&lt;effect name&gt;</c> coverage from <c>data/skill-effect-coverage.json</c>.
/// Keep in sync with <c>tools/audit_skill_effects.py</c>.
/// </summary>
public static class SkillEffectCoverageCatalog
{
    public const string FileName = "skill-effect-coverage.json";

    public enum EffectStatus
    {
        Full,
        Partial,
        Unsupported,
        Missing,
        Uncataloged,
    }

    static readonly Lazy<Root> Loaded = new(Load);

    public static IReadOnlyDictionary<string, Entry> Effects => Loaded.Value.Effects;

    public static IReadOnlyList<string> DocumentedUnsupported => Loaded.Value.DocumentedUnsupported;

    public static EffectStatus StatusOf(string effectName)
    {
        if (string.IsNullOrWhiteSpace(effectName))
            return EffectStatus.Missing;
        if (DocumentedUnsupported.Contains(effectName, StringComparer.Ordinal))
            return EffectStatus.Unsupported;
        if (!Effects.TryGetValue(effectName, out var entry))
            return EffectStatus.Uncataloged;
        return entry.Status switch
        {
            "full" => EffectStatus.Full,
            "partial" => EffectStatus.Partial,
            "unsupported" => EffectStatus.Unsupported,
            "missing" => EffectStatus.Missing,
            _ => EffectStatus.Uncataloged,
        };
    }

    public static bool AllowsDatapackUse(string effectName) =>
        StatusOf(effectName) is EffectStatus.Full or EffectStatus.Partial or EffectStatus.Unsupported;

    static Root Load()
    {
        var path = TeleportCatalog.FindFile(FileName);
        if (path == null)
            return new Root(new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase), []);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var effects = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("effects", out var effectsObj) && effectsObj.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in effectsObj.EnumerateObject())
            {
                string status = prop.Value.TryGetProperty("status", out var st) ? st.GetString() ?? "missing" : "missing";
                string note = prop.Value.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "";
                effects[prop.Name] = new Entry(status, note);
            }
        }

        var unsupported = new List<string>();
        if (root.TryGetProperty("documentedUnsupported", out var uns) && uns.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in uns.EnumerateArray())
            {
                if (el.GetString() is { } s && s.Length > 0)
                    unsupported.Add(s);
            }
        }

        return new Root(effects, unsupported);
    }

    public readonly record struct Entry(string Status, string Note);

    sealed record Root(Dictionary<string, Entry> Effects, List<string> DocumentedUnsupported);
}