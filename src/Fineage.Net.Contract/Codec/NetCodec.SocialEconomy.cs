using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeTradeAsk(TradeAskNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteString(m.RequestorName ?? "");
        });
        public static TradeAskNetMsg DecodeTradeAsk(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeAskNetMsg { RequestorObjectId = r.ReadInt32(), RequestorName = r.ReadString() };
        }

        public static byte[] EncodeTradeRequestResult(TradeRequestResultNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static TradeRequestResultNetMsg DecodeTradeRequestResult(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodeTradeStart(TradeStartNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.PartnerObjectId);
            w.WriteString(m.PartnerName ?? "");
        });
        public static TradeStartNetMsg DecodeTradeStart(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeStartNetMsg { PartnerObjectId = r.ReadInt32(), PartnerName = r.ReadString() };
        }

        public static byte[] EncodeTradeOfferUpdate(TradeOfferUpdateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.IsOwn);
            w.WriteInt32(m.InventoryObjectId);
            w.WriteInt32(m.ItemId);
            w.WriteInt64(m.Count);
            w.WriteInt32(m.EnchantLevel);
        });
        public static TradeOfferUpdateNetMsg DecodeTradeOfferUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeOfferUpdateNetMsg
            {
                IsOwn = r.ReadInt32(),
                InventoryObjectId = r.ReadInt32(),
                ItemId = r.ReadInt32(),
                Count = r.ReadInt64(),
                EnchantLevel = r.ReadInt32()
            };
        }

        public static byte[] EncodeTradeConfirmState(TradeConfirmStateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.SelfConfirmed);
            w.WriteInt32(m.PartnerConfirmed);
        });
        public static TradeConfirmStateNetMsg DecodeTradeConfirmState(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeConfirmStateNetMsg { SelfConfirmed = r.ReadInt32(), PartnerConfirmed = r.ReadInt32() };
        }

        public static byte[] EncodeTradeFinished(TradeFinishedNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static TradeFinishedNetMsg DecodeTradeFinished(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

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
        public static byte[] EncodeBuyList(BuyListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.ListId);
            w.WriteInt32(m.BaseTaxPercent);
            w.WriteInt32(m.CastleTaxPercent);
            var items = m.Items ?? Array.Empty<BuyListItemNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new BuyListItemNet();
                w.WriteInt32(row.ItemId);
                w.WriteInt32(row.Price);
                w.WriteInt32(row.Count);
            }
        });

        public static BuyListNetMsg DecodeBuyList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int listId = r.ReadInt32();
            int baseTax = r.ReadInt32();
            int castleTax = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new BuyListItemNet[n];
            for (int i = 0; i < n; i++)
                items[i] = new BuyListItemNet { ItemId = r.ReadInt32(), Price = r.ReadInt32(), Count = r.ReadInt32() };
            return new BuyListNetMsg { NpcObjectId = npc, ListId = listId, BaseTaxPercent = baseTax, CastleTaxPercent = castleTax, Items = items };
        }

        public static byte[] EncodeSellList(SellListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            var items = m.Items ?? Array.Empty<SellListItemNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new SellListItemNet();
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt32(row.Count);
                w.WriteInt32(row.Price);
                w.WriteInt32(row.EnchantLevel);
            }
        });

        public static SellListNetMsg DecodeSellList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new SellListItemNet[n];
            for (int i = 0; i < n; i++)
                items[i] = new SellListItemNet
                {
                    ObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt32(),
                    Price = r.ReadInt32(),
                    EnchantLevel = r.ReadInt32()
                };
            return new SellListNetMsg { NpcObjectId = npc, Items = items };
        }

        public static byte[] EncodeMultiSellList(MultiSellListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.ListId);
            w.WriteByte(m.MaintainEnchantment ? (byte)1 : (byte)0);
            w.WriteByte(m.InventoryOnly ? (byte)1 : (byte)0);
            w.WriteInt32(m.CastleTaxPercent);
            var entries = m.Entries ?? Array.Empty<MultiSellEntryNet>();
            w.WriteInt32(entries.Length);
            for (int i = 0; i < entries.Length; i++)
            {
                var e = entries[i] ?? new MultiSellEntryNet();
                w.WriteInt32(e.EntryId);
                WriteParts(w, e.Ingredients);
                WriteParts(w, e.Products);
            }
        });

        static void WriteParts(Writer w, MultiSellPartNet[]? parts)
        {
            parts ??= Array.Empty<MultiSellPartNet>();
            w.WriteInt32(parts.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i] ?? new MultiSellPartNet();
                w.WriteInt32(p.ItemId);
                w.WriteInt32(p.Count);
                w.WriteInt32(p.EnchantLevel);
            }
        }

        static MultiSellPartNet[] ReadParts(ref Reader r)
        {
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var parts = new MultiSellPartNet[n];
            for (int i = 0; i < n; i++)
                parts[i] = new MultiSellPartNet { ItemId = r.ReadInt32(), Count = r.ReadInt32(), EnchantLevel = r.ReadInt32() };
            return parts;
        }

        public static MultiSellListNetMsg DecodeMultiSellList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int listId = r.ReadInt32();
            bool maintain = r.ReadByte() != 0;
            bool invOnly = r.ReadByte() != 0;
            int tax = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var entries = new MultiSellEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                entries[i] = new MultiSellEntryNet
                {
                    EntryId = r.ReadInt32(),
                    Ingredients = ReadParts(ref r),
                    Products = ReadParts(ref r)
                };
            }
            return new MultiSellListNetMsg
            {
                NpcObjectId = npc,
                ListId = listId,
                MaintainEnchantment = maintain,
                InventoryOnly = invOnly,
                CastleTaxPercent = tax,
                Entries = entries
            };
        }

        public static byte[] EncodeShopBalance(ShopBalanceNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Adena);
            w.WriteInt32(m.Weight);
            w.WriteInt32(m.MaxWeight);
            w.WriteInt32(m.Slots);
            w.WriteInt32(m.MaxSlots);
            w.WriteInt32(m.BaseTaxPercent);
            w.WriteInt32(m.CastleTaxPercent);
        });

        public static ShopBalanceNetMsg DecodeShopBalance(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new ShopBalanceNetMsg
            {
                Adena = r.ReadInt32(),
                Weight = r.ReadInt32(),
                MaxWeight = r.ReadInt32(),
                Slots = r.ReadInt32(),
                MaxSlots = r.ReadInt32(),
                BaseTaxPercent = r.ReadInt32(),
                CastleTaxPercent = r.ReadInt32()
            };
        }

        public static byte[] EncodeRequestBuyItem(RequestBuyItemNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.ListId);
            var items = m.Items ?? Array.Empty<ShopCountNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new ShopCountNet();
                w.WriteInt32(row.ItemId);
                w.WriteInt32(row.Count);
            }
        });

        public static RequestBuyItemNetMsg DecodeRequestBuyItem(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int listId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new ShopCountNet[n];
            for (int i = 0; i < n; i++)
                items[i] = new ShopCountNet { ItemId = r.ReadInt32(), Count = r.ReadInt32() };
            return new RequestBuyItemNetMsg { NpcObjectId = npc, ListId = listId, Items = items };
        }

        public static byte[] EncodeRequestSellItem(RequestSellItemNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            var items = m.Items ?? Array.Empty<ShopObjectCountNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new ShopObjectCountNet();
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.Count);
            }
        });

        public static RequestSellItemNetMsg DecodeRequestSellItem(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int npc = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var items = new ShopObjectCountNet[n];
            for (int i = 0; i < n; i++)
                items[i] = new ShopObjectCountNet { ObjectId = r.ReadInt32(), Count = r.ReadInt32() };
            return new RequestSellItemNetMsg { NpcObjectId = npc, Items = items };
        }

        public static byte[] EncodeRequestMultiSellChoose(RequestMultiSellChooseNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcObjectId);
            w.WriteInt32(m.ListId);
            w.WriteInt32(m.EntryId);
            w.WriteInt32(m.Amount);
        });

        public static RequestMultiSellChooseNetMsg DecodeRequestMultiSellChoose(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new RequestMultiSellChooseNetMsg
            {
                NpcObjectId = r.ReadInt32(),
                ListId = r.ReadInt32(),
                EntryId = r.ReadInt32(),
                Amount = r.ReadInt32()
            };
        }

        static void WriteItemListEntries(Writer w, ItemListEntryNet[]? items)
        {
            items ??= Array.Empty<ItemListEntryNet>();
            w.WriteInt32(items.Length);
            for (int i = 0; i < items.Length; i++)
            {
                var row = items[i] ?? new ItemListEntryNet();
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.ItemId);
                w.WriteInt32(row.Count);
                w.WriteInt32(row.EnchantLevel);
                w.WriteInt32(row.EquippedSlot);
                w.WriteString(row.BodySlot ?? "");
                w.WriteString(row.Kind ?? "");
                w.WriteString(row.Icon ?? "");
                w.WriteString(row.WeaponType ?? "");
                w.WriteString(row.ArmorType ?? "");
            }
        }

        static ItemListEntryNet[] ReadItemListEntries(ref Reader r)
        {
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new ItemListEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new ItemListEntryNet
                {
                    ObjectId = r.ReadInt32(),
                    ItemId = r.ReadInt32(),
                    Count = r.ReadInt32(),
                    EnchantLevel = r.ReadInt32(),
                    EquippedSlot = r.ReadInt32(),
                    BodySlot = r.ReadString(),
                    Kind = r.ReadString(),
                    Icon = r.ReadString(),
                    WeaponType = r.ReadString(),
                    ArmorType = r.ReadString()
                };
            }
            return rows;
        }

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

        public static byte[] EncodeRequestQuestList() => Array.Empty<byte>();
        public static void DecodeRequestQuestList(ReadOnlySpan<byte> _) { }

        public static byte[] EncodeRequestQuestAbort(RequestQuestAbortNetMsg m) => Write(w => w.WriteInt32(m.QuestId));
        public static RequestQuestAbortNetMsg DecodeRequestQuestAbort(ReadOnlySpan<byte> p) =>
            new() { QuestId = new Reader(p).ReadInt32() };

        public static byte[] EncodeQuestList(QuestListNetMsg m) => Write(w =>
        {
            var active = m.Active ?? Array.Empty<QuestJournalEntryNet>();
            w.WriteInt32(active.Length);
            for (int i = 0; i < active.Length; i++)
            {
                var row = active[i] ?? new QuestJournalEntryNet();
                w.WriteInt32(row.QuestId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.State);
                w.WriteInt32(row.Cond);
                w.WriteInt32(row.ProgressFlags);
                w.WriteInt32(row.JournalWord);
                w.WriteByte(row.Repeatable ? (byte)1 : (byte)0);
            }
            var mask = m.CompletedMask ?? Array.Empty<byte>();
            w.WriteInt32(mask.Length);
            for (int i = 0; i < mask.Length; i++)
                w.WriteByte(mask[i]);
            var done = m.CompletedQuestIds ?? Array.Empty<int>();
            w.WriteInt32(done.Length);
            for (int i = 0; i < done.Length; i++)
                w.WriteInt32(done[i]);
            w.WriteInt32(m.MaxActive);
        });

        public static QuestListNetMsg DecodeQuestList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var active = new QuestJournalEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                active[i] = new QuestJournalEntryNet
                {
                    QuestId = r.ReadInt32(),
                    Name = r.ReadString(),
                    State = r.ReadInt32(),
                    Cond = r.ReadInt32(),
                    ProgressFlags = r.ReadInt32(),
                    JournalWord = r.ReadInt32(),
                    Repeatable = r.ReadByte() != 0
                };
            }
            int maskLen = r.ReadInt32();
            if (maskLen < 0) maskLen = 0;
            var mask = new byte[maskLen];
            for (int i = 0; i < maskLen; i++)
                mask[i] = r.ReadByte();
            int doneLen = r.ReadInt32();
            if (doneLen < 0) doneLen = 0;
            var done = new int[doneLen];
            for (int i = 0; i < doneLen; i++)
                done[i] = r.ReadInt32();
            return new QuestListNetMsg
            {
                Active = active,
                CompletedMask = mask,
                CompletedQuestIds = done,
                MaxActive = r.ReadInt32()
            };
        }

        public static byte[] EncodePartyInviteRequest(PartyInviteRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.TargetObjectId);
            w.WriteInt32(m.ItemDistribution);
        });
        public static PartyInviteRequestNetMsg DecodePartyInviteRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyInviteRequestNetMsg { TargetObjectId = r.ReadInt32(), ItemDistribution = r.ReadInt32() };
        }

        public static byte[] EncodePartyAnswerJoinRequest(PartyAnswerJoinRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteInt32(m.Response);
        });
        public static PartyAnswerJoinRequestNetMsg DecodePartyAnswerJoinRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyAnswerJoinRequestNetMsg { RequestorObjectId = r.ReadInt32(), Response = r.ReadInt32() };
        }

        public static byte[] EncodePartyLeaveRequest() => Array.Empty<byte>();
        public static void DecodePartyLeaveRequest(ReadOnlySpan<byte> _) { }

        public static byte[] EncodePartyKickRequest(PartyKickRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static PartyKickRequestNetMsg DecodePartyKickRequest(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodePartyAskJoin(PartyAskJoinNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteString(m.RequestorName ?? "");
            w.WriteInt32(m.ItemDistribution);
        });
        public static PartyAskJoinNetMsg DecodePartyAskJoin(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PartyAskJoinNetMsg
            {
                RequestorObjectId = r.ReadInt32(),
                RequestorName = r.ReadString(),
                ItemDistribution = r.ReadInt32()
            };
        }

        public static byte[] EncodePartyJoinResult(PartyJoinResultNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static PartyJoinResultNetMsg DecodePartyJoinResult(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodePartyMemberList(PartyMemberListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.LeaderObjectId);
            w.WriteInt32(m.PartyId);
            var members = m.Members ?? Array.Empty<PartyMemberEntryNet>();
            w.WriteInt32(members.Length);
            for (int i = 0; i < members.Length; i++)
            {
                var row = members[i] ?? new PartyMemberEntryNet();
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.ClassId);
                w.WriteInt32(row.Level);
                w.WriteInt32(row.CurHp);
                w.WriteInt32(row.MaxHp);
                w.WriteInt32(row.CurMp);
                w.WriteInt32(row.MaxMp);
            }
        });
        public static PartyMemberListNetMsg DecodePartyMemberList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int leader = r.ReadInt32();
            int partyId = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var members = new PartyMemberEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                members[i] = new PartyMemberEntryNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    ClassId = r.ReadInt32(),
                    Level = r.ReadInt32(),
                    CurHp = r.ReadInt32(),
                    MaxHp = r.ReadInt32(),
                    CurMp = r.ReadInt32(),
                    MaxMp = r.ReadInt32()
                };
            }
            return new PartyMemberListNetMsg { LeaderObjectId = leader, PartyId = partyId, Members = members };
        }

        public static byte[] EncodeTradeRequest(TradeRequestNetMsg m) => Write(w => w.WriteInt32(m.TargetObjectId));
        public static TradeRequestNetMsg DecodeTradeRequest(ReadOnlySpan<byte> p) =>
            new() { TargetObjectId = new Reader(p).ReadInt32() };

        public static byte[] EncodeTradeAnswerRequest(TradeAnswerRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.RequestorObjectId);
            w.WriteInt32(m.Response);
        });
        public static TradeAnswerRequestNetMsg DecodeTradeAnswerRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeAnswerRequestNetMsg { RequestorObjectId = r.ReadInt32(), Response = r.ReadInt32() };
        }

        public static byte[] EncodeTradeAddItemRequest(TradeAddItemRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.InventoryObjectId);
            w.WriteInt64(m.Count);
        });
        public static TradeAddItemRequestNetMsg DecodeTradeAddItemRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new TradeAddItemRequestNetMsg { InventoryObjectId = r.ReadInt32(), Count = r.ReadInt64() };
        }

        public static byte[] EncodeTradeDoneRequest(TradeDoneRequestNetMsg m) => Write(w => w.WriteInt32(m.Response));
        public static TradeDoneRequestNetMsg DecodeTradeDoneRequest(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };
    }
}
