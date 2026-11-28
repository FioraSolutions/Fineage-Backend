using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

/// <summary>
/// Account freight (Mobius <c>PlayerFreight</c>, <c>RequestPackageSend</c>, bypass <c>package_deposit</c> / <c>package_withdraw</c>).
/// </summary>
public sealed partial class ZoneWorld
{
    void EnsureFreightLoaded(ZoneEntity player, CharacterRecord? stored = null)
    {
        if (player.Freight != null)
            return;
        var freight = new PrivateWarehouse(_game.Items, _itemIds);
        var rows = stored?.Freight;
        if (rows == null && player.Session is { } session)
        {
            var fromStore = _accounts?.FindById(session.Account, player.ObjectId);
            rows = fromStore?.Freight;
        }

        if (rows != null)
            RestoreFreight(freight, rows);
        LogWarehouseLoad("freight", player.ObjectId, freight);
        player.Freight = freight;
    }

    PrivateWarehouse FreightWarehouse(ZoneEntity player) =>
        player.Freight ?? throw new InvalidOperationException($"player {player.ObjectId} freight is not loaded");

    /// <summary>Warehouse/freight stacks have no durable piece ownership, unlike an online player's bag.</summary>
    static void RestoreFreight(PrivateWarehouse freight, IEnumerable<StoredItem> rows) =>
        freight.Restore(rows.Select(item => new WarehouseRow(item.ItemId, item.Count, item.EnchantLevel, ObjectId: 0)));

    PrivateWarehouse ResolveRecipientFreight(string account, int recipientId, out ZoneEntity? online)
    {
        online = null;
        if (_byId.TryGetValue(recipientId, out var entity) && entity.Kind == ZoneEntityKind.Player)
        {
            online = entity;
            EnsureFreightLoaded(entity);
            return FreightWarehouse(entity);
        }

        var stored = _accounts?.FindById(account, recipientId);
        var freight = new PrivateWarehouse(_game.Items, _itemIds);
        if (stored?.Freight != null)
            RestoreFreight(freight, stored.Freight);
        LogWarehouseLoad("freight", recipientId, freight);
        return freight;
    }

    bool TryAccountCharacter(string account, int characterId, int selfId, out CharacterRecord? record)
    {
        record = _accounts?.List(account)?.FirstOrDefault(c => c.Id == characterId);
        if (record == null)
        {
            ZoneLog.Warn($"freight recipient object={characterId} not on account {account}");
            return false;
        }

        if (characterId == selfId)
        {
            ZoneLog.Warn($"freight refuse self-ship sender={selfId}");
            return false;
        }

        return true;
    }

    void SaveFreight(ZoneSession session, ZoneEntity player)
    {
        if (player.Freight == null)
            return;
        bool commitNow;
        using (_economyLock.Enter())
        {
            _dirtyFreight[session.ObjectId] = player;
            if (_stepping)
                _economyTouched.Add(session.ObjectId);
            commitNow = !InventoryPersistDeferred;
        }

        if (commitNow)
            TryCommitDirtyEconomy(discardWithoutPublish: null);
    }

    void PersistFreightStacks(string account, int characterId, PrivateWarehouse freight)
    {
        var stacks = freight.Items;
        var stored = new StoredItem[stacks.Count];
        for (int i = 0; i < stacks.Count; i++)
        {
            var stack = stacks[i];
            stored[i] = new StoredItem(stack.ItemId, stack.Count, -1, stack.EnchantLevel, stack.ObjectId);
        }

        using (_economyLock.Enter())
            _dirtyOfflineBags.Add(new EconomyBagWrite(account, characterId) { Freight = stored });
    }

    string? OpenPackageDeposit(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!IsWarehouse(npc))
        {
            ZoneLog.Warn($"package_deposit non-warehouse npc={npc.NpcId} object={npc.ObjectId}");
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return "bad_request";
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            return null;
        }

