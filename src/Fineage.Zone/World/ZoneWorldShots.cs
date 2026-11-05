using Fineage.Game;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius SoulShots / SpiritShot / BlessedSpiritShot item handlers and RequestAutoSoulShot:
/// using a shot charges the equipped weapon (consumes weapon soulshots / spiritshots count), the next swing or
/// physical skill fires the soulshot, the next magic skill fires the spiritshot. With auto enabled the weapon is
/// recharged right after each discharge (Mobius rechargeShots) until the stack runs out.
/// </summary>
public sealed partial class ZoneWorld
{
    public const int SysMsgSoulshotGradeMismatch = 337;
    public const int SysMsgNotEnoughSoulshots = 338;
    public const int SysMsgCannotUseSoulshots = 339;
    public const int SysMsgSoulshotEnabled = 342;
    public const int SysMsgSpiritshotGradeMismatch = 530;
    public const int SysMsgNotEnoughSpiritshots = 531;
    public const int SysMsgCannotUseSpiritshots = 532;
    public const int SysMsgSpiritshotEnabled = 533;
    public const int SysMsgAutoShotOn = 1433;
    public const int SysMsgAutoShotOff = 1434;
    /// <summary>Spirit charge on <see cref="ZoneEntity.SpiritshotCharge"/>.</summary>
    public const int SpiritCharge = 1, BlessedSpiritCharge = 2;
    /// <summary>Mobius item_skill per grade (No grade, D … S).</summary>
    static readonly int[] SoulshotSkillByGrade = [2039, 2150, 2151, 2152, 2153, 2154];
    static readonly int[] SpiritshotSkillByGrade = [2047, 2155, 2156, 2157, 2158, 2159];
    static readonly int[] BlessedSpiritshotSkillByGrade = [2061, 2160, 2161, 2162, 2163, 2164];

    internal static bool IsShot(ItemTemplate t) => t.IsSoulshot || t.IsSpiritshot || t.IsBlessedSpiritshot;

    /// <summary>Charges the weapon with <paramref name="shot"/>. <paramref name="quiet"/> (auto recharge) skips the refusal notices.</summary>
    bool TryChargeShot(ZoneSession session, ZoneEntity player, ItemInstance shot, bool quiet)
    {
        var t = shot.Template;
        bool soul = t.IsSoulshot;
        var weapon = player.Inventory!.GetEquipped(PaperdollSlot.RHand)?.Template;
        int need = weapon == null || weapon.Kind != ItemKind.Weapon ? 0 : soul ? weapon.Soulshots : weapon.Spiritshots;
        if (need <= 0)
        {
            if (!quiet)
                ShotNotice(session, soul ? SysMsgCannotUseSoulshots : SysMsgCannotUseSpiritshots,
                    soul ? "Cannot use soulshots." : "Cannot use spiritshots.");
            return false;
        }
        if (weapon!.Crystal != t.ShotGrade)
        {
            if (!quiet)
                ShotNotice(session, soul ? SysMsgSoulshotGradeMismatch : SysMsgSpiritshotGradeMismatch,
                    soul ? "The soulshot you are attempting to use does not match the grade of your equipped weapon."
                         : "Your spiritshot does not match the weapon's grade.");
            return false;
        }
        if (soul ? player.SoulshotCharged : player.SpiritshotCharge > 0)
            return false; // Mobius: already charged, nothing consumed

        if (!player.Inventory.TryConsume(shot.ItemId, need, out var stack, out bool removed) || stack == null)
        {
            ShotNotice(session, soul ? SysMsgNotEnoughSoulshots : SysMsgNotEnoughSpiritshots,
                soul ? "You do not have enough soulshots for that." : "You do not have enough spiritshots for that.");
            // Mobius: auto use switches itself off when the stack cannot pay for a charge.
            if (player.AutoShots.Remove(shot.ItemId))
                SendAutoShotState(session, player, shot.ItemId, t.Name, false);
            return false;
        }

        if (soul)
            player.SoulshotCharged = true;
        else
            player.SpiritshotCharge = t.IsBlessedSpiritshot ? BlessedSpiritCharge : SpiritCharge;
        var update = InventoryMessages.Update(removed ? InventoryChange.Remove : InventoryChange.Modify, stack);
        NetBridge?.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(update));
        ShotNotice(session, soul ? SysMsgSoulshotEnabled : SysMsgSpiritshotEnabled,
            soul ? "Your soulshots are enabled." : "Your spiritshot has been enabled.");

