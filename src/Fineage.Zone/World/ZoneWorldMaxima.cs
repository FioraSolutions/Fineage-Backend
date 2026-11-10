using Fineage.Game;
using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius player MaxHp/MaxMp: class table + equipped item flat maxHp/maxMp (jewelry etc.).
    /// Call after equip/unequip and on enter once inventory/gear skills are ready.
    /// </summary>
    internal bool RefreshPlayerMaxima(ZoneEntity player, bool fanout = true)
    {
        if (player.Kind != ZoneEntityKind.Player)
            return false;
        if (!PlayerClasses.TryGet(player.ClassId, out var template) || template == null)
            return false;

        int level = Math.Max(1, player.Level);
        int maxHp = template.ModifiedHp(level);
        int maxMp = template.ModifiedMp(level);
        if (player.Inventory != null)
        {
            foreach (var item in player.Inventory.Items)
            {
                if (!item.IsEquipped)
                    continue;
                maxHp += Math.Max(0, item.Template.MaxHp);
                maxMp += Math.Max(0, item.Template.MaxMp);
            }
        }

        maxHp = Math.Max(1, maxHp);
        maxMp = Math.Max(1, maxMp);
        bool changed = player.MaxHp != maxHp || player.MaxMp != maxMp;
        player.MaxHp = maxHp;
        player.MaxMp = maxMp;
        if (player.CurHp > maxHp)
            player.CurHp = maxHp;
        if (player.CurMp > maxMp)
            player.CurMp = maxMp;

        if (changed && fanout && player.Session != null)
        {
            FanoutVitals(player, PlayerVitalsStatus(player));
            SelfUserInfo(player);
        }

        return changed;
    }
}
