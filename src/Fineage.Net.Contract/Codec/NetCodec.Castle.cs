using System;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeDoorStatus(DoorStatusNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.DoorId);
            w.WriteByte(m.Open ? (byte)1 : (byte)0);
            w.WriteInt32(m.CurHp);
            w.WriteInt32(m.MaxHp);
            w.WriteByte(m.Attackable ? (byte)1 : (byte)0);
            w.WriteByte(m.Wall ? (byte)1 : (byte)0);
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.HallId);
            w.WriteInt32(m.UpgradeRatio);
        });

        public static DoorStatusNetMsg DecodeDoorStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DoorStatusNetMsg
            {
                ObjectId = r.ReadInt32(),
                DoorId = r.ReadInt32(),
                Open = r.ReadByte() != 0,
                CurHp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                Attackable = r.ReadByte() != 0,
                Wall = r.ReadByte() != 0,
                CastleId = r.ReadInt32(),
                HallId = r.ReadInt32(),
                UpgradeRatio = r.ReadInt32()
            };
        }

        public static byte[] EncodeCastleCommand(CastleCommandNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Kind);
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.IntArg1);
            w.WriteInt32(m.IntArg2);
            w.WriteInt64(m.Amount);
        });

        public static CastleCommandNetMsg DecodeCastleCommand(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new CastleCommandNetMsg
            {
                Kind = r.ReadInt32(),
                CastleId = r.ReadInt32(),
                IntArg1 = r.ReadInt32(),
                IntArg2 = r.ReadInt32(),
                Amount = r.ReadInt64()
            };
        }

        public static byte[] EncodeCastleInfo(CastleInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.OwnerClanId);
            w.WriteString(m.OwnerClanName ?? "");
            w.WriteInt32(m.TaxPercent);
            w.WriteInt32(m.MaxTaxPercent);
            w.WriteInt64(m.Treasury);
            w.WriteInt64(m.SiegeUnixMs);
            w.WriteByte(m.InProgress ? (byte)1 : (byte)0);
            var doors = m.Doors ?? Array.Empty<CastleDoorNet>();
            w.WriteInt32(doors.Length);
            foreach (var d in doors)
            {
                w.WriteInt32(d.DoorId);
                w.WriteInt32(d.UpgradeType);
                w.WriteInt32(d.UpgradeRatio);
                w.WriteInt32(d.CurHp);
                w.WriteInt32(d.MaxHp);
                w.WriteByte(d.Open ? (byte)1 : (byte)0);
            }
        });

        public static CastleInfoNetMsg DecodeCastleInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var info = new CastleInfoNetMsg
            {
                CastleId = r.ReadInt32(),
                Name = r.ReadString(),
                OwnerClanId = r.ReadInt32(),
                OwnerClanName = r.ReadString(),
                TaxPercent = r.ReadInt32(),
                MaxTaxPercent = r.ReadInt32(),
                Treasury = r.ReadInt64(),
                SiegeUnixMs = r.ReadInt64(),
                InProgress = r.ReadByte() != 0
            };
            int n = ReadCount(ref r, 64);
            var doors = new CastleDoorNet[n];
            for (int i = 0; i < n; i++)
            {
                doors[i] = new CastleDoorNet
                {
                    DoorId = r.ReadInt32(),
                    UpgradeType = r.ReadInt32(),
                    UpgradeRatio = r.ReadInt32(),
                    CurHp = r.ReadInt32(),
                    MaxHp = r.ReadInt32(),
                    Open = r.ReadByte() != 0
                };
            }

            info.Doors = doors;
            return info;
        }
    }
}
