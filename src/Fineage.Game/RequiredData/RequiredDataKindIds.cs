namespace Fineage.Game.RequiredData;

public static class RequiredDataKindIds
{
    public static string ToId(this RequiredDataKind kind) => kind switch
    {
        RequiredDataKind.Skill => "skill",
        RequiredDataKind.Item => "item",
        RequiredDataKind.Npc => "npc",
        RequiredDataKind.Teleport => "teleport",
        RequiredDataKind.Droplist => "droplist",
        RequiredDataKind.SkillPassive => "skill-passive",
        RequiredDataKind.NpcAi => "npc-ai",
        RequiredDataKind.Quest => "quest",
        RequiredDataKind.Spawn => "spawn",
        RequiredDataKind.BuyList => "buylist",
        RequiredDataKind.MultiSell => "multisell",
        RequiredDataKind.NpcScript => "npc-script",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown required-data kind.")
    };

    public static RequiredDataKind Parse(string id) => id switch
    {
        "skill" => RequiredDataKind.Skill,
        "item" => RequiredDataKind.Item,
        "npc" => RequiredDataKind.Npc,
        "teleport" => RequiredDataKind.Teleport,
        "droplist" => RequiredDataKind.Droplist,
        "skill-passive" => RequiredDataKind.SkillPassive,
        "npc-ai" => RequiredDataKind.NpcAi,
        "quest" => RequiredDataKind.Quest,
        "spawn" => RequiredDataKind.Spawn,
        "buylist" => RequiredDataKind.BuyList,
        "multisell" => RequiredDataKind.MultiSell,
        "npc-script" => RequiredDataKind.NpcScript,
        _ => throw new InvalidDataException($"required-data-registry.json has unknown kind id '{id}'.")
    };
}
