using Fineage.Game.Economy;
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
    string? OpenDeposit(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!IsWarehouse(npc))
        {
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return null;
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.EnchantOpenText + "</body></html>");
            return null;
        }

        player.ClanWarehouseOpen = false;
        player.FreightWithdrawOpen = false;
        SendDepositList(session, player, npc);
        SendHtml(session, player, npc, "<html><body>%npcname%:<br>Deposit an item. The fee is 30 adena per item.</body></html>");
        return null;
    }

    string? OpenWithdraw(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!IsWarehouse(npc))
        {
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return null;
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.EnchantOpenText + "</body></html>");
            return null;
        }

        player.ClanWarehouseOpen = false;
        player.FreightWithdrawOpen = false;
        SendWithdrawList(session, player, npc);
        SendHtml(session, player, npc, "<html><body>%npcname%:<br>Withdraw an item from your private warehouse.</body></html>");
        return null;
    }

    string? DepositPrivate(ZoneSession session, int npcObjectId, WarehouseLine[]? lines, bool html)
    {
        // Mobius SendWareHouseDepositList: isInsideRadius2D(manager, INTERACTION_DISTANCE).
        if (!TryServicePair(session, npcObjectId, out var player, out var npc, out var error, InteractGate.Circle2D))
            return error;
        var wh = ActiveWarehouse(player);
        if (player.Inventory == null || wh == null)
            return "world";
        if (WarehouseSessionRules.NpcWindowError(IsWarehouse(npc)) is { } depositNpcError)
        {
            if (html)
                SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return depositNpcError;
        }

        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            return null;
        }

        if (ClanDepositBlock(session, player) is { } clanDepositError)
            return clanDepositError;

        if (!TryReadLines(lines, out var request, out error))
            return error;

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
            slots += WarehouseRules.SlotsFor(stackable, wh.ContainsItem(item.ItemId), line.Count);
        }

        if (wh.SlotCount + slots > ActiveWarehouseSlots(player))
        {
            Notice(session, NpcServiceProtocol.WarehouseFull, NpcServiceProtocol.WarehouseFullText);
            return null;
        }

        int fee = WarehouseRules.DepositFee(planned.Count);
        int adena = player.Inventory.CountOf(WarehouseRules.AdenaItemId);
        if (!WarehouseRules.FeeCovered(adena, depositedAdena, fee))
        {
            Notice(session, NpcServiceProtocol.WarehouseAdena, NpcServiceProtocol.NotEnoughAdenaText);
            return null;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        var bag = LedgerAccount.Inventory(player.ObjectId);
        var store = ActiveWarehouseAccount(player);
        var move = BeginLedgerMove(LedgerTxKind.WarehouseDeposit, player.ObjectId, npc.NpcId);
        ObserveLedger(move, bag);
        ObserveLedger(move, store);
        if (!CommitLedgerMutation(move, $"warehouse deposit object={player.ObjectId} keeper={npc.ObjectId}", () =>
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
                throw new InvalidOperationException($"warehouse deposit fee plan failed for object {player.ObjectId}");
            foreach (var (item, count, _) in planned)
            {
                if (!player.Inventory.TryTakeFrom(item.ObjectId, count, out var taken, out bool removed) || taken == null)
                    throw new InvalidOperationException($"warehouse deposit item object={item.ObjectId} changed after preflight");
                changes.Add(removed
                    ? InventoryMessages.RemoveRow(taken, -1)
                    : InventoryMessages.ChangeRow(InventoryChange.Modify, taken));
                wh.Add(taken.ItemId, count, taken.EnchantLevel, out _);
            }
        }, LedgerHoldingsOf))
            return "ledger_refused";

        FanInventory(session, Collapse(changes));
        SaveInventory(session, player.Inventory);
        SaveActiveWarehouse(session, player);
        Notice(session, NpcServiceProtocol.TransactionComplete, NpcServiceProtocol.TransactionCompleteText);
        return null;
    }

}
