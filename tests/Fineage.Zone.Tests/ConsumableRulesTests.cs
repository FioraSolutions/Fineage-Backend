using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ConsumableRulesTests
{
    [Fact]
    public void Use_precheck_rejects_unknown_and_reuse()
    {
        Assert.Equal(InventoryProtocol.NotEquippable, ConsumableRules.UsePrecheckError(false, false));
        Assert.Equal(ConsumableRules.InventoryReuse, ConsumableRules.UsePrecheckError(true, true));
        Assert.Null(ConsumableRules.UsePrecheckError(true, false));
    }

    [Fact]
    public void Escape_town_rejects_dead_and_busy()
    {
        Assert.Equal(InventoryProtocol.Dead, ConsumableRules.EscapeTownError(true, false));
        Assert.Equal("busy", ConsumableRules.EscapeTownError(false, true));
        Assert.Null(ConsumableRules.EscapeTownError(false, false));
    }

    [Fact]
    public void Escape_skills_come_from_the_catalog_with_mobius_hit_times()
    {
        // Every skill the zone casts for an escape (items and /unstuck) is a skills.json template; the cast reads
        // the hit time from it. Blessed scrolls are near-instant, plain scrolls take 20 s (Mobius stats/skills).
        var skills = TestCatalogs.Game.Skills;
        Assert.Equal(20_000, skills.Find(2013)!.HitTimeMs);
        Assert.Equal(200, skills.Find(2036)!.HitTimeMs);
        Assert.Equal(20_000, skills.Find(2040)!.HitTimeMs);
        Assert.Equal(200, skills.Find(2177)!.HitTimeMs);
        Assert.Equal(300_000, skills.Find(2099)!.HitTimeMs);
        Assert.Equal(1_000, skills.Find(2100)!.HitTimeMs);
    }

    [Fact]
    public void Every_item_skill_template_is_in_the_skill_catalog()
    {
        // The zone casts an item's skill on use (Mobius ItemSkills) and refuses to improvise a missing template, so
        // tools/import_skills.py must have brought in every skill item-skills.json names.
        using var doc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "item-skills.json")));
        var missing = new List<string>();
        foreach (var item in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            foreach (var effect in item.GetProperty("effects").EnumerateArray())
            {
                if (!effect.TryGetProperty("skillId", out var id) || id.GetInt32() is <= 0 or >= 10000)
                    continue;
                if (TestCatalogs.Game.Skills.Find(id.GetInt32()) == null)
                    missing.Add($"{id.GetInt32()} (item {item.GetProperty("itemId").GetInt32()})");
            }
        }

        Assert.True(missing.Count == 0, "item skills missing from skills.json: " + string.Join(", ", missing));
    }

    [Fact]
    public void Percent_heal_clamps_to_room()
    {
        Assert.Equal(0, ConsumableRules.PercentHeal(100, 100, 50));
        Assert.Equal(50, ConsumableRules.PercentHeal(50, 100, 50));
        Assert.Equal(0, ConsumableRules.PercentHeal(0, 0, 50));
        Assert.Equal(0, ConsumableRules.PercentHeal(10, 100, 0));
    }
}
