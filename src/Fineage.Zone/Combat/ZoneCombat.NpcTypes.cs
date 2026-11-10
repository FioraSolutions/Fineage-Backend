namespace Fineage.Zone.Combat;

public static partial class ZoneCombat
{
    /// <summary>
    /// Mobius <c>Attackable</c> instance types: take HP damage, build hate, and swing back.
    /// Folk / Merchant / Trainer / Teleporter / … extend <c>Npc</c> only — <c>FolkStatus.reduceHp</c>
    /// is a no-op and they never retaliate. Do <b>not</b> use the XML <c>attackable</c> flag alone when
    /// <paramref name="npcType"/> is set: town Guards ship with <c>attackable=false</c> but still fight.
    /// </summary>
    public static bool IsCombatNpcType(string? npcType)
    {
        if (string.IsNullOrEmpty(npcType))
            return false;
        return npcType switch
        {
            "Monster" or "Guard" or "Defender" or "FriendlyMob"
                or "RaidBoss" or "GrandBoss"
                or "FestivalMonster" or "RiftInvader" or "SepulcherMonster"
                or "FeedableBeast" or "Chest" or "Artefact" or "TamedBeast"
                or "Pet" or "Servitor" => true,
            _ => false
        };
    }

    /// <summary>
    /// Combat HP/hate gate. Prefer typed hierarchy; when type is missing (hand-built smoke
    /// <c>SpawnNpcDefinition</c>), fall back to the Attackable flag so Gremlins still die.
    /// </summary>
    public static bool TakesNpcCombatDamage(string? npcType, bool attackable) =>
        string.IsNullOrEmpty(npcType) ? attackable : IsCombatNpcType(npcType);
}
