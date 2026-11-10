using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Inventory;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void SendDepositList(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var items = new List<ItemListEntryMsg>();
        if (player.Inventory != null)
        {
            foreach (var item in player.Inventory.Items)
            {
                if (!item.IsEquipped && item.Template.CanDropTradeStore)
                    items.Add(InventoryMessages.Entry(item));
            }
        }

        var list = new WarehouseDepositListMsg
        {
            NpcObjectId = npc.ObjectId,
            FeePerLine = WarehouseRules.DepositFeePerLine,
            SlotsUsed = player.Warehouse?.SlotCount ?? 0,
            SlotsMax = WarehouseRules.WarehouseSlots(player.Race),
            Adena = player.Inventory?.CountOf(WarehouseRules.AdenaItemId) ?? 0,
            Items = items.ToArray()
        };
        if (NetBridge != null)
            NetBridge.FanoutWarehouseDepositList([session.ObjectId], list);
    }

    void SendWithdrawList(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var items = new List<ItemListEntryMsg>();
        if (player.Warehouse != null)
        {
            foreach (var stack in player.Warehouse.Items)
                items.Add(StackEntry(stack));
        }

        var list = new WarehouseWithdrawListMsg
        {
            NpcObjectId = npc.ObjectId,
            SlotsUsed = player.Warehouse?.SlotCount ?? 0,
            SlotsMax = WarehouseRules.WarehouseSlots(player.Race),
            InventorySlotsUsed = player.Inventory?.Items.Count ?? 0,
            InventorySlotsMax = WarehouseRules.InventorySlots(player.Race),
            CurrentWeight = player.Inventory == null ? 0 : _weights.Load(player.Inventory.Items.Select(item => (item.ItemId, item.Count))),
            MaxWeight = MaxLoadOf(player),
            Items = items.ToArray()
        };
        if (NetBridge != null)
            NetBridge.FanoutWarehouseWithdrawList([session.ObjectId], list);
    }

    ItemListEntryMsg StackEntry(WarehouseStack stack)
    {
        var template = _game.Items.Get(stack.ItemId);
        return new ItemListEntryMsg
        {
            ObjectId = stack.ObjectId,
            ItemId = stack.ItemId,
            Count = stack.Count,
            EquippedSlot = -1,
            BodySlot = "none",
            Kind = template.Kind switch
            {
                ItemKind.Weapon => "weapon",
                ItemKind.Armor => "armor",
                ItemKind.Etc => "etc",
                _ => "etc"
            },
            Icon = template.Icon,
            WeaponType = "",
            ArmorType = "",
            EnchantLevel = stack.EnchantLevel
        };
    }

    void CastBuff(
        ZoneEntity npc,
        ZoneEntity player,
        int skillId,
        int level,
        int abnormalLevel,
        int durationSeconds)
    {
        if (player.Session == null)
            return;
        long now = _clock().ToUnixTimeMilliseconds();
        ZoneAbnormals.Apply(player.Abnormals, skillId, abnormalLevel, durationSeconds, now, level);
        var cast = new MagicSkillUseMsg
        {
            CasterObjectId = npc.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = 0,
            ReuseDelayMs = 0
        };
                // Net clients only see the gesture through the bridge; the abnormal row alone leaves the pawn silent.
        NetBridge?.FanoutMagicSkillUse([player.ObjectId], cast);
        foreach (var other in Players())
        {
            if (other.Session == null || ReferenceEquals(other, player))
                continue;
            if (!other.Knownlist.Contains(player.ObjectId) && !other.Knownlist.Contains(npc.ObjectId))
                continue;
        }
    }

    bool TryServicePair(ZoneSession session, int npcObjectId, out ZoneEntity player, out ZoneEntity npc, out string? error)
    {
        player = null!;
        npc = null!;
        error = null;
        if (!IsLive(session) || !TryPlayer(session, out player!) || player.Inventory == null)
        {
            error = "world";
            return false;
        }

        if (player.Dead)
        {
            error = InventoryProtocol.Dead;
            return false;
        }

        if (!_byId.TryGetValue(npcObjectId, out npc!) || npc.Kind != ZoneEntityKind.Npc)
        {
            error = QuestProtocol.Npc;
            return false;
        }

        if (npc.Attackable)
        {
            error = NpcHtmlProtocol.Attackable;
            return false;
        }

        if (!InTalkRange(player, npc))
        {
            error = "range";
            return false;
        }

        return true;
    }

    static bool TryReadLines(WarehouseLine[]? lines, out WarehouseLine[] request, out string? error)
    {
        request = [];
        error = null;
        if (lines == null || lines.Length == 0 || lines.Length > WarehouseRules.MaxLines)
        {
            error = "bad_request";
            return false;
        }

        foreach (var line in lines)
        {
            if (line.ObjectId <= 0 || line.Count < 1)
            {
                error = "bad_request";
                return false;
            }
        }

        request = lines;
        return true;
    }

    bool SiegeBlocks(TeleportLocation loc)
    {
        foreach (int castleId in loc.CastleIds)
        {
            var castle = _siege.Find(castleId);
            if (castle is { InProgress: true })
                return true;
        }

        return false;
    }

    (int X, int Y) Offset(int x, int y)
    {
        if (_teleportOffset <= 0)
            return (x, y);
        int ox = x + _serviceRandom.Next(-_teleportOffset, _teleportOffset + 1);
        int oy = y + _serviceRandom.Next(-_teleportOffset, _teleportOffset + 1);
        return Loaded(ox, oy) ? (ox, oy) : (x, y);
    }

    bool Loaded(int x, int y)
    {
        foreach (var entity in _byId.Values)
        {
            if (entity.Kind != ZoneEntityKind.Npc)
                continue;
            if (NpcServiceProtocol.InLoadedZone(x, y, [(entity.X, entity.Y)]))
                return true;
        }

        return false;
    }

    int ClassLevelOf(ZoneEntity player)
    {
        if (PlayerClasses.TryGet(player.ClassId, out var template) && template != null)
            return template.ClassLevel;
        return 0;
    }

    void SaveWarehouse(ZoneSession session, ZoneEntity player)
    {
        if (player.Warehouse == null)
            return;
        if (_deferInventoryPersist)
        {
            _dirtyWarehouses[session.ObjectId] = player;
            return;
        }

        ReplaceWarehouseNow(session, player);
    }

    void ReplaceWarehouseNow(ZoneSession session, ZoneEntity player)
    {
        if (player.Warehouse == null)
            return;
        var stacks = player.Warehouse.Items;
        var stored = new StoredItem[stacks.Count];
        for (int i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            stored[i] = new StoredItem(stack.ItemId, stack.Count, -1, stack.EnchantLevel, stack.ObjectId);
        }

        _persist.ReplaceWarehouse(session.Account, session.ObjectId, stored);
    }

    static int ReadTeleportOffset()
    {
        string? text = Environment.GetEnvironmentVariable("FINEAGE_TELEPORT_OFFSET");
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return NpcServiceProtocol.DefaultTeleportOffset;
        return Math.Clamp(value, 0, 500);
    }

    static bool IsWarehouse(ZoneEntity npc) =>
        string.Equals(npc.NpcType, "Warehouse", StringComparison.OrdinalIgnoreCase);

    void Notice(ZoneSession session, int id, string text)
    {
        var message = new SystemMessageMsg
        {
            Id = id,
            Params = [],
            Text = text
        };
        if (NetBridge != null)
        {
            NetBridge.FanoutSystemMessage([session.ObjectId], new SystemMessageMsg
            {
                Id = message.Id,
                Text = message.Text ?? "",
                Params = message.Params ?? []
            });
        }
    }

    void FanInventory(ZoneSession session, InventoryUpdateEntryMsg[] changes)
    {
        var invUpdate = new InventoryUpdateMsg { Changes = changes };
        if (NetBridge != null)
            NetBridge.FanoutInventoryUpdate([session.ObjectId], invUpdate);
    }

    static (int Id, string Text, string Code) Refusal(NewbieBuffRefusal refusal) => refusal switch
    {
        NewbieBuffRefusal.NotNewbie => (NpcServiceProtocol.BuffNotNewbie, NpcServiceProtocol.BuffNotNewbieText, "newbie"),
        NewbieBuffRefusal.LevelLow => (NpcServiceProtocol.BuffLevelLow, NpcServiceProtocol.BuffLevelLowText, "low"),
        NewbieBuffRefusal.LevelHigh => (NpcServiceProtocol.BuffLevelHigh, NpcServiceProtocol.BuffLevelHighText, "high"),
        NewbieBuffRefusal.ThirdClass => (NpcServiceProtocol.BuffThirdClass, NpcServiceProtocol.BuffThirdClassText, "class"),
        _ => (NpcServiceProtocol.BuffNone, NpcServiceProtocol.BuffNoneText, "none")
    };

    static string FeeName(int itemId) => itemId == WarehouseRules.AdenaItemId ? "Adena" : "items";

    static string HtmlText(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    static InventoryUpdateEntryMsg[] Collapse(List<InventoryUpdateEntryMsg> changes)
    {
        var order = new List<int>();
        var map = new Dictionary<int, InventoryUpdateEntryMsg>();
        foreach (var change in changes)
        {
            if (map.TryAdd(change.ObjectId, change))
                order.Add(change.ObjectId);
            else
                map[change.ObjectId] = change;
        }

        var rows = new InventoryUpdateEntryMsg[order.Count];
        for (int i = 0; i < order.Count; i++)
            rows[i] = map[order[i]];
        return rows;
    }
}