        var roster = _accounts?.List(session.Account) ?? [];
        if (roster.Count <= 1)
        {
            Notice(session, NpcServiceProtocol.FreightDeferred, "That character does not exist.");
            return null;
        }

        player.ClanWarehouseOpen = false;
        player.FreightWithdrawOpen = false;
        player.FreightKeeperNpcObjectId = npc.ObjectId;
        SendPackageToList(session, player, roster);
        return null;
    }

    string? OpenPackageWithdraw(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!IsWarehouse(npc))
        {
            ZoneLog.Warn($"package_withdraw non-warehouse npc={npc.NpcId} object={npc.ObjectId}");
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return "bad_request";
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            return null;
        }

        EnsureFreightLoaded(player);
        if (player.Freight!.SlotCount == 0)
        {
            Notice(session, NpcServiceProtocol.FreightDeferred, "No packages have arrived.");
            return null;
        }

        player.ClanWarehouseOpen = false;
        player.FreightWithdrawOpen = true;
        player.FreightKeeperNpcObjectId = npc.ObjectId;
        SendWithdrawList(session, player, npc);
        return null;
    }

    void SendPackageToList(ZoneSession session, ZoneEntity player, IReadOnlyList<CharacterRecord> roster)
    {
        var chars = new List<PackageToListEntryMsg>();
        foreach (var character in roster)
        {
            if (character.Id == player.ObjectId)
                continue;
            chars.Add(new PackageToListEntryMsg { ObjectId = character.Id, Name = character.Name });
        }

        if (NetBridge != null)
            NetBridge.FanoutPackageToList([session.ObjectId], new PackageToListMsg { Characters = chars.ToArray() });
    }

    string? PackageSendableList(ZoneSession session, int recipientId)
    {
        if (!IsLive(session) || !TryPlayer(session, out var player) || player.Inventory == null)
            return "world";
        if (!TryAccountCharacter(session.Account, recipientId, player.ObjectId, out _))
            return "bad_request";

        var items = new List<ItemListEntryMsg>();
        foreach (var item in player.Inventory.Items)
        {
            if (!item.IsEquipped && item.Template.CanDropTradeStore)
                items.Add(InventoryMessages.Entry(item));
        }

        var list = new PackageSendableListMsg
        {
            RecipientObjectId = recipientId,
            Adena = player.Inventory.CountOf(WarehouseRules.AdenaItemId),
            Items = items.ToArray()
        };
        if (NetBridge != null)
            NetBridge.FanoutPackageSendableList([session.ObjectId], list);
        return null;
    }

    string? PackageSend(ZoneSession session, int recipientId, WarehouseLine[]? lines)
    {
        if (!IsLive(session) || !TryPlayer(session, out var player) || player.Inventory == null)
            return "world";
        if (player.FreightKeeperNpcObjectId <= 0 ||
            !_byId.TryGetValue(player.FreightKeeperNpcObjectId, out var keeper) ||
            keeper.Kind != ZoneEntityKind.Npc ||
            !IsWarehouse(keeper) ||
            !InInteractCircle(player, keeper))
        {
            ZoneLog.Warn($"package send too far from keeper object={player.ObjectId} keeper={player.FreightKeeperNpcObjectId}");
            return "range";
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            return null;
        }

        if (!TryAccountCharacter(session.Account, recipientId, player.ObjectId, out var recipientRecord))
            return "bad_request";

        if (!TryReadLines(lines, out var request, out var error))
            return error;

        var freight = ResolveRecipientFreight(session.Account, recipientId, out var onlineRecipient);
        int maxSlots = FreightRules.Slots;

        var planned = new List<(ItemInstance Item, int Count, bool Stackable)>();
        var seen = new HashSet<int>();
        int depositedAdena = 0;
        int slots = 0;
        foreach (var line in request)
        {
            if (WarehouseSessionRules.LineDuplicateError(!seen.Add(line.ObjectId)) is { } dupError)
                return dupError;
            var item = player.Inventory.Find(line.ObjectId);
            if (item == null || item.IsEquipped || !item.Template.CanDropTradeStore || item.Count < line.Count)
            {
                Notice(session, NpcServiceProtocol.CannotStore, NpcServiceProtocol.CannotStoreText);
                return null;
            }

            bool stackable = item.Template.Stackable;
            if (WarehouseSessionRules.NonStackableCountError(stackable, line.Count) is { } countError)
                return countError;
            planned.Add((item, line.Count, stackable));
            if (item.ItemId == WarehouseRules.AdenaItemId)
                depositedAdena = checked(depositedAdena + line.Count);
            slots += WarehouseRules.SlotsFor(stackable, freight.ContainsItem(item.ItemId), line.Count);
        }

        if (freight.SlotCount + slots > maxSlots)
        {
            Notice(session, NpcServiceProtocol.WarehouseFull, NpcServiceProtocol.WarehouseFullText);
            return null;
        }

        int fee = FreightRules.DepositFee(planned.Count);
        int adena = player.Inventory.CountOf(WarehouseRules.AdenaItemId);
        if (!WarehouseRules.FeeCovered(adena, depositedAdena, fee))
        {
            Notice(session, NpcServiceProtocol.WarehouseAdena, NpcServiceProtocol.NotEnoughAdenaText);
            return null;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var store = LedgerAccount.Freight(recipientId);
        var move = BeginLedgerMove(LedgerTxKind.FreightDeposit, player.ObjectId, recipientId);
        ObserveLedger(move, bag);
        ObserveLedger(move, store);
        if (!CommitLedgerMutation(move, $"freight send object={player.ObjectId} recipient={recipientId}", () =>
        {
            move.Post(bag, WarehouseRules.AdenaItemId, -fee);
            move.Post(LedgerAccount.Of(SystemAccount.Fees), WarehouseRules.AdenaItemId, fee);
            foreach (var (item, count, _) in planned)
            {
                move.Post(bag, item.ItemId, -count, PiecesOf([item]));
                move.Post(store, item.ItemId, count);
            }
        }, () =>
        {
            if (fee > 0 && !Take(player.Inventory, WarehouseRules.AdenaItemId, fee, changes))
                throw new InvalidOperationException($"freight fee plan failed for object {player.ObjectId}");
            foreach (var (item, count, _) in planned)
            {
                if (!player.Inventory.TryTakeFrom(item.ObjectId, count, out var taken, out bool removed) || taken == null)
                    throw new InvalidOperationException($"freight item object={item.ObjectId} changed after preflight");
                changes.Add(removed
                    ? InventoryMessages.RemoveRow(taken, -1)
                    : InventoryMessages.ChangeRow(InventoryChange.Modify, taken));
                freight.Add(taken.ItemId, count, taken.EnchantLevel, out _);
                if (onlineRecipient != null)
                    onlineRecipient.Freight = freight;
                else
                    PersistFreightStacks(session.Account, recipientId, freight);
            }
        }, LedgerHoldingsOf))
            return "ledger_refused";

        if (onlineRecipient != null)
            SaveFreight(onlineRecipient.Session!, onlineRecipient);

        FanInventory(session, Collapse(changes));
        SaveInventory(session, player.Inventory);
        if (onlineRecipient != null)
            SaveFreight(onlineRecipient.Session!, onlineRecipient);

        Notice(session, NpcServiceProtocol.TransactionComplete, NpcServiceProtocol.TransactionCompleteText);
        _ = recipientRecord;
        return null;
    }

    public Task<string?> TryPackageSendableListAsync(ZoneSession session, int recipientObjectId, CancellationToken ct) =>
        Task.FromResult(WithDeferredInventoryPersist(() => PackageSendableList(session, recipientObjectId)));

    public Task<string?> TryPackageSendAsync(ZoneSession session, int recipientObjectId, WarehouseLine[] lines, CancellationToken ct) =>
        Task.FromResult(WithDeferredInventoryPersist(() => PackageSend(session, recipientObjectId, lines)));
}