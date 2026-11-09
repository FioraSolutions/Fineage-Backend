using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeRecipeBookOpen(RecipeBookOpenNetMsg m) => Write(w => w.WriteInt32(m.Dwarven));
        public static RecipeBookOpenNetMsg DecodeRecipeBookOpen(ReadOnlySpan<byte> p) =>
            new() { Dwarven = new Reader(p).ReadInt32() };

        public static byte[] EncodeRecipeMakeSelf(RecipeMakeSelfNetMsg m) => Write(w => w.WriteInt32(m.RecipeId));
        public static RecipeMakeSelfNetMsg DecodeRecipeMakeSelf(ReadOnlySpan<byte> p) =>
            new() { RecipeId = new Reader(p).ReadInt32() };

        public static byte[] EncodeRecipeBookList(RecipeBookListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Dwarven);
            var ids = m.RecipeIds ?? System.Array.Empty<int>();
            w.WriteInt32(ids.Length);
            for (int i = 0; i < ids.Length; i++)
                w.WriteInt32(ids[i]);
        });
        public static RecipeBookListNetMsg DecodeRecipeBookList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int dwarven = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var ids = new int[n];
            for (int i = 0; i < n; i++)
                ids[i] = r.ReadInt32();
            return new RecipeBookListNetMsg { Dwarven = dwarven, RecipeIds = ids };
        }

        public static byte[] EncodeRecipeItemMakeInfo(RecipeItemMakeInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RecipeId);
            w.WriteInt32(m.Result);
            w.WriteInt32(m.CurrentMp);
            w.WriteInt32(m.MaxMp);
        });
        public static RecipeItemMakeInfoNetMsg DecodeRecipeItemMakeInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RecipeItemMakeInfoNetMsg
            {
                RecipeId = r.ReadInt32(),
                Result = r.ReadInt32(),
                CurrentMp = r.ReadInt32(),
                MaxMp = r.ReadInt32()
            };
        }

        public static byte[] EncodeSetRecipeShop(SetRecipeShopNetMsg m) => Write(w =>
        {
            w.WriteString(m.Message ?? "");
            var items = m.Items ?? System.Array.Empty<RecipeShopRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new RecipeShopRowNet();
                w.WriteInt32(row.RecipeId);
                w.WriteInt64(row.Price);
            }
        });
        public static SetRecipeShopNetMsg DecodeSetRecipeShop(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            string message = r.ReadString();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new RecipeShopRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new RecipeShopRowNet
                {
                    RecipeId = r.ReadInt32(),
                    Price = r.ReadInt64()
                };
            }
            return new SetRecipeShopNetMsg { Message = message, Items = items };
        }

        public static byte[] EncodeRecipeShopMakeInfo(RecipeShopMakeInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ManufacturerObjectId);
            w.WriteInt32(m.RecipeId);
        });
        public static RecipeShopMakeInfoNetMsg DecodeRecipeShopMakeInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RecipeShopMakeInfoNetMsg
            {
                ManufacturerObjectId = r.ReadInt32(),
                RecipeId = r.ReadInt32()
            };
        }

        public static byte[] EncodeRecipeShopMakeItem(RecipeShopMakeItemNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ManufacturerObjectId);
            w.WriteInt32(m.RecipeId);
        });
        public static RecipeShopMakeItemNetMsg DecodeRecipeShopMakeItem(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RecipeShopMakeItemNetMsg
            {
                ManufacturerObjectId = r.ReadInt32(),
                RecipeId = r.ReadInt32()
            };
        }

        public static byte[] EncodeRecipeShopSellList(RecipeShopSellListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ManufacturerObjectId);
            w.WriteInt32(m.CurrentMp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt64(m.BuyerAdena);
            var items = m.Items ?? System.Array.Empty<RecipeShopRowNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new RecipeShopRowNet();
                w.WriteInt32(row.RecipeId);
                w.WriteInt64(row.Price);
            }
        });
        public static RecipeShopSellListNetMsg DecodeRecipeShopSellList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int manufacturer = r.ReadInt32();
            int curMp = r.ReadInt32();
            int maxMp = r.ReadInt32();
            long adena = r.ReadInt64();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new RecipeShopRowNet[n];
            for (int i = 0; i < n; i++)
            {
                items[i] = new RecipeShopRowNet
                {
                    RecipeId = r.ReadInt32(),
                    Price = r.ReadInt64()
                };
            }
            return new RecipeShopSellListNetMsg
            {
                ManufacturerObjectId = manufacturer,
                CurrentMp = curMp,
                MaxMp = maxMp,
                BuyerAdena = adena,
                Items = items
            };
        }

        public static byte[] EncodeRecipeShopItemInfo(RecipeShopItemInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.ManufacturerObjectId);
            w.WriteInt32(m.RecipeId);
            w.WriteInt32(m.CurrentMp);
            w.WriteInt32(m.MaxMp);
            w.WriteInt32(m.Result);
        });
        public static RecipeShopItemInfoNetMsg DecodeRecipeShopItemInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RecipeShopItemInfoNetMsg
            {
                ManufacturerObjectId = r.ReadInt32(),
                RecipeId = r.ReadInt32(),
                CurrentMp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                Result = r.ReadInt32()
            };
        }
    }
}
