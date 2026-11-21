using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Clan;
using Fineage.Zone.Combat;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Npcs;
using Fineage.Zone.Simulation;
using Fineage.Zone.Inventory;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>ClanHallManager</c> leased functions: HP/MP/EXP regen, teleport, support, item shop, decor; fees from the
/// clan warehouse with ledger <see cref="LedgerTxKind.ClanHallFunction"/> / <see cref="SystemAccount.ClanHall"/>.
/// </summary>
public sealed partial class ZoneWorld
{
    HallFunctionCatalog? _hallFunctionCatalog;
    readonly HallFunctionActor _hallFunctions = new();

    HallFunctionCatalog HallFunctionCatalog =>
        _hallFunctionCatalog ??= HallFunctionCatalog.LoadDefault();

    ClanHallSave? HallOfNpc(ZoneEntity npc)
    {
        int hallId = HallNearNpc(npc.X, npc.Y);
        if (hallId <= 0)
            return null;
        return Halls(NowUnixMs()).GetValueOrDefault(hallId);
    }

    static ClanHallFunctionSave? FindHallFunction(ClanHallSave hall, int type) =>
        hall.Functions.FirstOrDefault(f => f.Type == type);

    void TickHallFunctions(TickContext tick)
    {
        if (HallCatalog.Halls.Count == 0)
            return;
        var halls = Halls(tick.WallUnixMs);
        _hallFunctions.Halls = halls;
        RenewHallFunctions(_hallFunctions.Step(tick), tick.WallUnixMs);
    }

    void RenewHallFunctions(IReadOnlyList<HallFunctionDue> due, long now)
    {
        if (due.Count == 0)
            return;
        bool changed = false;
        var halls = Halls(now);
        foreach (var item in due)
        {
            if (!halls.TryGetValue(item.HallId, out var hall) || hall.OwnerClanId <= 0)
                continue;
            if (item.Type == 0)
            {
                changed |= TryRenewHallLease(hall, now);
                continue;
            }

            var fn = FindHallFunction(hall, item.Type);
            if (fn == null || fn.Level <= 0)
                continue;
            changed |= TryRenewHallFunction(hall, fn, now);
        }

        if (changed)
        {
            SaveHalls();
            foreach (var hall in halls.Values.Where(h => h.OwnerClanId > 0))
            {
                if (_clan.TryGet(hall.OwnerClanId, out var clan))
                    SyncHallRegenForClan(clan);
                BroadcastAgitDecoInHall(hall);
            }
        }
    }

    bool TryRenewHallLease(ClanHallSave hall, long now)
    {
        if (!_clan.TryGet(hall.OwnerClanId, out var clan))
            return false;
        HallCatalog.TryGet(hall.Id, out var info);
        int lease = (int)Math.Min(info?.Lease ?? 0, int.MaxValue);
        if (lease <= 0)
        {
            hall.PaidUntilUnixMs = now + HallFunctionProtocol.HallLeasePeriodMs;
            return true;
        }

        var wh = ClanWarehouseOf(clan)!;
        var adena = wh.FindByItemId(PledgeProtocol.AdenaItemId);
        if (adena == null || adena.Count < lease)
        {
            hall.OwnerClanId = 0;
            hall.PaidUntilUnixMs = 0;
            hall.Functions.Clear();
            hall.DecoCurtains = 0;
            hall.DecoFront = 0;
            hall.AuctionEndUnixMs = now + HallAuctionMs;
            clan.D.HallId = 0;
            SaveClan(clan);
            return true;
        }

        if (!PayClanWarehouseFee(clan, wh, lease, LedgerTxKind.ClanHallLease, hall.Id, causedBy: 0))
            return false;
        hall.PaidUntilUnixMs = now + HallFunctionProtocol.HallLeasePeriodMs;
        return true;
    }

    bool TryRenewHallFunction(ClanHallSave hall, ClanHallFunctionSave fn, long now)
    {
        if (!_clan.TryGet(hall.OwnerClanId, out var clan))
            return false;
        if (fn.Lease <= 0)
        {
            hall.Functions.Remove(fn);
            SyncHallDeco(hall);
            return true;
        }

        var wh = ClanWarehouseOf(clan)!;
        var adena = wh.FindByItemId(PledgeProtocol.AdenaItemId);
        if (adena == null || adena.Count < fn.Lease)
        {
            hall.Functions.Remove(fn);
            SyncHallDeco(hall);
            return true;
        }

        if (!PayClanWarehouseFee(clan, wh, fn.Lease, LedgerTxKind.ClanHallFunction, fn.Type, causedBy: 0))
            return false;
        if (!HallFunctionCatalog.TryFee(fn.Type, fn.Level, out _, out long ratio))
            ratio = HallFunctionProtocol.HallLeasePeriodMs;
        fn.EndUnixMs = now + ratio;
        return true;
    }

