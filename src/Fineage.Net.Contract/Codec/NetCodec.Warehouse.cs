using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {

        public static byte[] EncodeWarehouseDepositList(WarehouseDepositListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.FeePerLine);
            w.WriteInt32(m.SlotsUsed);
            w.WriteInt32(m.SlotsMax);
            w.WriteInt32(m.Adena);
            WriteItemListEntries(w, m.Items);
        });

        public static WarehouseDepositListNetMsg DecodeWarehouseDepositList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new WarehouseDepositListNetMsg
            {
                NpcObjectId = r.ReadInt32(),
                FeePerLine = r.ReadInt32(),
                SlotsUsed = r.ReadInt32(),
                SlotsMax = r.ReadInt32(),
                Adena = r.ReadInt32(),
                Items = ReadItemListEntries(ref r)
            };
        }

        public static byte[] EncodeWarehouseWithdrawList(WarehouseWithdrawListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.SlotsUsed);
            w.WriteInt32(m.SlotsMax);
            w.WriteInt32(m.InventorySlotsUsed);
            w.WriteInt32(m.InventorySlotsMax);
            w.WriteInt32(m.CurrentWeight);
            w.WriteInt32(m.MaxWeight);
            WriteItemListEntries(w, m.Items);
        });

        public static WarehouseWithdrawListNetMsg DecodeWarehouseWithdrawList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new WarehouseWithdrawListNetMsg
            {
                NpcObjectId = r.ReadInt32(),
                SlotsUsed = r.ReadInt32(),
                SlotsMax = r.ReadInt32(),
                InventorySlotsUsed = r.ReadInt32(),
                InventorySlotsMax = r.ReadInt32(),
                CurrentWeight = r.ReadInt32(),
                MaxWeight = r.ReadInt32(),
                Items = ReadItemListEntries(ref r)
            };
        }

        public static byte[] EncodeWarehouseDepositRequest(WarehouseDepositRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            var lines = m.Lines ?? Array.Empty<ShopObjectCountNet>();
            w.WriteInt32(lines.Length);
            for (int i = 0; i < lines.Length; i++)
            {
                var row = lines[i] ?? new ShopObjectCountNet();
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.Count);
            }
        });

        public static WarehouseDepositRequestNetMsg DecodeWarehouseDepositRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var lines = new ShopObjectCountNet[n];
            for (int i = 0; i < n; i++)
                lines[i] = new ShopObjectCountNet { ObjectId = r.ReadInt32(), Count = r.ReadInt32() };
            return new WarehouseDepositRequestNetMsg { NpcObjectId = npc, Lines = lines };
        }

        public static byte[] EncodeWarehouseWithdrawRequest(WarehouseWithdrawRequestNetMsg m) =>
            EncodeWarehouseDepositRequest(new WarehouseDepositRequestNetMsg { NpcObjectId = m.NpcObjectId, Lines = m.Lines });

        public static WarehouseWithdrawRequestNetMsg DecodeWarehouseWithdrawRequest(ReadOnlySpan<byte> p)
        {
            var dep = DecodeWarehouseDepositRequest(p);
            return new WarehouseWithdrawRequestNetMsg { NpcObjectId = dep.NpcObjectId, Lines = dep.Lines };
        }
    }
}
