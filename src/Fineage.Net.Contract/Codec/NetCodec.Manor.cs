using System;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        const int MaxManorRows = 512;

        public static byte[] EncodeManorCommand(ManorCommandNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Kind);
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.NpcObjectId);
            w.WriteByte(m.Next ? (byte)1 : (byte)0);
            var rows = m.Rows ?? Array.Empty<ManorRowNet>();
            w.WriteInt32(rows.Length);
            foreach (var r in rows)
            {
                w.WriteInt32(r.ItemId);
                w.WriteInt32(r.ObjectId);
                w.WriteInt32(r.ManorId);
                w.WriteInt64(r.Count);
                w.WriteInt64(r.Price);
                w.WriteInt32(r.RewardType);
            }
        });

        public static ManorCommandNetMsg DecodeManorCommand(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new ManorCommandNetMsg
            {
                Kind = r.ReadInt32(),
                CastleId = r.ReadInt32(),
                NpcObjectId = r.ReadInt32(),
                Next = r.ReadByte() != 0
            };
            int n = ReadCount(ref r, MaxManorRows);
            var rows = new ManorRowNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new ManorRowNet
                {
                    ItemId = r.ReadInt32(),
                    ObjectId = r.ReadInt32(),
                    ManorId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64(),
                    RewardType = r.ReadInt32()
                };
            }

            m.Rows = rows;
            return m;
        }

        public static byte[] EncodeManorInfo(ManorInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.View);
            w.WriteInt32(m.CastleId);
            w.WriteByte(m.Next ? (byte)1 : (byte)0);
            w.WriteInt32(m.Mode);
            w.WriteInt64(m.NextChangeUnixMs);
            w.WriteInt64(m.Treasury);
            w.WriteInt64(m.ManorCost);
            w.WriteInt64(m.Adena);
            var seeds = m.Seeds ?? Array.Empty<ManorSeedNet>();
            w.WriteInt32(seeds.Length);
            foreach (var s in seeds)
            {
                w.WriteInt32(s.SeedId);
                w.WriteInt32(s.CropId);
                w.WriteInt32(s.Level);
                w.WriteInt32(s.Reward1);
                w.WriteInt32(s.Reward2);
                w.WriteByte(s.Alternative ? (byte)1 : (byte)0);
                w.WriteInt64(s.Limit);
                w.WriteInt64(s.ReferencePrice);
                w.WriteInt64(s.MinPrice);
                w.WriteInt64(s.MaxPrice);
                w.WriteInt64(s.Amount);
                w.WriteInt64(s.StartAmount);
                w.WriteInt64(s.Price);
                w.WriteInt64(s.CurrentStartAmount);
                w.WriteInt64(s.CurrentPrice);
            }

            var crops = m.Crops ?? Array.Empty<ManorCropNet>();
            w.WriteInt32(crops.Length);
            foreach (var c in crops)
            {
                w.WriteInt32(c.CropId);
                w.WriteInt32(c.SeedId);
                w.WriteInt32(c.Level);
                w.WriteInt32(c.Reward1);
                w.WriteInt32(c.Reward2);
                w.WriteInt64(c.Limit);
                w.WriteInt64(c.ReferencePrice);
                w.WriteInt64(c.MinPrice);
                w.WriteInt64(c.MaxPrice);
                w.WriteInt32(c.ManorId);
                w.WriteInt64(c.Amount);
                w.WriteInt64(c.StartAmount);
                w.WriteInt64(c.Price);
                w.WriteInt32(c.RewardType);
                w.WriteInt32(c.InventoryObjectId);
                w.WriteInt64(c.InventoryCount);
                w.WriteInt64(c.CurrentStartAmount);
                w.WriteInt64(c.CurrentPrice);
                w.WriteInt32(c.CurrentRewardType);
            }
        });

        public static ManorInfoNetMsg DecodeManorInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var m = new ManorInfoNetMsg
            {
                View = r.ReadInt32(),
                CastleId = r.ReadInt32(),
                Next = r.ReadByte() != 0,
                Mode = r.ReadInt32(),
                NextChangeUnixMs = r.ReadInt64(),
                Treasury = r.ReadInt64(),
                ManorCost = r.ReadInt64(),
                Adena = r.ReadInt64()
            };
            int ns = ReadCount(ref r, 4096);
            var seeds = new ManorSeedNet[ns];
            for (int i = 0; i < ns; i++)
            {
                seeds[i] = new ManorSeedNet
                {
                    SeedId = r.ReadInt32(),
                    CropId = r.ReadInt32(),
                    Level = r.ReadInt32(),
                    Reward1 = r.ReadInt32(),
                    Reward2 = r.ReadInt32(),
                    Alternative = r.ReadByte() != 0,
                    Limit = r.ReadInt64(),
                    ReferencePrice = r.ReadInt64(),
                    MinPrice = r.ReadInt64(),
                    MaxPrice = r.ReadInt64(),
                    Amount = r.ReadInt64(),
                    StartAmount = r.ReadInt64(),
                    Price = r.ReadInt64(),
                    CurrentStartAmount = r.ReadInt64(),
                    CurrentPrice = r.ReadInt64()
                };
            }

            int nc = ReadCount(ref r, 4096);
            var crops = new ManorCropNet[nc];
            for (int i = 0; i < nc; i++)
            {
                crops[i] = new ManorCropNet
                {
                    CropId = r.ReadInt32(),
                    SeedId = r.ReadInt32(),
                    Level = r.ReadInt32(),
                    Reward1 = r.ReadInt32(),
                    Reward2 = r.ReadInt32(),
                    Limit = r.ReadInt64(),
                    ReferencePrice = r.ReadInt64(),
                    MinPrice = r.ReadInt64(),
                    MaxPrice = r.ReadInt64(),
                    ManorId = r.ReadInt32(),
                    Amount = r.ReadInt64(),
                    StartAmount = r.ReadInt64(),
                    Price = r.ReadInt64(),
                    RewardType = r.ReadInt32(),
                    InventoryObjectId = r.ReadInt32(),
                    InventoryCount = r.ReadInt64(),
                    CurrentStartAmount = r.ReadInt64(),
                    CurrentPrice = r.ReadInt64(),
                    CurrentRewardType = r.ReadInt32()
                };
            }

            m.Seeds = seeds;
            m.Crops = crops;
            return m;
        }
    }
}
