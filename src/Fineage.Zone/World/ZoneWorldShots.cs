using Fineage.Game;
using Fineage.Protocol;
using MessagePack;

namespace Fineage.Zone.World;

/// <summary>Mobius SoulShots item handler: charge the equipped weapon, consume on the next swing.</summary>
public sealed partial class ZoneWorld
{
    public const int SysMsgSoulshotGradeMismatch = 337;
    public const int SysMsgNotEnoughSoulshots = 338;
    public const int SysMsgCannotUseSoulshots = 339;
    public const int SysMsgSoulshotEnabled = 342;
    /// <summary>Mobius item_skill per soulshot grade (No grade 2039, D 2150 … S 2154).</summary>
    static readonly int[] SoulshotSkillByGrade = [2039, 2150, 2151, 2152, 2153, 2154];

    string? TryChargeSoulshot(ZoneSession session, ZoneEntity player, ItemInstance shot, List<Pending> toSelf, List<Pending> toOthers)
    {
        var weapon = player.Inventory!.GetEquipped(PaperdollSlot.RHand)?.Template;
        if (weapon == null || weapon.Kind != ItemKind.Weapon || weapon.Soulshots <= 0)
        {
            ShotNotice(session, SysMsgCannotUseSoulshots, "Cannot use soulshots.", toSelf);
            return null;
        }
        if (weapon.Crystal != shot.Template.ShotGrade)
        {
            ShotNotice(session, SysMsgSoulshotGradeMismatch,
                "The soulshot you are attempting to use does not match the grade of your equipped weapon.", toSelf);
            return null;
        }
        if (player.SoulshotCharged)
            return null; // Mobius: already charged, nothing consumed

        if (!player.Inventory.TryConsume(shot.ItemId, weapon.Soulshots, out var stack, out bool removed) || stack == null)
        {
            ShotNotice(session, SysMsgNotEnoughSoulshots, "You do not have enough soulshots for that.", toSelf);
            return null;
        }

        player.SoulshotCharged = true;
        var update = InventoryMessages.Update(removed ? InventoryChange.Remove : InventoryChange.Modify, stack);
        toSelf.Add(Pack(session, MsgType.InventoryUpdate, update));
        NetBridge?.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(update));
        ShotNotice(session, SysMsgSoulshotEnabled, "Your soulshots are enabled.", toSelf);

        // Mobius broadcasts MagicSkillUse(item_skill) for the weapon glow.
        int skill = SoulshotSkillByGrade[Math.Clamp((int)shot.Template.ShotGrade, 0, SoulshotSkillByGrade.Length - 1)];
        var cast = new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = skill,
            SkillLevel = 1
        };
        byte[] body = MessagePackSerializer.Serialize(cast);
        List<int> netWatchers = [player.ObjectId];
        toSelf.Add(new Pending(session, MsgType.MagicSkillUse, body));
        foreach (var watcher in Players())
        {
            if (watcher.Session == null || ReferenceEquals(watcher, player) || !watcher.Knownlist.Contains(player.ObjectId))
                continue;
            toOthers.Add(new Pending(watcher.Session, MsgType.MagicSkillUse, body));
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
        ZoneLog.Info($"soulshot object={player.ObjectId} item={shot.ItemId} consumed={weapon.Soulshots} left={(removed ? 0 : stack.Count)}");
        return null;
    }

    void ShotNotice(ZoneSession session, int id, string text, List<Pending> toSelf)
    {
        toSelf.Add(Pack(session, MsgType.SystemMessage, new SystemMessageMsg { Id = id, Params = [], Text = text }));
        NetBridge?.FanoutSystemMessage([session.ObjectId], new Fineage.Net.Contract.SystemMessageNetMsg
        {
            Id = id,
            Text = text,
            Params = []
        });
    }
}
