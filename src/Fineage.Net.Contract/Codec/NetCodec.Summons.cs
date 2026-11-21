namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeSummonCommand(SummonCommandNetMsg m) => Write(w =>
        {
            w.WriteByte((byte)m.Kind);
            w.WriteInt32(m.TargetObjectId);
            w.WriteInt32(m.SkillId);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteByte(m.Forced ? (byte)1 : (byte)0);
            w.WriteInt32(m.ItemObjectId);
            w.WriteInt32(m.Count);
        });

        public static SummonCommandNetMsg DecodeSummonCommand(System.ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new SummonCommandNetMsg
            {
                Kind = (SummonCommandKind)r.ReadByte(),
                TargetObjectId = r.ReadInt32(),
                SkillId = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Forced = r.ReadByte() != 0
            };
            if (r.Remaining >= 8)
            {
                m.ItemObjectId = r.ReadInt32();
                m.Count = r.ReadInt32();
            }
            return m;
        }

        public static byte[] EncodeSummonInfo(SummonInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteByte((byte)m.Kind);
            w.WriteInt32(m.NpcId);
            w.WriteString(m.Name);
            w.WriteInt32(m.Level);
            w.WriteInt32(m.Hp);
            w.WriteInt32(m.MaxHp);
            w.WriteInt32(m.Mp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt64(m.Exp);
            w.WriteInt64(m.ExpLevelStart);
            w.WriteInt64(m.ExpLevelEnd);
            w.WriteInt32(m.Feed);
            w.WriteInt32(m.MaxFeed);
            w.WriteInt32(m.PAtk);
            w.WriteInt32(m.PDef);
            w.WriteInt32(m.MAtk);
            w.WriteInt32(m.MDef);
            w.WriteInt32(m.Accuracy);
            w.WriteInt32(m.Evasion);
            w.WriteInt32(m.Critical);
            w.WriteInt32(m.PAtkSpd);
            w.WriteInt32(m.CastSpd);
            w.WriteInt32(m.RunSpeed);
            w.WriteByte(m.Following ? (byte)1 : (byte)0);
            w.WriteInt32(m.TargetObjectId);
            w.WriteInt32(m.Load);
            w.WriteInt32(m.MaxLoad);
            w.WriteInt32Array(m.SkillIds);
            w.WriteByte(m.Dead ? (byte)1 : (byte)0);
            w.WriteByte(m.Mountable ? (byte)1 : (byte)0);
        });

        public static SummonInfoNetMsg DecodeSummonInfo(System.ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new SummonInfoNetMsg
            {
                ObjectId = r.ReadInt32(),
                Kind = (SummonKindNet)r.ReadByte(),
                NpcId = r.ReadInt32(),
                Name = r.ReadString(),
                Level = r.ReadInt32(),
                Hp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                Mp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                Exp = r.ReadInt64(),
                ExpLevelStart = r.ReadInt64(),
                ExpLevelEnd = r.ReadInt64(),
                Feed = r.ReadInt32(),
                MaxFeed = r.ReadInt32(),
                PAtk = r.ReadInt32(),
                PDef = r.ReadInt32(),
                MAtk = r.ReadInt32(),
                MDef = r.ReadInt32(),
                Accuracy = r.ReadInt32(),
                Evasion = r.ReadInt32(),
                Critical = r.ReadInt32(),
                PAtkSpd = r.ReadInt32(),
                CastSpd = r.ReadInt32(),
                RunSpeed = r.ReadInt32(),
                Following = r.ReadByte() != 0,
                TargetObjectId = r.ReadInt32(),
                Load = r.ReadInt32(),
                MaxLoad = r.ReadInt32(),
                SkillIds = r.ReadInt32Array(),
                Dead = r.ReadByte() != 0
            };
            if (r.Remaining >= 1)
                m.Mountable = r.ReadByte() != 0;
            return m;
        }

        public static byte[] EncodeSummonGone(SummonGoneNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteByte((byte)m.Kind);
            w.WriteInt32(m.Reason);
        });

        public static SummonGoneNetMsg DecodeSummonGone(System.ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SummonGoneNetMsg { ObjectId = r.ReadInt32(), Kind = (SummonKindNet)r.ReadByte(), Reason = r.ReadInt32() };
        }

        public static byte[] EncodePetInventory(PetInventoryNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.PetObjectId);
            var items = m.Items ?? System.Array.Empty<PetItemNet>();
            w.WriteInt32(items.Length);
            foreach (var item in items)
            {
                w.WriteInt32(item.ObjectId);
                w.WriteInt32(item.ItemId);
                w.WriteInt32(item.Count);
                w.WriteInt32(item.EnchantLevel);
                w.WriteByte(item.Equipped ? (byte)1 : (byte)0);
            }
        });

        public static PetInventoryNetMsg DecodePetInventory(System.ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new PetInventoryNetMsg { PetObjectId = r.ReadInt32() };
            int n = r.ReadInt32();
            var items = new PetItemNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PetItemNet
                {
                    ObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt32(),
                    EnchantLevel = r.ReadInt32(),
                    Equipped = r.ReadByte() != 0
                };
            }
            m.Items = items;
            return m;
        }
    }
}
