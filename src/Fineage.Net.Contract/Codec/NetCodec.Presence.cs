using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeEntitySpawn(EntitySpawnMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteByte((byte)m.Kind);
            w.WriteByte(m.IsSelf ? (byte)1 : (byte)0);
            w.WriteString(m.Name);
            w.WriteInt32(m.TemplateId);
            w.WriteInt32(m.ClassId);
            w.WriteInt32(m.Race);
            w.WriteInt32(m.Sex);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteInt32(m.Heading);
            w.WriteFloat(m.RunSpeed);
            w.WriteFloat(m.WalkSpeed);
            w.WriteByte(m.Running ? (byte)1 : (byte)0);
            w.WriteByte(m.Attackable ? (byte)1 : (byte)0);
            w.WriteInt32Array(m.Paperdoll);
            w.WriteByte(m.Dead ? (byte)1 : (byte)0);
            w.WriteInt32(m.Level);
            w.WriteInt32(m.MaxHp);
            w.WriteInt32(m.PAtkSpd);
            w.WriteInt32(m.Karma);
            w.WriteByte(m.PvpFlag);
            w.WriteInt32(m.CurHp);
            w.WriteString(m.Title ?? "");
            w.WriteInt32(m.ClanId);
            w.WriteInt32(m.ClanCrestId);
            w.WriteInt32(m.AllyId);
            w.WriteInt32(m.AllyCrestId);
            w.WriteInt32(m.LargeCrestId);
            w.WriteInt32(m.PledgeClass);
            w.WriteInt32(m.WeaponEnchant);
            w.WriteByte(m.Noble ? (byte)1 : (byte)0);
            w.WriteByte(m.Hero ? (byte)1 : (byte)0);
            w.WriteByte((byte)m.SummonKind);
            w.WriteInt32(m.OwnerObjectId);
        });

        public static EntitySpawnMsg DecodeEntitySpawn(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var msg = new EntitySpawnMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                Kind = (EntityKindNet)r.ReadByte(),
                IsSelf = r.ReadByte() != 0,
                Name = r.ReadString(),
                TemplateId = r.ReadInt32(),
                ClassId = r.ReadInt32(),
                Race = r.ReadInt32(),
                Sex = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Heading = r.ReadInt32(),
                RunSpeed = r.ReadFloat(),
                WalkSpeed = r.ReadFloat(),
                Running = r.ReadByte() != 0
            };
            if (r.Remaining > 0)
                msg.Attackable = r.ReadByte() != 0;
            if (r.Remaining > 0)
                msg.Paperdoll = r.ReadInt32Array();
            if (r.Remaining > 0)
                msg.Dead = r.ReadByte() != 0;
            if (r.Remaining > 0)
                msg.Level = r.ReadInt32();
            if (r.Remaining > 0)
                msg.MaxHp = r.ReadInt32();
            if (r.Remaining > 0)
                msg.PAtkSpd = r.ReadInt32();
            if (r.Remaining > 0)
                msg.Karma = r.ReadInt32();
            if (r.Remaining > 0)
                msg.PvpFlag = r.ReadByte();
            if (r.Remaining > 0)
                msg.CurHp = r.ReadInt32();
            else if (msg.Dead)
                msg.CurHp = 0;
            else if (msg.MaxHp > 0)
                msg.CurHp = msg.MaxHp;
            if (r.Remaining > 0)
            {
                msg.Title = r.ReadString();
                msg.ClanId = r.ReadInt32();
                msg.ClanCrestId = r.ReadInt32();
                msg.AllyId = r.ReadInt32();
                msg.AllyCrestId = r.ReadInt32();
                msg.LargeCrestId = r.ReadInt32();
                msg.PledgeClass = r.ReadInt32();
            }
            if (r.Remaining > 0)
                msg.WeaponEnchant = r.ReadInt32();
            if (r.Remaining >= 2)
            {
                msg.Noble = r.ReadByte() != 0;
                msg.Hero = r.ReadByte() != 0;
            }
            if (r.Remaining >= 5)
            {
                msg.SummonKind = (SummonKindNet)r.ReadByte();
                msg.OwnerObjectId = r.ReadInt32();
            }
            return msg;
        }

        public static byte[] EncodeUserInfo(UserInfoNetMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteString(m.Name);
            w.WriteInt32(m.ClassId);
            w.WriteInt32(m.Race);
            w.WriteInt32(m.Sex);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteFloat(m.RunSpeed);
            w.WriteFloat(m.WalkSpeed);
            w.WriteByte(m.Running ? (byte)1 : (byte)0);
            w.WriteInt32(m.Level);
            w.WriteInt64(m.Exp);
            w.WriteInt32(m.Sp);
            w.WriteInt32(m.CurHp);
            w.WriteInt32(m.MaxHp);
            w.WriteInt32(m.CurMp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt32(m.CurCp);
            w.WriteInt32(m.MaxCp);
            w.WriteInt32(m.Str);
            w.WriteInt32(m.Dex);
            w.WriteInt32(m.Con);
            w.WriteInt32(m.Int);
            w.WriteInt32(m.Wit);
            w.WriteInt32(m.Men);
            w.WriteInt32(m.PAtk);
            w.WriteInt32(m.MAtk);
            w.WriteInt32(m.PDef);
            w.WriteInt32(m.MDef);
            w.WriteInt32(m.PAtkSpd);
            w.WriteInt32(m.MAtkSpd);
            w.WriteInt32(m.Accuracy);
            w.WriteInt32(m.Evasion);
            w.WriteInt32(m.CritRate);
            w.WriteInt32(m.WeaponGradePenalty);
            w.WriteInt32(m.ArmorGradePenalty);
            w.WriteInt32(m.ClanId);
            w.WriteInt32(m.CurrentWeight);
            w.WriteInt32(m.MaxWeight);
            var slots = m.Paperdoll ?? Array.Empty<PaperdollSlotNet>();
            w.WriteInt32(slots.Length);
            for (int i = 0; i < slots.Length; i++)
            {
                w.WriteInt32(slots[i]?.ObjectId ?? 0);
                w.WriteInt32(slots[i]?.ItemId ?? 0);
                w.WriteInt32(slots[i]?.EnchantLevel ?? 0);
            }
            w.WriteInt32(m.Karma);
            w.WriteByte(m.PvpFlag);
            w.WriteInt32(m.PvpKills);
            w.WriteInt32(m.PkKills);
            w.WriteString(m.Title ?? "");
            w.WriteInt32(m.ClanCrestId);
            w.WriteInt32(m.LargeCrestId);
            w.WriteInt32(m.AllyId);
            w.WriteInt32(m.AllyCrestId);
            w.WriteInt32(m.ClanPrivileges);
            w.WriteInt32(m.PledgeClass);
            w.WriteInt32(m.PledgeType);
            w.WriteByte(m.ClanLeader ? (byte)1 : (byte)0);
            w.WriteByte(m.Noble ? (byte)1 : (byte)0);
            w.WriteByte(m.Hero ? (byte)1 : (byte)0);
            w.WriteInt32(m.BaseClassId);
            w.WriteInt32(m.ClassIndex);
        });

        public static UserInfoNetMsg DecodeUserInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new UserInfoNetMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                Name = r.ReadString(),
                ClassId = r.ReadInt32(),
                Race = r.ReadInt32(),
                Sex = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                RunSpeed = r.ReadFloat(),
                WalkSpeed = r.ReadFloat(),
                Running = r.ReadByte() != 0,
                Level = r.ReadInt32(),
                Exp = r.ReadInt64(),
                Sp = r.ReadInt32(),
                CurHp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                CurMp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                CurCp = r.ReadInt32(),
                MaxCp = r.ReadInt32(),
                Str = r.ReadInt32(),
                Dex = r.ReadInt32(),
                Con = r.ReadInt32(),
                Int = r.ReadInt32(),
                Wit = r.ReadInt32(),
                Men = r.ReadInt32(),
                PAtk = r.ReadInt32(),
                MAtk = r.ReadInt32(),
                PDef = r.ReadInt32(),
                MDef = r.ReadInt32(),
                PAtkSpd = r.ReadInt32(),
                MAtkSpd = r.ReadInt32(),
                Accuracy = r.ReadInt32(),
                Evasion = r.ReadInt32(),
                CritRate = r.ReadInt32(),
                WeaponGradePenalty = r.ReadInt32(),
                ArmorGradePenalty = r.ReadInt32(),
                ClanId = r.ReadInt32(),
                CurrentWeight = r.ReadInt32(),
                MaxWeight = r.ReadInt32()
            };
            int n = r.ReadInt32();
            if (n < 0 || n > 64)
                throw new InvalidDataException("paperdoll length");
            var slots = new PaperdollSlotNet[n];
            for (int i = 0; i < n; i++)
                slots[i] = new PaperdollSlotNet { ObjectId = r.ReadInt32(), ItemId = r.ReadInt32(), EnchantLevel = r.ReadInt32() };
            m.Paperdoll = slots;
            if (r.Remaining > 0)
                m.Karma = r.ReadInt32();
            if (r.Remaining > 0)
                m.PvpFlag = r.ReadByte();
            if (r.Remaining >= 8)
            {
                m.PvpKills = r.ReadInt32();
                m.PkKills = r.ReadInt32();
            }
            if (r.Remaining > 0)
            {
                m.Title = r.ReadString();
                m.ClanCrestId = r.ReadInt32();
                m.LargeCrestId = r.ReadInt32();
                m.AllyId = r.ReadInt32();
                m.AllyCrestId = r.ReadInt32();
                m.ClanPrivileges = r.ReadInt32();
                m.PledgeClass = r.ReadInt32();
                m.PledgeType = r.ReadInt32();
                m.ClanLeader = r.ReadByte() != 0;
            }
            if (r.Remaining >= 10)
            {
                m.Noble = r.ReadByte() != 0;
                m.Hero = r.ReadByte() != 0;
                m.BaseClassId = r.ReadInt32();
                m.ClassIndex = r.ReadInt32();
            }
            return m;
        }

        public static byte[] EncodePlayerStats(PlayerStatsMsg m) => Write(w =>
        {
            w.WriteUInt64(m.EntityValue);
            w.WriteUInt32(m.EntityGeneration);
            w.WriteInt32(m.Level);
            w.WriteInt64(m.Exp);
            w.WriteInt32(m.Sp);
            w.WriteInt32(m.Hp);
            w.WriteInt32(m.MaxHp);
            w.WriteInt32(m.Mp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt32(m.Cp);
            w.WriteInt32(m.MaxCp);
        });

        public static PlayerStatsMsg DecodePlayerStats(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PlayerStatsMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                Level = r.ReadInt32(),
                Exp = r.ReadInt64(),
                Sp = r.ReadInt32(),
                Hp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                Mp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                Cp = r.ReadInt32(),
                MaxCp = r.ReadInt32()
            };
        }
    }
}
