using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

/// <summary>
/// Clan warehouse after Mobius <c>ClanWarehouse</c> and the warehouse bypasses <c>DepositC</c> / <c>WithdrawC</c>:
/// clan level 1+ (710), withdraw window needs ACCESS_WAREHOUSE (709), withdrawing itself is leader-only
/// (<c>AltMembersCanWithdrawFromClanWH=false</c>), 200 slots, same 30 adena deposit fee. Rows persist in the clan payload.
/// </summary>
public sealed partial class ZoneWorld
{
    PrivateWarehouse? ClanWarehouseOf(ZoneClan clan)
    {
        if (clan.Warehouse != null)
            return clan.Warehouse;
        var wh = new PrivateWarehouse(_game.Items);
        wh.Restore(clan.D.Warehouse.Select(i => new WarehouseRow(i.ItemId, i.Count, i.EnchantLevel, i.ObjectId)));
        clan.Warehouse = wh;
        return wh;
    }

    /// <summary>The warehouse the open window points at (Mobius <c>getActiveWarehouse</c>).</summary>
    PrivateWarehouse? ActiveWarehouse(ZoneEntity player)
    {
        if (player.FreightWithdrawOpen)
            return player.Freight;
        if (!player.ClanWarehouseOpen)
            return player.Warehouse;
        var clan = ClanOf(player.ObjectId);
        return clan == null ? null : ClanWarehouseOf(clan);
    }

    /// <summary>The ledger account of the warehouse the open window points at.</summary>
    LedgerAccount ActiveWarehouseAccount(ZoneEntity player)
    {
        if (player.FreightWithdrawOpen)
            return LedgerAccount.Freight(player.ObjectId);
        return player.ClanWarehouseOpen
            ? LedgerAccount.ClanWarehouse(ClanOf(player.ObjectId)?.Id
                ?? throw new InvalidOperationException($"player {player.ObjectId} has a clan warehouse open without a clan"))
            : LedgerAccount.Warehouse(player.ObjectId);
    }

    int ActiveWarehouseSlots(ZoneEntity player)
    {
        if (player.FreightWithdrawOpen)
            return FreightRules.Slots;
        return player.ClanWarehouseOpen ? PledgeProtocol.ClanWarehouseSlots : WarehouseRules.WarehouseSlots(player.Race);
    }

    void SaveActiveWarehouse(ZoneSession session, ZoneEntity player)
    {
        if (player.FreightWithdrawOpen)
        {
            SaveFreight(session, player);
            return;
        }
        if (!player.ClanWarehouseOpen)
        {
            SaveWarehouse(session, player);
            return;
        }
        if (ClanOf(player.ObjectId) is { } clan)
            SaveClan(clan);
    }

    string? ClanDepositBlock(ZoneSession session, ZoneEntity player)
    {
        if (!player.ClanWarehouseOpen)
            return null;
        var clan = ClanOf(player.ObjectId);
        if (clan == null || clan.Level < 1)
        {
            SelfLine(session, 710, "Only clans of clan level 1 or higher can use a clan warehouse.");
            return PledgeProtocol.Conditions;
        }
        return null;
    }

    string? ClanWithdrawBlock(ZoneSession session, ZoneEntity player)
    {
        if (!player.ClanWarehouseOpen)
            return null;
        var clan = ClanOf(player.ObjectId);
        if (clan == null || clan.Level < 1)
        {
            SelfLine(session, 710, "Only clans of clan level 1 or higher can use a clan warehouse.");
            return PledgeProtocol.Conditions;
        }
        if (!HasPriv(clan, player.ObjectId, PledgeProtocol.PrivAccessWarehouse) || clan.LeaderId != player.ObjectId)
        {
            SelfLine(session, 709, "You do not have the right to use the clan warehouse.");
            return PledgeProtocol.Denied;
        }
        return null;
    }

    /// <summary>Mobius warehouse bypass <c>DepositC</c> / <c>WithdrawC</c>.</summary>
    string? OpenClanWarehouse(ZoneSession session, ZoneEntity player, ZoneEntity npc, bool deposit)
    {
        if (!IsWarehouse(npc))
        {
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>" + NpcServiceProtocol.NotWarehouseText + "</body></html>");
            return null;
        }
        var clan = ClanOf(player.ObjectId);
        if (clan == null)
        {
            SelfLine(session, 709, "You do not have the right to use the clan warehouse.");
            return PledgeProtocol.Denied;
        }
        if (!deposit && !HasPriv(clan, player.ObjectId, PledgeProtocol.PrivAccessWarehouse))
        {
            SelfLine(session, 709, "You do not have the right to use the clan warehouse.");
            return PledgeProtocol.Denied;
        }
        if (clan.Level < 1)
        {
            SelfLine(session, 710, "Only clans of clan level 1 or higher can use a clan warehouse.");
            return PledgeProtocol.Conditions;
        }
        if (player.ActiveEnchantObjectId != 0)
        {
            Notice(session, NpcServiceProtocol.EnchantOpen, NpcServiceProtocol.EnchantOpenText);
            return null;
        }

        player.ClanWarehouseOpen = true;
        if (deposit)
            SendDepositList(session, player, npc);
        else
            SendWithdrawList(session, player, npc);
        return null;
    }

    /// <summary>Test hook: the clan warehouse's stack object ids.</summary>
    internal List<int> ClanWarehouseStacksForTest(int clanId)
    {
        using (_affinity.Step())
            return _clan.TryGet(clanId, out var clan) ? ClanWarehouseOf(clan)!.Items.Select(i => i.ObjectId).ToList() : [];
    }

    /// <summary>Test hook: the clan warehouse rows (item id → count).</summary>
    internal Dictionary<int, int> ClanWarehouseForTest(int clanId)
    {
        using (_affinity.Step())
        {
            if (!_clan.TryGet(clanId, out var clan))
                return [];
            return ClanWarehouseOf(clan)!.Items.GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));
        }
    }
}
