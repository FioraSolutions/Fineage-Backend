using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Inventory;

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
    public const string InventoryReuse = ConsumableRules.InventoryReuse;

    bool IsConsumable(int itemId) => _itemSkills.TryGet(itemId, out _);

    /// <summary>
    /// Runs the item's skill list and consumes one. Returns null on success or an
    /// <see cref="InventoryProtocol"/> / <see cref="InventoryReuse"/> code the Net client turns into a chat line.
    /// </summary>
    string? TryUseConsumable(ZoneSession session, ZoneEntity player, ItemInstance item)
    {
        bool known = _itemSkills.TryGet(item.ItemId, out var use);
        long now = _clock().ToUnixTimeMilliseconds();
        // Mobius keeps one reuse stamp per item id on the character; a stack shares that timer.
        bool onReuse = known
            && use!.ReuseDelayMs > 0
            && player.ItemReuseUntilMs.TryGetValue(item.ItemId, out long reuseUntil)
            && reuseUntil > now;
        if (ConsumableRules.UsePrecheckError(known, onReuse) is { } precheck)
            return precheck;

        // Scroll of Escape / Blessed SOE: cast gesture + hitTime teleport (skills 2013 / 2036 are not in skills.json).
        if (use!.Effects is [{ Kind: "escapeTown" } escape])
            return TryUseEscapeTown(session, player, item, escape, use.ReuseDelayMs, now);

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
                    hpTotal += ConsumableRules.PercentHeal(player.CurHp, player.MaxHp, effect.Power);
                    vitals = true;
                    break;
                case "manaHealPercent":
                    mpTotal += ConsumableRules.PercentHeal(player.CurMp, player.MaxMp, effect.Power);
                    vitals = true;
                    break;
                case "healByLevel":
                    hpTotal += HealByLevel(player.Level, effect.Power, effect.SkillLevel);
                    vitals = true;
                    break;
                case "escapeTown":
                    // Mixed-effect items with escapeTown still teleport instantly; pure SOE uses TryUseEscapeTown.
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
                NetBridge?.FanoutInventoryUpdate([session.ObjectId], InventoryMessages.Update(InventoryChange.Add, stack));
            }
        }

        NetBridge?.FanoutInventoryUpdate([session.ObjectId], InventoryMessages.Update(
                removedEntirely ? InventoryChange.Remove : InventoryChange.Modify, item));

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

    /// <summary>
    /// Interlude item skills 2013 (SOE) / 2036 (Blessed SOE) are not in <c>skills.json</c>; hit times match Mobius XML.
    /// </summary>
    /// <summary>
    /// Mobius ItemSkills for Scroll of Escape: <c>MagicSkillUse</c> now, consume + <c>teleToLocation(TOWN)</c> at hitTime.
    /// </summary>
    string? TryUseEscapeTown(
        ZoneSession session,
        ZoneEntity player,
        ItemInstance item,
        ItemSkillEffect effect,
        int reuseDelayMs,
        long now)
    {
        if (ConsumableRules.EscapeTownError(player.Dead, now < player.CastingUntilMs) is { } escapeError)
            return escapeError;

        int skillId = effect.SkillId > 0 ? effect.SkillId : 2013;
        int level = Math.Max(1, effect.SkillLevel);
        int hitTimeMs = ConsumableRules.EscapeHitTimeMs(skillId);
        var (x, y, z) = TownDestination(player);

        // Fan the cast gesture first so the client starts long-cast / short-cast before the teleport lands.
        var cast = new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = hitTimeMs,
            ReuseDelayMs = Math.Max(0, reuseDelayMs)
        };
        FanoutSkill(player, player, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + hitTimeMs;

        var template = new SkillDefinition(
            skillId,
            effect.Name ?? "Scroll of Escape",
            passive: false,
            operateType: "A1",
            targetType: "SELF",
            castRange: 0,
            hitTimeMs: hitTimeMs,
            reuseDelayMs: Math.Max(0, reuseDelayMs),
            abnormalTimeSeconds: 0,
            isDebuff: false,
            levels: [new SkillLevelData(level, mpConsume: 0, power: 0)])
        {
            MagicType = 1
        };

        // Consume + teleport land together (Mobius destroys the scroll when the cast succeeds).
        ScheduleEffect(player, player, template, level, now, hitTimeMs, (_, __) =>
        {
            if (!player.Inventory!.TryReduce(item, 1, out bool removedEntirely))
                return;

            player.ItemReuseUntilMs[item.ItemId] = _clock().ToUnixTimeMilliseconds() + Math.Max(0, reuseDelayMs);
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], InventoryMessages.Update(
                    removedEntirely ? InventoryChange.Remove : InventoryChange.Modify, item));
            Relocate(player, x, y, z);
            SelfUserInfo(player);
            SaveVitals(session, player);
            SaveInventory(session, player.Inventory);
            ZoneLog.Info($"escape object={session.ObjectId} item={item.ItemId} skill={skillId} -> {x},{y},{z}");
        });

        return null;
    }

    /// <summary>Mobius <c>Heal</c> with a level table: power grows with the caster level (HP potions 1061 … 1065).</summary>
    static int HealByLevel(int level, int basePower, int step) =>
        Math.Max(0, basePower) + Math.Max(0, step) * Math.Max(0, level - 1);
}
