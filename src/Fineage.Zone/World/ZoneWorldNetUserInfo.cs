using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Net projection of Mobius UserInfo: the same sheet V2 <see cref="UserInfoMsg"/> carries (vitals, base stats,
/// live combat stats from <see cref="PlayerStats"/>, speeds, paperdoll) so the Net status window and the
/// owner's model match the server. Every self UserInfo refresh goes through <see cref="SelfUserInfo"/>.
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
        var v2 = UserInfo(e);
        var stats = PlayerClasses.TryGet(e.ClassId, out var template) && template != null ? PlayerStats(e) : default;
        var slots = new Fineage.Net.Contract.PaperdollSlotNet[v2.Paperdoll.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            var src = v2.Paperdoll[i];
            slots[i] = new Fineage.Net.Contract.PaperdollSlotNet
            {
                ObjectId = src?.ObjectId ?? 0,
                ItemId = src?.ItemId ?? 0,
                EnchantLevel = src?.EnchantLevel ?? 0
            };
        }

        return new Fineage.Net.Contract.UserInfoNetMsg
        {
            EntityValue = (ulong)(uint)e.ObjectId,
            EntityGeneration = 1,
            Name = v2.Name,
            ClassId = v2.ClassId,
            Race = e.Race,
            Sex = e.Sex,
            X = v2.X,
            Y = v2.Y,
            Z = v2.Z,
            RunSpeed = v2.RunSpeed,
            WalkSpeed = v2.WalkSpeed,
            Running = v2.Running,
            Level = v2.Level,
            Exp = v2.Exp,
            Sp = v2.Sp,
            CurHp = v2.CurHp,
            MaxHp = v2.MaxHp,
            CurMp = v2.CurMp,
            MaxMp = v2.MaxMp,
            CurCp = v2.CurCp,
            MaxCp = v2.MaxCp,
            Str = v2.Str,
            Dex = v2.Dex,
            Con = v2.Con,
            Int = v2.Int,
            Wit = v2.Wit,
            Men = v2.Men,
            PAtk = v2.PAtk,
            MAtk = v2.MAtk,
            PDef = v2.PDef,
            MDef = v2.MDef,
            PAtkSpd = v2.PAtkSpd,
            MAtkSpd = v2.MAtkSpd,
            Accuracy = v2.Accuracy,
            Evasion = stats.Evasion,
            CritRate = v2.CritRate,
            WeaponGradePenalty = v2.WeaponGradePenalty,
            ArmorGradePenalty = v2.ArmorGradePenalty,
            ClanId = v2.ClanId,
            Paperdoll = slots
        };
    }

    /// <summary>V2 UserInfo to <paramref name="session"/> and the same sheet to its Net client (Mobius sendPacket(new UserInfo)).</summary>
    Pending SelfUserInfo(ZoneSession session, ZoneEntity entity)
    {
        NetBridge?.FanoutUserInfo([entity.ObjectId], NetUserInfo(entity));
        return Pack(session, MsgType.UserInfo, UserInfo(entity));
    }
}
