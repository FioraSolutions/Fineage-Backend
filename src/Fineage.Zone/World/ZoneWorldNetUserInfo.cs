using Fineage.Game;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Projection of Mobius UserInfo: vitals, base stats, live combat stats from <see cref="PlayerStats"/>,
/// speeds, paperdoll, so the status window and the owner's model match the server.
/// Every self UserInfo refresh goes through <see cref="SelfUserInfo"/>.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Build the UserInfo for a live player (enter path + fanout).</summary>
    internal bool TryBuildNetUserInfo(ZoneSession session, out UserInfoMsg info)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(session.ObjectId, out var e) && e.Kind == ZoneEntityKind.Player && e.Inventory != null)
            {
                info = BuildUserInfo(e);
                return true;
            }
        }
        info = null!;
        return false;
    }

    internal UserInfoMsg BuildUserInfo(ZoneEntity e)
    {
        var vitals = BuildUserInfoVitals(e);
        var slots = new PaperdollSlotMsg[InventoryMessages.PaperdollSlotCount];
        if (e.Inventory != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var item = e.Inventory.GetEquipped((PaperdollSlot)i);
                slots[i] = new PaperdollSlotMsg
                {
                    ObjectId = item?.ObjectId ?? 0,
                    ItemId = item?.ItemId ?? 0,
                    EnchantLevel = item?.EnchantLevel ?? 0
                };
            }
        }

        return new UserInfoMsg
        {
            ObjectId = e.ObjectId,
            Name = e.Name,
            ClassId = e.ClassId,
            Race = e.Race,
            Sex = e.Sex,
            X = e.X,
            Y = e.Y,
            Z = e.Z,
            RunSpeed = e.RunSpeed,
            WalkSpeed = e.WalkSpeed,
            Running = e.Running,
            Level = vitals.Level,
            Exp = vitals.Exp,
            Sp = vitals.Sp,
            CurHp = vitals.CurHp,
            MaxHp = vitals.MaxHp,
            CurMp = vitals.CurMp,
            MaxMp = vitals.MaxMp,
            CurCp = vitals.CurCp,
            MaxCp = vitals.MaxCp,
            Str = vitals.Str,
            Dex = vitals.Dex,
            Con = vitals.Con,
            Int = vitals.Int,
            Wit = vitals.Wit,
            Men = vitals.Men,
            PAtk = vitals.PAtk,
            MAtk = vitals.MAtk,
            PDef = vitals.PDef,
            MDef = vitals.MDef,
            PAtkSpd = vitals.PAtkSpd,
            MAtkSpd = vitals.MAtkSpd,
            Accuracy = vitals.Accuracy,
            Evasion = vitals.Evasion,
            CritRate = vitals.CritRate,
            WeaponGradePenalty = e.WeaponGradePenalty,
            ArmorGradePenalty = e.ArmorGradePenalty,
            GradePenalty = Math.Max(e.WeaponGradePenalty, e.ArmorGradePenalty),
            ClanId = e.ClanId,
            Paperdoll = slots
        };
    }

    /// <summary>Legacy name used by clan / gear paths; same as <see cref="BuildUserInfo"/>.</summary>
    internal UserInfoMsg NetUserInfo(ZoneEntity e) => BuildUserInfo(e);

    /// <summary>Mobius <c>sendPacket(new UserInfo)</c>: the owner's sheet only, straight to its Net client.</summary>
    void SelfUserInfo(ZoneEntity entity) =>
        NetBridge?.FanoutUserInfo([entity.ObjectId], BuildUserInfo(entity));
}
