namespace Fineage.Game.RequiredData;

/// <summary>Catalog kinds that must never be silently defaulted when missing.</summary>
public enum RequiredDataKind
{
    Skill,
    Item,
    Npc,
    Teleport,
    Droplist,
    SkillPassive,
    NpcAi,
    Quest,
    Spawn,
    BuyList,
    MultiSell,
    NpcScript
}
