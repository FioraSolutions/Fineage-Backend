using Fineage.Game;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public SevenSignsEngine SevenSigns => _sevenSigns;

    public Task<string?> TrySevenSignsStatusAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        lock (_gate)
            QueueRecord(session);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TrySevenSignsActionAsync(ZoneSession session, SevenSignsActionRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = ApplySevenSigns(session, request);
        return Task.FromResult<string?>(error);
    }

    public Task<bool> AdvanceSevenSignsAsync(CancellationToken ct)
    {
        bool advanced;
        lock (_gate)
            advanced = TickSevenSigns();
        return Task.FromResult(advanced);
    }

    bool TickSevenSigns()
    {
        var notices = new List<SevenSignsNotice>();
        if (!_sevenSigns.AdvanceDue(notices))
            return false;

        var sky = _sevenSigns.Sky();
        var live = new List<int>();
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            live.Add(player.ObjectId);
        }

        if (live.Count > 0)
        {
            foreach (var notice in notices)
                NetBridge?.FanoutSystemMessage(live, ToNet(notice));
            NetBridge?.FanoutSevenSignsSky(live, ToNet(sky));
        }

        EvictDungeonLosers();
        return true;
    }

    string? ApplySevenSigns(ZoneSession session, SevenSignsActionRequestMsg request)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";

        var notices = new List<SevenSignsNotice>();
        string? error = request.Action switch
        {
            SevenSignsProtocol.ActionJoin => Join(session, player, request, notices),
            SevenSignsProtocol.ActionContribute => Contribute(session, player.Inventory, request),
            _ => "bad_request"
        };
        if (error != null)
            return error;

        foreach (var notice in notices)
        {
            NetBridge?.FanoutSystemMessage([session.ObjectId], ToNet(notice));
        }
        QueueRecord(session);
        return null;
    }

    string? Join(
        ZoneSession session,
        ZoneEntity player,
        SevenSignsActionRequestMsg request,
        List<SevenSignsNotice> notices)
    {
        if (player.Inventory == null)
            return "world";
        if (!Enum.IsDefined(typeof(SevenSignsCabal), request.Cabal) || !Enum.IsDefined(typeof(SevenSignsSeal), request.Seal))
            return "bad_request";

        int classLevel = 0;
        if (ClassTemplates.TryGet(session.ClassId, out var template) && template != null)
            classLevel = template.ClassLevel;

        var inventory = player.Inventory;
        var changes = new List<InventoryUpdateEntryMsg>();
        string? error = _sevenSigns.TryJoin(
            session.ObjectId,
            classLevel,
            clanOwnsCastle: player.CastleId > 0,
            (SevenSignsCabal)request.Cabal,
            (SevenSignsSeal)request.Seal,
            cost => Pay(inventory, cost, changes),
            notices);
        if (error != null)
            return error;

        if (changes.Count > 0)
        {
            var update = new InventoryUpdateMsg { Changes = changes.ToArray() };
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], update);
            SaveInventory(session, inventory);
        }

        return null;
    }

    string? Contribute(
        ZoneSession session,
        PlayerInventory inventory,
        SevenSignsActionRequestMsg request)
    {
        if (request.BlueStones < 0 || request.GreenStones < 0 || request.RedStones < 0)
            return "bad_request";
        if (!HasStones(inventory, SevenSignsProtocol.BlueSealStoneId, request.BlueStones) ||
            !HasStones(inventory, SevenSignsProtocol.GreenSealStoneId, request.GreenStones) ||
            !HasStones(inventory, SevenSignsProtocol.RedSealStoneId, request.RedStones))
            return SevenSignsProtocol.StonesMissing;

        string? error = _sevenSigns.TryContribute(session.ObjectId, request.BlueStones, request.GreenStones, request.RedStones);
        if (error != null)
            return error;

        var changes = new List<InventoryUpdateEntryMsg>();
        Take(inventory, SevenSignsProtocol.BlueSealStoneId, request.BlueStones, changes);
        Take(inventory, SevenSignsProtocol.GreenSealStoneId, request.GreenStones, changes);
        Take(inventory, SevenSignsProtocol.RedSealStoneId, request.RedStones, changes);
        if (changes.Count > 0)
        {
            var update = new InventoryUpdateMsg { Changes = changes.ToArray() };
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], update);
            SaveInventory(session, inventory);
        }

        return null;
    }

    void QueueRecord(ZoneSession session)
    {
        var status = _sevenSigns.StatusFor(session.ObjectId);
        var sky = _sevenSigns.Sky();
        NetBridge?.FanoutSevenSignsStatus([session.ObjectId], ToNet(status));
        NetBridge?.FanoutSevenSignsSky([session.ObjectId], ToNet(sky));
    }

    bool Pay(PlayerInventory inventory, SevenSignsJoinCost cost, List<InventoryUpdateEntryMsg> changes)
    {
        if (cost.CertificateCount > 0)
            return Take(inventory, _sevenSigns.Options.CertificateItemId, cost.CertificateCount, changes);
        if (cost.Adena > 0)
            return Take(inventory, SevenSignsProtocol.AdenaItemId, cost.Adena, changes);
        return true;
    }

    static bool HasStones(PlayerInventory inventory, int itemId, int count) =>
        count == 0 || (inventory.FindByItemId(itemId)?.Count ?? 0) >= count;

    static bool Take(PlayerInventory inventory, int itemId, int count, List<InventoryUpdateEntryMsg> changes)
    {
        if (count <= 0)
            return true;
        if (!inventory.TryConsume(itemId, count, out var stack, out bool removed) || stack == null)
            return false;
        changes.Add(InventoryMessages.ChangeRow(removed ? InventoryChange.Remove : InventoryChange.Modify, stack));
        return true;
    }

    static SystemMessageMsg ToSystem(SevenSignsNotice notice) => new()
    {
        Id = notice.Id,
        Params = [],
        Text = notice.Text
    };

    static SystemMessageMsg ToNet(SevenSignsNotice notice) => new()
    {
        Id = notice.Id,
        Params = [],
        Text = notice.Text
    };

    static Fineage.Net.Contract.SevenSignsSkyNetMsg ToNet(SevenSignsSkyMsg sky) => new()
    {
        State = sky.State,
        Period = sky.Period,
        WinningCabal = sky.WinningCabal
    };

    static Fineage.Net.Contract.SevenSignsStatusNetMsg ToNet(SevenSignsStatusMsg s) => new()
    {
        Period = s.Period,
        Cycle = s.Cycle,
        PlayerCabal = s.PlayerCabal,
        PlayerSeal = s.PlayerSeal,
        PlayerStoneCount = s.PlayerStoneCount,
        PlayerAncientAdena = s.PlayerAncientAdena,
        PlayerContribution = s.PlayerContribution,
        DawnScore = s.DawnScore,
        DuskScore = s.DuskScore,
        DawnFestivalScore = s.DawnFestivalScore,
        DuskFestivalScore = s.DuskFestivalScore,
        DawnStonePoints = s.DawnStonePoints,
        DuskStonePoints = s.DuskStonePoints,
        AvariceOwner = s.AvariceOwner,
        GnosisOwner = s.GnosisOwner,
        StrifeOwner = s.StrifeOwner,
        DawnMembers = s.DawnMembers,
        DuskMembers = s.DuskMembers,
        WinningCabal = s.WinningCabal,
        NextChangeUnixMs = s.NextChangeUnixMs,
        SkyState = s.SkyState,
        DawnAvarice = s.DawnAvarice,
        DawnGnosis = s.DawnGnosis,
        DawnStrife = s.DawnStrife,
        DuskAvarice = s.DuskAvarice,
        DuskGnosis = s.DuskGnosis,
        DuskStrife = s.DuskStrife,
        PreviousWinner = s.PreviousWinner
    };
}
