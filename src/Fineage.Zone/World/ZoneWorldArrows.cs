using Fineage.Game;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius bow ammunition (<c>Creature.canUseRangeWeapon</c> / <c>Creature.doAttackHitByBow</c>,
/// <c>Creature.java:845</c> and <c>:1202</c>): a bow needs an arrow of the same crystal grade in the left hand,
/// takes one from the bag when the hand is empty, and burns one arrow per shot — hit or miss.
/// <list type="bullet">
/// <item><c>checkAndEquipArrows</c> (<c>Player.java:5838</c>) + <c>Inventory.findArrowForBow</c>: equip a matching
/// arrow, or refuse the attack with <c>YOU_HAVE_RUN_OUT_OF_ARROWS</c> (112).</item>
/// <item><c>reduceArrowCount</c> (<c>Player.java:5798</c>): one arrow per shot; the last one destroys the stack,
/// frees the left hand and refreshes the item list.</item>
/// </list>
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>SystemMessageId.YOU_HAVE_RUN_OUT_OF_ARROWS</c> (<c>SystemMessageId.java:112</c>).</summary>
    public const int SysMsgRunOutOfArrows = 112;

    static bool IsArrowFor(ItemInstance item, ItemTemplate bow) =>
        item.Template.EtcType == EtcItemType.Arrow && item.Template.Crystal == bow.Crystal;

    /// <summary>Mobius <c>Inventory.findArrowForBow</c>: first arrow in the bag whose grade matches the bow.</summary>
    static ItemInstance? FindArrowForBow(PlayerInventory bag, ItemTemplate bow)
    {
        foreach (var item in bag.Items)
        {
            if (IsArrowFor(item, bow))
                return item;
        }
        return null;
    }

    /// <summary>
    /// Mobius <c>Player.checkAndEquipArrows</c>: returns the arrow the bow will fire. When the left hand is empty the
    /// matching arrow is taken from the bag and equipped (the client sees the paperdoll change). Null = out of arrows,
    /// the caller must refuse the shot and say <see cref="SysMsgRunOutOfArrows"/>.
    /// </summary>
    ItemInstance? EnsureBowArrow(ZoneEntity player, ItemTemplate bow)
    {
        var bag = player.Inventory;
        if (bag == null)
            return null;
        var equipped = bag.GetEquipped(PaperdollSlot.LHand);
        if (equipped != null)
            return IsArrowFor(equipped, bow) ? equipped : null;

        var arrow = FindArrowForBow(bag, bow);
        if (arrow == null)
            return null;
        try
        {
            bag.Equip(arrow);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        var update = InventoryMessages.Modify([arrow]);
        if (player.Session != null)
        NetBridge?.FanoutInventoryUpdate([player.ObjectId], ToNetInventoryUpdate(update));
        return arrow;
    }

    /// <summary>
    /// Mobius <c>Player.reduceArrowCount</c>: one arrow per shot. The last arrow destroys the stack, frees the left hand
    /// and sends a fresh item list so the client drops the paperdoll entry.
    /// </summary>
    void ConsumeBowArrow(ZoneEntity player)
    {
        var bag = player.Inventory;
        var arrow = bag?.GetEquipped(PaperdollSlot.LHand);
        if (bag == null || arrow == null || player.Session == null)
            return;

        int left = bag.TakeOne(arrow);
        var update = InventoryMessages.Update(left > 0 ? InventoryChange.Modify : InventoryChange.Remove, arrow);
        NetBridge?.FanoutInventoryUpdate([player.ObjectId], ToNetInventoryUpdate(update));
        if (left == 0)
        {
            ZoneLog.Info($"bow object={player.ObjectId} arrows exhausted item={arrow.ItemId}");
        }
    }

    /// <summary>Mobius: no arrow for the bow → ActionFailed + SysMsg 112 and the attack intention is dropped.</summary>
    void OutOfArrows(ZoneSession session)
    {
        session.AutoAttackTargetId = 0;
        session.ClearApproach();
        NetBridge?.FanoutSystemMessage([session.ObjectId], new Fineage.Net.Contract.SystemMessageNetMsg
        {
            Id = SysMsgRunOutOfArrows,
            Text = "You have run out of arrows.",
            Params = []
        });
    }
}