    static void SyncHallDeco(ClanHallSave hall)
    {
        hall.DecoFront = FindHallFunction(hall, HallFunctionProtocol.FuncDecoFront)?.Level ?? 0;
        hall.DecoCurtains = FindHallFunction(hall, HallFunctionProtocol.FuncDecoCurtains)?.Level ?? 0;
    }

    string? TryHallManagerService(ZoneSession session, ZoneEntity player, ZoneEntity npc, BypassReply reply)
    {
        if (!IsLive(session))
            return "world";
        if (!ZoneCombat.Within(player.X, player.Y, npc.X, npc.Y, HallFunctionProtocol.ManagerRange))
            return NpcServiceProtocol.NotWarehouseText;
        if (!string.Equals(npc.NpcType, "ClanHallManager", StringComparison.Ordinal))
            return "bad_request";

        var hall = HallOfNpc(npc);
        if (hall == null)
            return PledgeProtocol.BadArgument;
        var clan = ClanOf(player.ObjectId);
        if (clan == null || hall.OwnerClanId != clan.Id)
            return NotAuthorized(session);

        return reply.Service switch
        {
            NpcServiceIntent.HallFunction => TryHallFunctionBypass(session, player, clan, hall, reply.ServiceArg),
            NpcServiceIntent.HallDeco => TryHallDecoBypass(session, player, clan, hall, reply.ServiceArg),
            NpcServiceIntent.HallLease => TryHallLeaseBypass(session, player, clan, hall, reply.ServiceArg),
            NpcServiceIntent.HallSupport => TryHallSupport(session, player, clan, hall, npc, reply.ServiceArg),
            NpcServiceIntent.HallTeleport => TryHallTeleport(session, player, clan, hall, npc, reply.ServiceArg),
            NpcServiceIntent.HallItemCreate => TryHallItemCreate(session, player, clan, hall, npc, reply.ServiceArg),
            _ => "bad_request"
        };
    }

    string? TryHallItemCreate(ZoneSession session, ZoneEntity player, ZoneClan clan, ClanHallSave hall, ZoneEntity npc, string command)
    {
        if (!HasPriv(clan, player.ObjectId, PledgeProtocol.PrivHallFunctions))
            return NotAuthorized(session);
        var fn = FindHallFunction(hall, HallFunctionProtocol.FuncItemCreate);
        if (fn is not { Level: > 0 })
            return PledgeProtocol.Conditions;
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3
            || !parts[0].Equals("functions", StringComparison.OrdinalIgnoreCase)
            || !parts[1].Equals("item_creation", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(parts[2], out int hallToken)
            || hallToken != hall.Id)
            return PledgeProtocol.BadArgument;
        int listId = HallFunctionProtocol.ItemCreateListId(hall.Id, fn.Level);
        if (!_multisells.TryGet(listId, out _))
            return PledgeProtocol.Conditions;
        return OpenMultiSell(session, player, npc, listId, inventoryOnly: false);
    }

    string? TryHallFunctionBypass(ZoneSession session, ZoneEntity player, ZoneClan clan, ClanHallSave hall, string command)
    {
        if (!HasPriv(clan, player.ObjectId, PledgeProtocol.PrivHallManageFunctions))
            return NotAuthorized(session);
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 4 || !parts[0].Equals("manage", StringComparison.OrdinalIgnoreCase))
            return PledgeProtocol.BadArgument;

        int type;
        int level;
        if (parts[1].Equals("recovery", StringComparison.OrdinalIgnoreCase))
        {
            type = parts[2].ToLowerInvariant() switch
            {
                "hp" => HallFunctionProtocol.FuncRestoreHp,
                "mp" => HallFunctionProtocol.FuncRestoreMp,
                "exp" => HallFunctionProtocol.FuncRestoreExp,
                _ => 0
            };
            if (type == 0 || parts.Length < 4 || !int.TryParse(parts[3], out level))
                return PledgeProtocol.BadArgument;
        }
        else if (parts[1].Equals("other", StringComparison.OrdinalIgnoreCase))
        {
            type = parts[2].ToLowerInvariant() switch
            {
                "tele" => HallFunctionProtocol.FuncTeleport,
                "item" => HallFunctionProtocol.FuncItemCreate,
                "support" => HallFunctionProtocol.FuncSupport,
                _ => 0
            };
            if (type == 0 || parts.Length < 4 || !int.TryParse(parts[3], out level))
                return PledgeProtocol.BadArgument;
        }
        else
            return PledgeProtocol.BadArgument;

