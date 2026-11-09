using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeSetPrivateStoreSell(SetPrivateStoreSellNetMsg m) => Write(w =>
        {
            w.WriteString(m.Message ?? "");
            w.WriteInt32(m.Packaged);
            var items = m.Items ?? System.Array.Empty<PrivateStoreSellRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreSellRowNet();
                w.WriteInt32(row.InventoryObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
                w.WriteInt32(row.EnchantLevel);
            }
        });
        public static SetPrivateStoreSellNetMsg DecodeSetPrivateStoreSell(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            string message = r.ReadString();
            int packaged = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreSellRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreSellRowNet
                {
                    InventoryObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64(),
                    EnchantLevel = r.ReadInt32()
                };
            }
            return new SetPrivateStoreSellNetMsg { Message = message, Packaged = packaged, Items = items };
        }

        public static byte[] EncodePrivateStoreListRequest(PrivateStoreListRequestNetMsg m) =>
            Write(w => w.WriteInt32(m.StoreObjectId));
        public static PrivateStoreListRequestNetMsg DecodePrivateStoreListRequest(ReadOnlySpan<byte> p) =>
            new() { StoreObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodePrivateStoreBuy(PrivateStoreBuyNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.StoreObjectId);
            var items = m.Items ?? System.Array.Empty<PrivateStoreSellRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreSellRowNet();
                w.WriteInt32(row.InventoryObjectId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
            }
        });
        public static PrivateStoreBuyNetMsg DecodePrivateStoreBuy(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int storeId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreSellRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreSellRowNet
                {
                    InventoryObjectId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64()
                };
            }
            return new PrivateStoreBuyNetMsg { StoreObjectId = storeId, Items = items };
        }

        public static byte[] EncodePrivateStoreInfo(PrivateStoreInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ObjectId);
            w.WriteInt32(m.StoreType);
            w.WriteString(m.Message ?? "");
        });
        public static PrivateStoreInfoNetMsg DecodePrivateStoreInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PrivateStoreInfoNetMsg
            {
                ObjectId = r.ReadInt32(),
                StoreType = r.ReadInt32(),
                Message = r.ReadString()
            };
        }

        public static byte[] EncodePrivateStoreListSell(PrivateStoreListSellNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.StoreObjectId);
            w.WriteString(m.StoreName ?? "");
            w.WriteString(m.Message ?? "");
            w.WriteInt32(m.Packaged);
            w.WriteInt64(m.BuyerAdena);
            var items = m.Items ?? System.Array.Empty<PrivateStoreSellRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreSellRowNet();
                w.WriteInt32(row.InventoryObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
                w.WriteInt32(row.EnchantLevel);
            }
        });
        public static PrivateStoreListSellNetMsg DecodePrivateStoreListSell(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int storeId = r.ReadInt32();
            string name = r.ReadString();
            string message = r.ReadString();
            int packaged = r.ReadInt32();
            long adena = r.ReadInt64();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreSellRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreSellRowNet
                {
                    InventoryObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64(),
                    EnchantLevel = r.ReadInt32()
                };
            }
            return new PrivateStoreListSellNetMsg
            {
                StoreObjectId = storeId,
                StoreName = name,
                Message = message,
                Packaged = packaged,
                BuyerAdena = adena,
                Items = items
            };
        }

        public static byte[] EncodeSetPrivateStoreBuy(SetPrivateStoreBuyNetMsg m) => Write(w =>
        {
            w.WriteString(m.Message ?? "");
            var items = m.Items ?? System.Array.Empty<PrivateStoreBuyRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreBuyRowNet();
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
            }
        });
        public static SetPrivateStoreBuyNetMsg DecodeSetPrivateStoreBuy(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            string message = r.ReadString();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreBuyRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreBuyRowNet
                {
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64()
                };
            }
            return new SetPrivateStoreBuyNetMsg { Message = message, Items = items };
        }

        public static byte[] EncodePrivateStoreSellTo(PrivateStoreSellToNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.StoreObjectId);
            var items = m.Items ?? System.Array.Empty<PrivateStoreSellRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreSellRowNet();
                w.WriteInt32(row.InventoryObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
            }
        });
        public static PrivateStoreSellToNetMsg DecodePrivateStoreSellTo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int storeId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreSellRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreSellRowNet
                {
                    InventoryObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64()
                };
            }
            return new PrivateStoreSellToNetMsg { StoreObjectId = storeId, Items = items };
        }

        public static byte[] EncodePrivateStoreListBuy(PrivateStoreListBuyNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.StoreObjectId);
            w.WriteString(m.StoreName ?? "");
            w.WriteString(m.Message ?? "");
            w.WriteInt64(m.StoreAdena);
            var items = m.Items ?? System.Array.Empty<PrivateStoreBuyRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new PrivateStoreBuyRowNet();
                w.WriteInt32(row.ItemId);
                w.WriteInt64(row.Count);
                w.WriteInt64(row.Price);
            }
        });
        public static PrivateStoreListBuyNetMsg DecodePrivateStoreListBuy(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int storeId = r.ReadInt32();
            string name = r.ReadString();
            string message = r.ReadString();
            long adena = r.ReadInt64();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new PrivateStoreBuyRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new PrivateStoreBuyRowNet
                {
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt64(),
                    Price = r.ReadInt64()
                };
            }
            return new PrivateStoreListBuyNetMsg
            {
                StoreObjectId = storeId,
                StoreName = name,
                Message = message,
                StoreAdena = adena,
                Items = items
            };
        }
    }
}
