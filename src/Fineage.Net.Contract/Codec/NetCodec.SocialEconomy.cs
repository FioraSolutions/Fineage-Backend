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

        public static byte[] EncodeFriendInvite(FriendInviteNetMsg m) =>
            Write(w => w.WriteString(m.Name ?? ""));
        public static FriendInviteNetMsg DecodeFriendInvite(ReadOnlySpan<byte> p) =>
            new() { Name = new Reader(p).ReadString() };

        public static byte[] EncodeFriendAnswer(FriendAnswerNetMsg m) =>
            Write(w => w.WriteInt32(m.Response));
        public static FriendAnswerNetMsg DecodeFriendAnswer(ReadOnlySpan<byte> p) =>
            new() { Response = new Reader(p).ReadInt32() };

        public static byte[] EncodeFriendDelete(FriendDeleteNetMsg m) =>
            Write(w => w.WriteString(m.Name ?? ""));
        public static FriendDeleteNetMsg DecodeFriendDelete(ReadOnlySpan<byte> p) =>
            new() { Name = new Reader(p).ReadString() };

        public static byte[] EncodeBlockRequest(BlockRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Type);
            w.WriteString(m.Name ?? "");
        });
        public static BlockRequestNetMsg DecodeBlockRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new BlockRequestNetMsg { Type = r.ReadInt32(), Name = r.ReadString() };
        }

        public static byte[] EncodeFriendInviteAsk(FriendInviteAskNetMsg m) =>
            Write(w => w.WriteString(m.RequestorName ?? ""));
        public static FriendInviteAskNetMsg DecodeFriendInviteAsk(ReadOnlySpan<byte> p) =>
            new() { RequestorName = new Reader(p).ReadString() };

        public static byte[] EncodeFriendList(FriendListNetMsg m) => Write(w =>
        {
            var rows = m.Friends ?? System.Array.Empty<FriendRowNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new FriendRowNet();
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.Online);
            }
        });
        public static FriendListNetMsg DecodeFriendList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new FriendRowNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new FriendRowNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    Online = r.ReadInt32()
                };
            }
            return new FriendListNetMsg { Friends = rows };
        }

        public static byte[] EncodeFriendUpdate(FriendUpdateNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Added);
            w.WriteInt32(m.ObjectId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.Online);
        });
        public static FriendUpdateNetMsg DecodeFriendUpdate(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new FriendUpdateNetMsg
            {
                Added = r.ReadInt32(),
                ObjectId = r.ReadInt32(),
                Name = r.ReadString(),
                Online = r.ReadInt32()
            };
        }

        public static byte[] EncodeBlockList(BlockListNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.BlockAll);
            var rows = m.Blocked ?? System.Array.Empty<FriendRowNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new FriendRowNet();
                w.WriteInt32(row.ObjectId);
                w.WriteString(row.Name ?? "");
                w.WriteInt32(row.Online);
            }
        });
        public static BlockListNetMsg DecodeBlockList(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int blockAll = r.ReadInt32();
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new FriendRowNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new FriendRowNet
                {
                    ObjectId = r.ReadInt32(),
                    Name = r.ReadString(),
                    Online = r.ReadInt32()
                };
            }
            return new BlockListNetMsg { BlockAll = blockAll, Blocked = rows };
        }

        public static byte[] EncodeBossStatusRequest(BossStatusRequestNetMsg m) => Write(w => w.WriteInt32(m.NpcId));

        public static BossStatusRequestNetMsg DecodeBossStatusRequest(ReadOnlySpan<byte> p) =>
            new() { NpcId = new Reader(p).ReadInt32() };

        public static byte[] EncodeAdminBossSchedule(AdminBossScheduleNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.NpcId);
            w.WriteInt32(m.Action);
        });

        public static AdminBossScheduleNetMsg DecodeAdminBossSchedule(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AdminBossScheduleNetMsg { NpcId = r.ReadInt32(), Action = r.ReadInt32() };
        }

        public static byte[] EncodeBossStatus(BossStatusNetMsg m) => Write(w =>
        {
            var rows = m.Bosses ?? System.Array.Empty<BossStatusEntryNet>();
            w.WriteInt32(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                var row = rows[i] ?? new BossStatusEntryNet();
                w.WriteInt32(row.NpcId);
                w.WriteInt32(row.ObjectId);
                w.WriteInt32(row.Kind);
                w.WriteInt32(row.Status);
                w.WriteInt64(row.RespawnUnixMs);
                w.WriteInt32(row.CurrentHp);
                w.WriteInt32(row.MaxHp);
                w.WriteInt32(row.CurrentMp);
                w.WriteInt32(row.MaxMp);
                w.WriteByte(row.Enabled ? (byte)1 : (byte)0);
                w.WriteByte(row.Spawned ? (byte)1 : (byte)0);
            }
        });

        public static BossStatusNetMsg DecodeBossStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var rows = new BossStatusEntryNet[n];
            for (int i = 0; i < n; i++)
            {
                rows[i] = new BossStatusEntryNet
                {
                    NpcId = r.ReadInt32(),
                    ObjectId = r.ReadInt32(),
                    Kind = r.ReadInt32(),
                    Status = r.ReadInt32(),
                    RespawnUnixMs = r.ReadInt64(),
                    CurrentHp = r.ReadInt32(),
                    MaxHp = r.ReadInt32(),
                    CurrentMp = r.ReadInt32(),
                    MaxMp = r.ReadInt32(),
                    Enabled = r.ReadByte() != 0,
                    Spawned = r.ReadByte() != 0
                };
            }
            return new BossStatusNetMsg { Bosses = rows };
        }

        public static byte[] EncodeSiegeListRequest(SiegeListRequestNetMsg m) => Write(w => w.WriteInt32(m.CastleId));

        public static SiegeListRequestNetMsg DecodeSiegeListRequest(ReadOnlySpan<byte> p) =>
            new() { CastleId = new Reader(p).ReadInt32() };

        public static byte[] EncodeSiegeRegisterRequest(SiegeRegisterRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.Attacker);
            w.WriteInt32(m.Joining);
        });

        public static SiegeRegisterRequestNetMsg DecodeSiegeRegisterRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SiegeRegisterRequestNetMsg
            {
                CastleId = r.ReadInt32(),
                Attacker = r.ReadInt32(),
                Joining = r.ReadInt32()
            };
        }

        public static byte[] EncodeSiegeDefenderAnswerRequest(SiegeDefenderAnswerRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.ClanId);
            w.WriteInt32(m.Approved);
        });

        public static SiegeDefenderAnswerRequestNetMsg DecodeSiegeDefenderAnswerRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SiegeDefenderAnswerRequestNetMsg
            {
                CastleId = r.ReadInt32(),
                ClanId = r.ReadInt32(),
                Approved = r.ReadInt32()
            };
        }

        public static byte[] EncodeAdminCaptureCastle(AdminCaptureCastleNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteInt32(m.ClanId);
        });

        public static AdminCaptureCastleNetMsg DecodeAdminCaptureCastle(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AdminCaptureCastleNetMsg { CastleId = r.ReadInt32(), ClanId = r.ReadInt32() };
        }

        public static byte[] EncodeSiegeInfo(SiegeInfoNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.CastleId);
            w.WriteString(m.Name ?? "");
            w.WriteInt32(m.OwnerClanId);
            w.WriteInt32(m.TaxPercent);
            w.WriteInt64(m.SiegeUnixMs);
            w.WriteInt64(m.RegistrationDeadlineUnixMs);
            w.WriteByte(m.RegistrationClosed ? (byte)1 : (byte)0);
            w.WriteByte(m.InProgress ? (byte)1 : (byte)0);
            w.WriteInt64(m.EndsUnixMs);
            var clans = m.Clans ?? System.Array.Empty<SiegeClanNet>();
            w.WriteInt32(clans.Length);
            for (int i = 0; i < clans.Length; i++)
            {
                var row = clans[i] ?? new SiegeClanNet();
                w.WriteInt32(row.ClanId);
                w.WriteInt32(row.Side);
            }
        });

        public static SiegeInfoNetMsg DecodeSiegeInfo(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            var info = new SiegeInfoNetMsg
            {
                CastleId = r.ReadInt32(),
                Name = r.ReadString(),
                OwnerClanId = r.ReadInt32(),
                TaxPercent = r.ReadInt32(),
                SiegeUnixMs = r.ReadInt64(),
                RegistrationDeadlineUnixMs = r.ReadInt64(),
                RegistrationClosed = r.ReadByte() != 0,
                InProgress = r.ReadByte() != 0,
                EndsUnixMs = r.ReadInt64()
            };
            int n = r.ReadInt32();
            if (n < 0) n = 0;
            var clans = new SiegeClanNet[n];
            for (int i = 0; i < n; i++)
                clans[i] = new SiegeClanNet { ClanId = r.ReadInt32(), Side = r.ReadInt32() };
            info.Clans = clans;
            return info;
        }

        public static byte[] EncodeOlympiadRegisterRequest(OlympiadRegisterRequestNetMsg m) =>
            Write(w => w.WriteInt32(m.Classed));

        public static OlympiadRegisterRequestNetMsg DecodeOlympiadRegisterRequest(ReadOnlySpan<byte> p) =>
            new() { Classed = new Reader(p).ReadInt32() };

        public static byte[] EncodeAdminReportOlympiadResult(AdminReportOlympiadResultNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.WinnerObjectId);
        });

        public static AdminReportOlympiadResultNetMsg DecodeAdminReportOlympiadResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new AdminReportOlympiadResultNetMsg { MatchId = r.ReadInt32(), WinnerObjectId = r.ReadInt32() };
        }

        public static byte[] EncodeOlympiadStatus(OlympiadStatusNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Period);
            w.WriteInt32(m.Cycle);
            w.WriteByte(m.InCompetition ? (byte)1 : (byte)0);
            w.WriteInt64(m.CompetitionStartUnixMs);
            w.WriteInt64(m.CompetitionEndUnixMs);
            w.WriteInt64(m.OlympiadEndUnixMs);
            w.WriteInt64(m.ValidationEndUnixMs);
            w.WriteInt64(m.NextWeeklyUnixMs);
            w.WriteByte(m.RegistrationOpen ? (byte)1 : (byte)0);
            w.WriteByte(m.Noble ? (byte)1 : (byte)0);
            w.WriteInt32(m.Points);
            w.WriteInt32(m.CompetitionsDone);
            w.WriteInt32(m.Won);
            w.WriteInt32(m.Lost);
            w.WriteInt32(m.Drawn);
            w.WriteInt32(m.Queue);
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.StadiumId);
            w.WriteInt32(m.ClassId);
        });

        public static OlympiadStatusNetMsg DecodeOlympiadStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadStatusNetMsg
            {
                Period = r.ReadInt32(),
                Cycle = r.ReadInt32(),
                InCompetition = r.ReadByte() != 0,
                CompetitionStartUnixMs = r.ReadInt64(),
                CompetitionEndUnixMs = r.ReadInt64(),
                OlympiadEndUnixMs = r.ReadInt64(),
                ValidationEndUnixMs = r.ReadInt64(),
                NextWeeklyUnixMs = r.ReadInt64(),
                RegistrationOpen = r.ReadByte() != 0,
                Noble = r.ReadByte() != 0,
                Points = r.ReadInt32(),
                CompetitionsDone = r.ReadInt32(),
                Won = r.ReadInt32(),
                Lost = r.ReadInt32(),
                Drawn = r.ReadInt32(),
                Queue = r.ReadInt32(),
                MatchId = r.ReadInt32(),
                StadiumId = r.ReadInt32(),
                ClassId = r.ReadInt32()
            };
        }

        public static byte[] EncodeOlympiadMatch(OlympiadMatchNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.StadiumId);
            w.WriteInt32(m.ZoneId);
            w.WriteByte(m.Classed ? (byte)1 : (byte)0);
            w.WriteInt32(m.Phase);
            w.WriteInt32(m.PlayerOneId);
            w.WriteInt32(m.PlayerTwoId);
            w.WriteString(m.PlayerOneName ?? "");
            w.WriteString(m.PlayerTwoName ?? "");
            w.WriteInt32(m.InstanceId);
        });

        public static OlympiadMatchNetMsg DecodeOlympiadMatch(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadMatchNetMsg
            {
                MatchId = r.ReadInt32(),
                StadiumId = r.ReadInt32(),
                ZoneId = r.ReadInt32(),
                Classed = r.ReadByte() != 0,
                Phase = r.ReadInt32(),
                PlayerOneId = r.ReadInt32(),
                PlayerTwoId = r.ReadInt32(),
                PlayerOneName = r.ReadString(),
                PlayerTwoName = r.ReadString(),
                InstanceId = r.ReadInt32()
            };
        }

        public static byte[] EncodeOlympiadCountdown(OlympiadCountdownNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.Kind);
            w.WriteInt32(m.Seconds);
        });

        public static OlympiadCountdownNetMsg DecodeOlympiadCountdown(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadCountdownNetMsg
            {
                MatchId = r.ReadInt32(),
                Kind = r.ReadInt32(),
                Seconds = r.ReadInt32()
            };
        }

        public static byte[] EncodeOlympiadResult(OlympiadResultNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.MatchId);
            w.WriteInt32(m.Outcome);
            w.WriteInt32(m.WinnerObjectId);
            w.WriteInt32(m.PointsOne);
            w.WriteInt32(m.PointsTwo);
            w.WriteInt32(m.DeltaOne);
            w.WriteInt32(m.DeltaTwo);
            w.WriteInt32(m.RewardItemId);
            w.WriteInt32(m.RewardCount);
            w.WriteInt32(m.CompetitionsDoneOne);
            w.WriteInt32(m.CompetitionsDoneTwo);
        });

        public static OlympiadResultNetMsg DecodeOlympiadResult(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new OlympiadResultNetMsg
            {
                MatchId = r.ReadInt32(),
                Outcome = r.ReadInt32(),
                WinnerObjectId = r.ReadInt32(),
                PointsOne = r.ReadInt32(),
                PointsTwo = r.ReadInt32(),
                DeltaOne = r.ReadInt32(),
                DeltaTwo = r.ReadInt32(),
                RewardItemId = r.ReadInt32(),
                RewardCount = r.ReadInt32(),
                CompetitionsDoneOne = r.ReadInt32(),
                CompetitionsDoneTwo = r.ReadInt32()
            };
        }

        public static byte[] EncodeDungeonEnterRequest(DungeonEnterRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.DungeonId);
            w.WriteInt32(m.Action);
        });

        public static DungeonEnterRequestNetMsg DecodeDungeonEnterRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DungeonEnterRequestNetMsg { DungeonId = r.ReadInt32(), Action = r.ReadInt32() };
        }

        public static byte[] EncodeDungeonEntered(DungeonEnteredNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.DungeonId);
            w.WriteInt32(m.X);
            w.WriteInt32(m.Y);
            w.WriteInt32(m.Z);
            w.WriteByte(m.Inside ? (byte)1 : (byte)0);
            w.WriteInt32(m.Action);
        });

        public static DungeonEnteredNetMsg DecodeDungeonEntered(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new DungeonEnteredNetMsg
            {
                DungeonId = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Inside = r.ReadByte() != 0,
                Action = r.ReadInt32()
            };
        }

        public static byte[] EncodeSevenSignsActionRequest(SevenSignsActionRequestNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Action);
            w.WriteInt32(m.Cabal);
            w.WriteInt32(m.Seal);
            w.WriteInt32(m.BlueStones);
            w.WriteInt32(m.GreenStones);
            w.WriteInt32(m.RedStones);
        });

        public static SevenSignsActionRequestNetMsg DecodeSevenSignsActionRequest(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SevenSignsActionRequestNetMsg
            {
                Action = r.ReadInt32(),
                Cabal = r.ReadInt32(),
                Seal = r.ReadInt32(),
                BlueStones = r.ReadInt32(),
                GreenStones = r.ReadInt32(),
                RedStones = r.ReadInt32()
            };
        }

        public static byte[] EncodeSevenSignsStatus(SevenSignsStatusNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.Period);
            w.WriteInt32(m.Cycle);
            w.WriteInt32(m.PlayerCabal);
            w.WriteInt32(m.PlayerSeal);
            w.WriteInt32(m.PlayerStoneCount);
            w.WriteInt32(m.PlayerAncientAdena);
            w.WriteInt32(m.PlayerContribution);
            w.WriteInt32(m.DawnScore);
            w.WriteInt32(m.DuskScore);
            w.WriteInt32(m.DawnFestivalScore);
            w.WriteInt32(m.DuskFestivalScore);
            w.WriteInt64(m.DawnStonePoints);
            w.WriteInt64(m.DuskStonePoints);
            w.WriteInt32(m.AvariceOwner);
            w.WriteInt32(m.GnosisOwner);
            w.WriteInt32(m.StrifeOwner);
            w.WriteInt32(m.DawnMembers);
            w.WriteInt32(m.DuskMembers);
            w.WriteInt32(m.WinningCabal);
            w.WriteInt64(m.NextChangeUnixMs);
            w.WriteInt32(m.SkyState);
            w.WriteInt32(m.DawnAvarice);
            w.WriteInt32(m.DawnGnosis);
            w.WriteInt32(m.DawnStrife);
            w.WriteInt32(m.DuskAvarice);
            w.WriteInt32(m.DuskGnosis);
            w.WriteInt32(m.DuskStrife);
            w.WriteInt32(m.PreviousWinner);
        });

        public static SevenSignsStatusNetMsg DecodeSevenSignsStatus(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SevenSignsStatusNetMsg
            {
                Period = r.ReadInt32(),
                Cycle = r.ReadInt32(),
                PlayerCabal = r.ReadInt32(),
                PlayerSeal = r.ReadInt32(),
                PlayerStoneCount = r.ReadInt32(),
                PlayerAncientAdena = r.ReadInt32(),
                PlayerContribution = r.ReadInt32(),
                DawnScore = r.ReadInt32(),
                DuskScore = r.ReadInt32(),
                DawnFestivalScore = r.ReadInt32(),
                DuskFestivalScore = r.ReadInt32(),
                DawnStonePoints = r.ReadInt64(),
                DuskStonePoints = r.ReadInt64(),
                AvariceOwner = r.ReadInt32(),
                GnosisOwner = r.ReadInt32(),
                StrifeOwner = r.ReadInt32(),
                DawnMembers = r.ReadInt32(),
                DuskMembers = r.ReadInt32(),
                WinningCabal = r.ReadInt32(),
                NextChangeUnixMs = r.ReadInt64(),
                SkyState = r.ReadInt32(),
                DawnAvarice = r.ReadInt32(),
                DawnGnosis = r.ReadInt32(),
                DawnStrife = r.ReadInt32(),
                DuskAvarice = r.ReadInt32(),
                DuskGnosis = r.ReadInt32(),
                DuskStrife = r.ReadInt32(),
                PreviousWinner = r.ReadInt32()
            };
        }

        public static byte[] EncodeSevenSignsSky(SevenSignsSkyNetMsg m) => Write(w =>
        {
            w.WriteInt32(m.State);
            w.WriteInt32(m.Period);
            w.WriteInt32(m.WinningCabal);
        });

        public static SevenSignsSkyNetMsg DecodeSevenSignsSky(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new SevenSignsSkyNetMsg
            {
                State = r.ReadInt32(),
                Period = r.ReadInt32(),
                WinningCabal = r.ReadInt32()
            };
        }

        public static PositionCorrectionMsg DecodePositionCorrection(ReadOnlySpan<byte> p)
        {
            var r = new Reader(p);
            return new PositionCorrectionMsg
            {
                EntityValue = r.ReadUInt64(),
                EntityGeneration = r.ReadUInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                Heading = r.ReadInt32()
            };
        }

        static void WriteSummary(Writer w, CharacterSummaryNet c)
        {
            w.WriteInt32(c.CharacterId);
            w.WriteInt32(c.Slot);
            w.WriteString(c.Name);
            w.WriteInt32(c.ClassId);
            w.WriteInt32(c.Race);
            w.WriteInt32(c.Sex);
            w.WriteInt32(c.Level);
            w.WriteInt32(c.Hp);
            w.WriteInt32(c.MaxHp);
            w.WriteInt32(c.Mp);
            w.WriteInt32(c.MaxMp);
            w.WriteInt32(c.HairStyle);
            w.WriteInt32(c.HairColor);
            w.WriteInt32(c.Face);
            w.WriteByte(c.PendingDelete ? (byte)1 : (byte)0);
            w.WriteInt32(c.X);
            w.WriteInt32(c.Y);
            w.WriteInt32(c.Z);
            w.WriteInt32(c.DeleteSecondsLeft);
            w.WriteByte(c.IsLastPlayed ? (byte)1 : (byte)0);
            w.WriteInt32(c.CurCp);
            w.WriteInt32(c.MaxCp);
            w.WriteInt64(c.Exp);
            w.WriteInt32(c.Sp);
            w.WriteInt32(c.Karma);
            w.WriteInt32(c.WeaponItemId);
            w.WriteInt32Array(c.Paperdoll);
        }

        static CharacterSummaryNet ReadSummary(ref Reader r)
        {
            var row = new CharacterSummaryNet
            {
                CharacterId = r.ReadInt32(),
                Slot = r.ReadInt32(),
                Name = r.ReadString(),
                ClassId = r.ReadInt32(),
                Race = r.ReadInt32(),
                Sex = r.ReadInt32(),
                Level = r.ReadInt32(),
                Hp = r.ReadInt32(),
                MaxHp = r.ReadInt32(),
                Mp = r.ReadInt32(),
                MaxMp = r.ReadInt32(),
                HairStyle = r.ReadInt32(),
                HairColor = r.ReadInt32(),
                Face = r.ReadInt32(),
                PendingDelete = r.ReadByte() != 0,
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Z = r.ReadInt32(),
                DeleteSecondsLeft = r.ReadInt32(),
                IsLastPlayed = r.ReadByte() != 0,
                CurCp = r.ReadInt32(),
                MaxCp = r.ReadInt32(),
                Exp = r.ReadInt64(),
                Sp = r.ReadInt32(),
                Karma = r.ReadInt32(),
                WeaponItemId = r.ReadInt32(),
                Paperdoll = r.ReadInt32Array()
            };
            if (row.DeleteSecondsLeft > 0)
                row.PendingDelete = true;
            return row;
        }

        static byte[] Write(Action<Writer> write)
        {
            var w = new Writer();
            write(w);
            return w.ToArray();
        }
    }
}
