using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>UseItem</c> for non-equippable items (<c>handler="ItemSkills"</c>): potions, Scroll of Escape,
/// scrolls of recovery, compressed shot packages.
/// <para>
/// Mobius flow: <c>RequestItemUse</c> → <c>ItemHandler.useItem(player, item, ctrl)</c> → <c>ItemSkillsTemplate</c>
/// casts each <c>item_skill</c> on the player and, when the cast lands,
/// <c>player.destroyItem("Consume", item.getObjectId(), 1, player, false)</c>. The zone executes the flattened
/// effect list from <c>data/item-skills.json</c> and consumes one item, so a stack drains one per use.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius SystemMessageId 113 "This item cannot be used due to unsuitable terms."</summary>
    public const int SysMsgItemCannotBeUsed = 113;

    /// <summary>Returned when the item's own reuse delay has not expired yet (Mobius <c>ItemTemplate</c> reuse).</summary>
    public const string InventoryReuse = "item_reuse";

    bool IsConsumable(int itemId) => _itemSkills.TryGet(itemId, out _);

    /// <summary>
    /// Runs the item's skill list and consumes one. Returns null on success or an
    /// <see cref="InventoryProtocol"/> / <see cref="InventoryReuse"/> code the Net client turns into a chat line.
    /// </summary>
    string? TryUseConsumable(ZoneSession session, ZoneEntity player, ItemInstance item)
    {
        if (!_itemSkills.TryGet(item.ItemId, out var use))
            return InventoryProtocol.NotEquippable;

        long now = _clock().ToUnixTimeMilliseconds();
        // Mobius keeps one reuse stamp per item id on the character; a stack shares that timer.
        if (use.ReuseDelayMs > 0 &&
            player.ItemReuseUntilMs.TryGetValue(item.ItemId, out long reuseUntil) && reuseUntil > now)
            return InventoryReuse;

        // Effects are resolved first so an unsupported one leaves the stack untouched instead of eating the potion.
        bool vitals = false, abnormals = false, moved = false, penaltyCleared = false;
        int hpTotal = 0, mpTotal = 0;
        List<(int ItemId, int Count)>? grants = null;
        (int X, int Y, int Z)? destination = null;

        foreach (var effect in use.Effects)
        {
            switch (effect.Kind)
            {
                case "buff":
                    // Mobius ItemSkills casts the item skill on its owner, so the abnormal and the cast gesture are the
                    // same pair a normal self-buff produces (CastBuff fans MagicSkillUse as well).
                    CastBuff(player, player, effect.SkillId,
                        Math.Max(1, effect.SkillLevel),
                        Math.Max(1, effect.AbnormalLevel),
                        Math.Max(1, effect.DurationSeconds));
                    abnormals = true;
                    break;
                case "healOverTime":
                    // Mobius HealOverTime ticks `power` HP `ticks` times over abnormalTime. The zone has no periodic
                    // heal scheduler, so the whole amount lands at once and the icon still shows for the duration.
                    hpTotal += Math.Max(0, effect.Power) * Math.Max(1, effect.Ticks);
                    CastBuff(player, player, effect.SkillId,
                        Math.Max(1, effect.SkillLevel),
                        Math.Max(1, effect.AbnormalLevel),
                        Math.Max(1, effect.DurationSeconds));
                    abnormals = true;
                    vitals = true;
                    break;
                case "heal":
                    hpTotal += Math.Max(0, effect.Power);
                    vitals = true;
                    break;
                case "manaHeal":
                    mpTotal += Math.Max(0, effect.Power);
                    vitals = true;
                    break;
                case "healPercent":
                    hpTotal += PercentOfMax(player.CurHp, player.MaxHp, effect.Power);
                    vitals = true;
                    break;
                case "manaHealPercent":
                    mpTotal += PercentOfMax(player.CurMp, player.MaxMp, effect.Power);
                    vitals = true;
                    break;
                case "healByLevel":
                    hpTotal += HealByLevel(player.Level, effect.Power, effect.SkillLevel);
                    vitals = true;
                    break;
                case "escapeTown":
                    destination = TownDestination(player);
                    moved = true;
                    break;
                case "teleport":
                    destination = (effect.X, effect.Y, effect.Z);
                    moved = true;
                    break;
                case "dispel":
                    // No poison/bleed debuff exists in the zone yet, so the antidote clears nothing but is still drunk.
                    abnormals |= ZoneAbnormals.DispelBySlot(player.Abnormals, effect.Slot, effect.Count) > 0;
                    break;
                case "restoration":
                    if (effect.ItemId > 0)
                        (grants ??= []).Add((effect.ItemId, Math.Max(1, effect.Count)));
                    break;
                case "recovery":
                    // Mobius EffectRecovery on Scroll: Recovery — clear the Death Penalty level.
                    if (player.DeathPenaltyLevel > 0)
                    {
                        player.DeathPenaltyLevel = 0;
                        penaltyCleared = true;
                    }

                    break;
                default:
                    return InventoryProtocol.ConditionRestricted;
            }
        }

        // Mobius destroys the item only when the handler's cast lands; a refused use must not eat the stack.
        if (!player.Inventory!.TryReduce(item, 1, out bool removedEntirely))
            return InventoryProtocol.NotOwned;

        if (hpTotal > 0)
            player.CurHp = Math.Min(player.MaxHp, player.CurHp + hpTotal);
        if (mpTotal > 0)
            player.CurMp = Math.Min(player.MaxMp, player.CurMp + mpTotal);

        List<ItemInstance>? added = null;
        if (grants != null)
        {
            foreach (var (itemId, count) in grants)
                (added ??= []).Add(player.Inventory.Add(itemId, count));
        }

        player.ItemReuseUntilMs[item.ItemId] = now + Math.Max(0, use.ReuseDelayMs);

        if (abnormals)
            FanoutAbnormal(player);
        if (vitals)
            FanoutVitals(player, PlayerVitalsStatus(player));
        if (penaltyCleared)
        {
            FanoutNetEtcStatus(player);
            SelfUserInfo(player);
        }

        if (added != null)
        {
            foreach (var stack in added)
            {
                NetBridge?.FanoutInventoryUpdate([session.ObjectId],
                    ToNetInventoryUpdate(InventoryMessages.Update(InventoryChange.Add, stack)));
            }
        }

        NetBridge?.FanoutInventoryUpdate([session.ObjectId],
            ToNetInventoryUpdate(InventoryMessages.Update(
                removedEntirely ? InventoryChange.Remove : InventoryChange.Modify, item)));

        if (moved && destination is { } loc)
        {
            Relocate(player, loc.X, loc.Y, loc.Z);
            // Mobius teleToLocation ends with a UserInfo so the client's own position and stats settle.
            SelfUserInfo(player);
        }

        if (vitals || moved)
            SaveVitals(session, player);
        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"consumable object={session.ObjectId} item={item.ItemId} removed={removedEntirely} " +
                     $"hp={player.CurHp}/{player.MaxHp} mp={player.CurMp}/{player.MaxMp} moved={moved}");
        return null;
    }

    /// <summary>
    /// Mobius <c>MapRegionManager.getTeleToLocation(creature, TOWN)</c>: the town square of the region that owns the
    /// player's grid cell, or the Talking Island restart when the cell belongs to no region.
    /// </summary>
    (int X, int Y, int Z) TownDestination(ZoneEntity player)
    {
        var point = _mapRegions.TownRespawn(player.X, player.Y);
        return (point.X, point.Y, point.Z);
    }

    static int PercentOfMax(int current, int max, int percent)
    {
        if (max <= 0 || percent <= 0)
            return 0;
        int room = Math.Max(0, max - Math.Clamp(current, 0, max));
        return (int)Math.Min((long)max * percent / 100, room);
    }

    /// <summary>Mobius <c>Heal</c> with a level table: power grows with the caster level (HP potions 1061 … 1065).</summary>
    static int HealByLevel(int level, int basePower, int step) =>
        Math.Max(0, basePower) + Math.Max(0, step) * Math.Max(0, level - 1);
}
