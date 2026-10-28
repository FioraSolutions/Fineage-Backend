namespace Fineage.Protocol;

/// <summary>
/// Zone skill wire. Request **50** (after siege 46–49). Replies **1065–1066** (after siege 1064).
/// </summary>
/// <remarks>
/// Mobius Interlude: <c>SkillList</c> on enter, <c>RequestMagicSkillUse</c>, fan-out <c>MagicSkillUse</c>.
/// Catalog ids come from Mobius CT0 XML via <c>data/skills.json</c>.
/// Layout: <c>docs/skill-v2-protocol.md</c>.
/// </remarks>
public static class SkillProtocol
{
    public const ushort UseRequest = 50;

    public const ushort List = 1065;
    public const ushort MagicSkillUse = 1066;

    public const string Unknown = "skill_unknown";
    public const string Passive = "skill_passive";
    public const string Reuse = "skill_reuse";
    public const string Mp = "skill_mp";
    public const string Target = "skill_target";
    public const string Range = "skill_range";
    public const string Dead = "skill_dead";
}
