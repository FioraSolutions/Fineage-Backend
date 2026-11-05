using Fineage.Game;

namespace Fineage.Zone.World;

/// <summary>
/// Net projection of Mobius UserInfo: vitals, base stats, live combat stats from <see cref="PlayerStats"/>,
/// speeds, paperdoll, so the Net status window and the owner's model match the server.
/// Every self UserInfo refresh goes through <see cref="SelfUserInfo"/>.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Build the Net UserInfo for a live player (Net enter path).</summary>
    internal bool TryBuildNetUserInfo(ZoneSession session, out Fineage.Net.Contract.UserInfoNetMsg info)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(session.ObjectId, out var e) && e.Kind == ZoneEntityKind.Player && e.Inventory != null)
            {
                info = NetUserInfo(e);
                return true;
            }
        }
        info = null!;
        return false;
    }

    internal Fineage.Net.Contract.UserInfoNetMsg NetUserInfo(ZoneEntity e)
    {
        var vitals = BuildUserInfoVitals(e);
        var slots = new Fineage.Net.Contract.PaperdollSlotNet[InventoryMessages.PaperdollSlotCount];
        if (e.Inventory != null)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                var item = e.Inventory.GetEquipped((PaperdollSlot)i);
                slots[i] = new Fineage.Net.Contract.PaperdollSlotNet
                {
                    ObjectId = item?.ObjectId ?? 0,
                    ItemId = item?.ItemId ?? 0,
                    EnchantLevel = item?.EnchantLevel ?? 0
                };
            }
        }

        return new Fineage.Net.Contract.UserInfoNetMsg
        {
            EntityValue = (ulong)(uint)e.ObjectId,
            EntityGeneration = 1,
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
            ClanId = e.ClanId,
            Paperdoll = slots
        };
    }

    /// <summary>Mobius <c>sendPacket(new UserInfo)</c>: the owner's sheet only, straight to its Net client.</summary>
    void SelfUserInfo(ZoneEntity entity) =>
        NetBridge?.FanoutUserInfo([entity.ObjectId], NetUserInfo(entity));
}
