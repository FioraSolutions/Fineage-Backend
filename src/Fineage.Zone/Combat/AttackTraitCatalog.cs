namespace Fineage.Zone.Combat;

/// <summary>
/// Mobius <c>AttackTrait</c> multipliers from Detect Weakness / Eye of Hunter|Slayer buffs
/// (<c>(raw + 100) / 100</c>). Used with NPC race <c>defenceTraits</c> in
/// <see cref="ZoneCombat.AttackTraitBonus"/>.
/// </summary>
public static class AttackTraitCatalog
{
    /// <summary>skillId → trait key → multiplier while that abnormal is active.</summary>
    public static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, double>> BySkillId =
        new Dictionary<int, IReadOnlyDictionary<string, double>>
        {
            [75] = Map(("bugWeakness", 1.30)),
            [80] = Map(("beastWeakness", 1.30)),
            [87] = Map(("animalWeakness", 1.30)),
            [88] = Map(("dragonWeakness", 1.30)),
            [104] = Map(("plantWeakness", 1.30)),
            [359] = Map(
                ("bugWeakness", 1.30),
                ("plantWeakness", 1.30),
                ("animalWeakness", 1.30)),
            [360] = Map(
                ("beastWeakness", 1.30),
                ("constructWeakness", 1.30),
                ("giantWeakness", 1.30),
                ("dragonWeakness", 1.30)),
        };

    /// <summary>Fold active abnormals into Mobius <c>_attackTraits</c> (default 1, multiply).</summary>
    public static IReadOnlyDictionary<string, double>? FromAbnormals(
        IEnumerable<(int Id, int Level)> abnormals)
    {
        Dictionary<string, double>? traits = null;
        foreach (var (id, _) in abnormals)
        {
            if (!BySkillId.TryGetValue(id, out var row))
                continue;
            traits ??= new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var (key, mul) in row)
            {
                traits.TryGetValue(key, out var cur);
                if (cur <= 0)
                    cur = 1d;
                traits[key] = cur * mul;
            }
        }
        return traits is { Count: > 0 } ? traits : null;
    }

    static IReadOnlyDictionary<string, double> Map(params (string Key, double Mul)[] rows)
    {
        var d = new Dictionary<string, double>(rows.Length, StringComparer.Ordinal);
        foreach (var (key, mul) in rows)
            d[key] = mul;
        return d;
    }
}
