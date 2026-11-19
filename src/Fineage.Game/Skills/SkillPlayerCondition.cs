namespace Fineage.Game.Skills;

/// <summary>Mobius <c>&lt;cond msgId="113"&gt;&lt;player …/&gt;</c> skill-use gates (element seeds, required stance).</summary>
public readonly record struct SkillPlayerCondition(string Type, string Arg, int Value);
