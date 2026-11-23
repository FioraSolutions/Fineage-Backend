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

  /// <summary>Mobius Warlord (48) for pole splash + AoE skills.</summary>
  public int WarlordClassId { get; set; } = 48;

  /// <summary>Offensive rotation: Whirlwind (36), Thunder Storm (48), …</summary>
  public int[] SkillRotation { get; set; } = [36, 48];

  /// <summary>Fraction of bots in <c>aoe-combined</c> that run economy-hammer instead of combat.</summary>
  public double EconomyBotFraction { get; set; } = 0.44;

  /// <summary>Within economy bots, weight for town teleports (Nikita: ~20%).</summary>
  public double EconomyTeleportWeight { get; set; } = 0.20;

  public bool IsHeavyPlay =>
    !string.IsNullOrWhiteSpace(Mode) &&
    !Mode.Equals("mixed", StringComparison.OrdinalIgnoreCase);
}