        // Mobius broadcasts MagicSkillUse(item_skill) for the weapon glow.
        int[] table = soul ? SoulshotSkillByGrade : t.IsBlessedSpiritshot ? BlessedSpiritshotSkillByGrade : SpiritshotSkillByGrade;
        int skill = table[Math.Clamp((int)t.ShotGrade, 0, table.Length - 1)];
        var cast = new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = skill,
            SkillLevel = 1
        };
        List<int> netWatchers = [player.ObjectId];
        foreach (var watcher in Players())
        {
            if (watcher.Session == null || ReferenceEquals(watcher, player) || !watcher.Knownlist.Contains(player.ObjectId))
                continue;
            netWatchers.Add(watcher.ObjectId);
        }
        NetBridge?.FanoutMagicSkillUse(netWatchers, new Fineage.Net.Contract.MagicSkillUseNetMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = skill,
            SkillLevel = 1
        });

        SaveInventory(session, player.Inventory);
        ZoneLog.Info($"shot object={player.ObjectId} item={shot.ItemId} consumed={need} left={(removed ? 0 : stack.Count)} auto={quiet}");
        if (removed && player.AutoShots.Remove(shot.ItemId))
            SendAutoShotState(session, player, shot.ItemId, t.Name, false);
        return true;
    }

    /// <summary>Mobius RequestAutoSoulShot (type 1 on / 0 off) for a shot item the player carries.</summary>
    public Task<string?> TryAutoSoulShotAsync(ZoneSession session, int itemId, bool enable, CancellationToken ct)
    {
        string? error;
        lock (_gate)
            error = TryAutoSoulShot(session, itemId, enable);
        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

    string? TryAutoSoulShot(ZoneSession session, int itemId, bool enable)
    {
        if (!TryLivePlayer(session, out var player, out var error))
            return error;
        var stack = player.Inventory!.FindByItemId(itemId);
        if (!enable)
        {
            if (player.AutoShots.Remove(itemId))
                SendAutoShotState(session, player, itemId, stack?.Template.Name ?? "", false);
            return null;
        }
        if (stack == null)
            return InventoryProtocol.NotOwned;
        if (!IsShot(stack.Template))
            return "not_a_shot";
        // Mobius: one auto slot per shot kind; switching grades replaces the old one.
        foreach (int other in player.AutoShots.ToList())
        {
            var o = player.Inventory.FindByItemId(other)?.Template;
            if (o == null || o.IsSoulshot == stack.Template.IsSoulshot)
            {
                player.AutoShots.Remove(other);
                SendAutoShotState(session, player, other, o?.Name ?? "", false);
            }
        }
        player.AutoShots.Add(itemId);
        SendAutoShotState(session, player, itemId, stack.Template.Name, true);
        TryChargeShot(session, player, stack, quiet: false);
        return null;
    }

    /// <summary>Mobius rechargeShots after a discharge: charge again from each auto-enabled stack.</summary>
    void RechargeAutoShots(ZoneEntity player)
    {
        if (player.AutoShots.Count == 0 || player.Session == null || player.Inventory == null)
            return;
        foreach (int itemId in player.AutoShots.ToList())
        {
            var stack = player.Inventory.FindByItemId(itemId);
            if (stack == null)
            {
                player.AutoShots.Remove(itemId);
                SendAutoShotState(player.Session, player, itemId, "", false);
                continue;
            }
            TryChargeShot(player.Session, player, stack, quiet: true);
        }
    }

    /// <summary>Test seam: fire the soulshot charge as a swing would, then run the auto recharge.</summary>
    internal void DischargeSoulshot(ZoneEntity player)
    {
        lock (_gate)
        {
            player.SoulshotCharged = false;
            RechargeAutoShots(player);
        }
    }

    void SendAutoShotState(ZoneSession session, ZoneEntity player, int itemId, string name, bool enabled)
    {
        NetBridge?.FanoutAutoSoulShot([player.ObjectId], itemId, enabled);
        ShotNotice(session, enabled ? SysMsgAutoShotOn : SysMsgAutoShotOff,
            $"The automatic use of {name} has been {(enabled ? "activated" : "deactivated")}.");
    }

    void ShotNotice(ZoneSession session, int id, string text)
    {
        NetBridge?.FanoutSystemMessage([session.ObjectId], new Fineage.Net.Contract.SystemMessageNetMsg
        {
            Id = id,
            Text = text,
            Params = []
        });
    }
}
