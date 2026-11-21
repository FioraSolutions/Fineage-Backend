using System.Text.Json;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Xunit;

namespace Fineage.Game.Tests;

/// <summary>
/// Fails when <c>data/skill-effects.json</c> contains an effect type with no catalog entry or with status <c>missing</c>.
/// </summary>
public sealed class SkillEffectCoverageTests
{
    [Fact]
    public void Every_datapack_effect_type_is_cataloged_and_not_missing()
    {
        var path = TeleportCatalog.FindFile("skill-effects.json")
            ?? throw new InvalidOperationException("skill-effects.json not found");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in doc.RootElement.GetProperty("skills").EnumerateArray())
        {
            foreach (var eff in skill.GetProperty("effects").EnumerateArray())
            {
                if (eff.TryGetProperty("name", out var n))
                    names.Add(n.GetString() ?? "");
            }
        }

        var failures = new List<string>();
        foreach (var name in names.OrderBy(x => x))
        {
            if (string.IsNullOrEmpty(name))
                continue;
            var status = SkillEffectCoverageCatalog.StatusOf(name);
            if (status is SkillEffectCoverageCatalog.EffectStatus.Missing
                or SkillEffectCoverageCatalog.EffectStatus.Uncataloged)
                failures.Add($"{name}:{status}");
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void Documented_unsupported_matches_catalog()
    {
        foreach (var name in SkillEffectCoverageCatalog.DocumentedUnsupported)
            Assert.Equal(SkillEffectCoverageCatalog.EffectStatus.Unsupported, SkillEffectCoverageCatalog.StatusOf(name));
    }
}