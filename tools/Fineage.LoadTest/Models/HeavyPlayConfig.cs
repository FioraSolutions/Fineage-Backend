namespace Fineage.LoadTest.Models;

/// <summary>Perf #951 — structured AoE / party fan-out scenarios (not idle random walks).</summary>
public sealed class HeavyPlayConfig
{
    /// <summary>
    /// <c>mixed</c> (default), <c>aoe-train</c>, <c>aoe-multiparty</c>, or <c>aoe-combined</c>
    /// (multi-party combat + economy hammer background).
    /// </summary>
    public string Mode { get; set; } = "mixed";

    public int PartySize { get; set; } = 9;
    public int PartyCount { get; set; } = 4;

    /// <summary>Offensive rotation: Whirlwind (36), Thunder Storm (48), …</summary>
    public int[] SkillRotation { get; set; } = [36, 48];

    /// <summary>
    /// Bots that fight: every slot of <see cref="PartyCount"/> parties for <c>aoe-multiparty</c> / <c>aoe-combined</c>,
    /// the whole fleet for <c>aoe-train</c> (one train fighter plus observers). The server makes each load-test account a
    /// level 77 Warlord with a pole (<c>FINEAGE_ZONE_LOADTEST_COMBAT=1</c>).
    /// </summary>
    public int CombatBotCount => Mode.ToLowerInvariant() switch
    {
        "aoe-train" => int.MaxValue,
        "aoe-multiparty" or "aoe-combined" => Math.Max(2, PartySize) * Math.Max(1, PartyCount),
        _ => 0
    };

    public bool IsHeavyPlay =>
      !string.IsNullOrWhiteSpace(Mode) &&
      !Mode.Equals("mixed", StringComparison.OrdinalIgnoreCase);
}