        return ApplyHallFunction(session, player, clan, hall, type, level);
    }

    string? TryHallDecoBypass(ZoneSession session, ZoneEntity player, ZoneClan clan, ClanHallSave hall, string command)
    {
        if (!HasPriv(clan, player.ObjectId, PledgeProtocol.PrivHallManageFunctions))
            return NotAuthorized(session);
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 5)
            return PledgeProtocol.BadArgument;
        int type = parts[3].Equals("curtains", StringComparison.OrdinalIgnoreCase)
            ? HallFunctionProtocol.FuncDecoCurtains
            : parts[3].Equals("fixtures", StringComparison.OrdinalIgnoreCase)
                ? HallFunctionProtocol.FuncDecoFront
                : 0;
        if (type == 0 || !int.TryParse(parts[4], out int level))
            return PledgeProtocol.BadArgument;
        return ApplyHallFunction(session, player, clan, hall, type, level);
    }

    string? TryHallLeaseBypass(ZoneSession session, ZoneEntity player, ZoneClan clan, ClanHallSave hall, string command)
    {
        if (!HasPriv(clan, player.ObjectId, PledgeProtocol.PrivHallManageFunctions))
            return NotAuthorized(session);
        if (!command.Contains("pay", StringComparison.OrdinalIgnoreCase))
            return null;
        HallCatalog.TryGet(hall.Id, out var info);
        int lease = (int)Math.Min(info?.Lease ?? 0, int.MaxValue);
        if (lease <= 0)
            return null;
        long now = NowUnixMs();
        var wh = ClanWarehouseOf(clan)!;
        if (!PayClanWarehouseFee(clan, wh, lease, LedgerTxKind.ClanHallLease, hall.Id, player.ObjectId))
            return PledgeProtocol.Conditions;
        hall.PaidUntilUnixMs = now + HallFunctionProtocol.HallLeasePeriodMs;
        SaveHalls();
        SelfLine(session, 0, "The clan hall lease has been extended.");
        return null;
    }

    string? ApplyHallFunction(ZoneSession session, ZoneEntity player, ZoneClan clan, ClanHallSave hall, int type, int level)
    {
        long now = NowUnixMs();
        var existing = FindHallFunction(hall, type);
        if (existing != null && existing.Level == level)
            return null;

        if (level == 0)
        {
            if (existing != null)
                hall.Functions.Remove(existing);
            SyncHallDeco(hall);
            SaveHalls();
            SyncHallRegenForClan(clan);
            BroadcastAgitDecoInHall(hall);
            SendAgitDecoInfo(session, hall);
            return null;
        }

        if (!HallFunctionCatalog.TryFee(type, level, out int fee, out long ratioMs))
            return PledgeProtocol.BadArgument;

        var wh = ClanWarehouseOf(clan)!;
        if (!PayClanWarehouseFee(clan, wh, fee, LedgerTxKind.ClanHallFunction, type, player.ObjectId))
            return PledgeProtocol.Conditions;

        if (existing == null)
        {
            existing = new ClanHallFunctionSave { Type = type };
            hall.Functions.Add(existing);
        }

        existing.Level = level;
        existing.Lease = fee;
        existing.EndUnixMs = now + ratioMs;
        SyncHallDeco(hall);
        SaveHalls();
        SyncHallRegenForClan(clan);
        BroadcastAgitDecoInHall(hall);
        SendAgitDecoInfo(session, hall);
        SelfLine(session, 0, "The clan hall function has been activated.");
        return null;
    }

    string? TryHallSupport(ZoneSession session, ZoneEntity player, ZoneClan clan, ClanHallSave hall, ZoneEntity npc, string command)
    {
        if (!HasPriv(clan, player.ObjectId, PledgeProtocol.PrivHallFunctions))
            return NotAuthorized(session);
        var fn = FindHallFunction(hall, HallFunctionProtocol.FuncSupport);
        if (fn == null || fn.Level <= 0)
            return PledgeProtocol.Conditions;
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out int skillId))
            return PledgeProtocol.BadArgument;
        int skillLevel = parts.Length >= 3 && int.TryParse(parts[2], out int lvl) ? lvl : fn.Level;
        if (skillLevel > fn.Level)
            return PledgeProtocol.BadArgument;
        if (!HallFunctionCatalog.BuffMpFree && npc.CurMp < 1)
            return PledgeProtocol.Conditions;
        return CastNpcSupportSkill(
                npc,
                player,
                new NpcAiSupportSkill(skillId, skillLevel, HitTimeMs: 0, ReuseDelayMs: 0, Mp: 0, MpInitial: 0),
                NpcSupportKind.Buff,
                NowUnixMs())
            ? null
            : PledgeProtocol.Conditions;
    }

    string? TryHallTeleport(ZoneSession session, ZoneEntity player, ZoneClan clan, ClanHallSave hall, ZoneEntity npc, string command)
    {
        if (!HasPriv(clan, player.ObjectId, PledgeProtocol.PrivHallFunctions))
            return NotAuthorized(session);
        var fn = FindHallFunction(hall, HallFunctionProtocol.FuncTeleport);
        if (fn == null || fn.Level <= 0)
            return PledgeProtocol.Conditions;
        var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out int locId))
            return PledgeProtocol.BadArgument;
        string list = $"tel{fn.Level}";
        return TeleportTo(session, npc.ObjectId, list, locId, html: true);
    }

    bool PayClanWarehouseFee(ZoneClan clan, PrivateWarehouse wh, int fee, LedgerTxKind kind, int reference, int causedBy)
    {
        if (fee <= 0)
            return true;
        var account = LedgerAccount.ClanWarehouse(clan.Id);
        var before = HoldingsOf(wh);
        var adena = wh.FindByItemId(PledgeProtocol.AdenaItemId);
        if (adena == null || adena.Count < fee)
            return false;
        wh.TryTake(adena.ObjectId, fee, out _, out _);
        SaveClan(clan);
        var after = HoldingsOf(wh);
        if (after.SameAs(before))
            return true;
        var move = BeginLedgerMove(kind, causedBy, reference);
        move.Observe(account, before);
        PostChange(move, account, before, after, LedgerAccount.Of(SystemAccount.ClanHall));
        move.Commit(LedgerHoldingsOf);
        return true;
    }

    bool PlayerInsideHallArea(int hallId, int x, int y) => HallAtPosition(x, y) == hallId;

    void SyncHallRegenForClan(ZoneClan clan)
    {
        double hp = 1d;
        double mp = 1d;
        long now = NowUnixMs();
        if (clan.D.HallId > 0 &&
            Halls(now).TryGetValue(clan.D.HallId, out var hall) &&
            hall.OwnerClanId == clan.Id)
        {
            var hpFn = FindHallFunction(hall, HallFunctionProtocol.FuncRestoreHp);
            if (hpFn is { Level: > 0 })
                hp += hpFn.Level / 100d;
            var mpFn = FindHallFunction(hall, HallFunctionProtocol.FuncRestoreMp);
            if (mpFn is { Level: > 0 })
                mp += mpFn.Level / 100d;
        }

        foreach (int id in OnlineIds(clan))
        {
            if (!_byId.TryGetValue(id, out var player))
                continue;
            player.HallRegenHpMult = hp;
            player.HallRegenMpMult = mp;
        }
    }

    (double HpMult, double MpMult) HallRegenBonus(ZoneEntity player)
    {
        if (player.HideoutId <= 0 || !PlayerInsideHallArea(player.HideoutId, player.X, player.Y))
            return (1d, 1d);
        return (player.HallRegenHpMult, player.HallRegenMpMult);
    }

    internal string? HallFunctionCommandForTest(ZoneSession session, ZoneEntity player, ZoneEntity npc, string command)
    {
        using (_affinity.Step())
        {
            var reply = BypassReply.ServiceCall(
                command.Contains(" deco ", StringComparison.OrdinalIgnoreCase)
                    ? NpcServiceIntent.HallDeco
                    : command.StartsWith("support", StringComparison.OrdinalIgnoreCase)
                        ? NpcServiceIntent.HallSupport
                        : command.StartsWith("goto", StringComparison.OrdinalIgnoreCase)
                            ? NpcServiceIntent.HallTeleport
                            : NpcServiceIntent.HallFunction,
                command);
            return TryHallManagerService(session, player, npc, reply);
        }
    }

    internal void TickHallFunctionsForTest(long now)
    {
        using (_affinity.Step())
        {
            var halls = Halls(now);
            _hallFunctions.Halls = halls;
            RenewHallFunctions(_hallFunctions.Step(new TickContext(_simulationTicks.Tick, now, now)), now);
        }
    }

    internal ClanHallFunctionSave? HallFunctionForTest(int hallId, int type)
    {
        using (_affinity.Step())
            return Halls(NowUnixMs()).TryGetValue(hallId, out var hall)
                ? FindHallFunction(hall, type)
                : null;
    }

    internal string? HallItemCreateCommandForTest(ZoneSession session, ZoneEntity player, ZoneEntity npc, string command)
    {
        using (_affinity.Step())
        {
            var reply = BypassReply.ServiceCall(NpcServiceIntent.HallItemCreate, command);
            return TryHallManagerService(session, player, npc, reply);
        }
    }

    internal void SetHallWarehouseAdenaForTest(int clanId, int adena)
    {
        using (_affinity.Step())
        {
            if (!_clan.TryGet(clanId, out var clan))
                return;
            clan.D.Warehouse.Clear();
            clan.Warehouse = null;
            if (adena > 0)
                ClanWarehouseOf(clan)!.Add(PledgeProtocol.AdenaItemId, adena, 0, out _);
            SaveClan(clan);
        }
    }
}