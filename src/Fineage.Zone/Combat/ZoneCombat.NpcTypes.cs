namespace Fineage.Zone.Combat;

public static partial class ZoneCombat
{
    /// <summary>
    /// Mobius <c>Attackable</c> instance types: take HP damage, build hate, and swing back.
    /// Folk / Merchant / Trainer / Teleporter / … extend <c>Npc</c> only — <c>FolkStatus.reduceHp</c>
    /// is a no-op and they never retaliate. Do <b>not</b> use the XML <c>attackable</c> flag here:
    /// town Guards ship with <c>attackable=false</c> but still fight (Guard extends Attackable).
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
}
