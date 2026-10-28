using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public SevenSignsEngine SevenSigns => _sevenSigns;

    public async Task<string?> TrySevenSignsStatusAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        lock (_gate)
            QueueRecord(session, toSelf);
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TrySevenSignsActionAsync(ZoneSession session, SevenSignsActionRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        string? error;
        lock (_gate)
            error = ApplySevenSigns(session, request, toSelf);
        if (error != null)
            return error;

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<bool> AdvanceSevenSignsAsync(CancellationToken ct)
    {
        var outbound = new List<Pending>();
        bool advanced;
        lock (_gate)
            advanced = TickSevenSigns(outbound);
        if (advanced)
            await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return advanced;
    }

    bool TickSevenSigns(List<Pending> outbound)
    {
        var notices = new List<SevenSignsNotice>();
        if (!_sevenSigns.AdvanceDue(notices))
            return false;

        var sky = _sevenSigns.Sky();
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            foreach (var notice in notices)
                outbound.Add(Pack(player.Session, MsgType.SystemMessage, ToSystem(notice)));
            outbound.Add(Pack(player.Session, MsgType.SevenSignsSky, sky));
        }

        return true;
    }

    string? ApplySevenSigns(ZoneSession session, SevenSignsActionRequestMsg request, List<Pending> toSelf)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Inventory == null)
            return "world";

        var notices = new List<SevenSignsNotice>();
        string? error = request.Action switch
        {
            SevenSignsProtocol.ActionJoin => Join(session, player, request, notices, toSelf),
            SevenSignsProtocol.ActionContribute => Contribute(session, player.Inventory, request, toSelf),
            _ => "bad_request"
        };
        if (error != null)
            return error;

        foreach (var notice in notices)
            toSelf.Add(Pack(session, MsgType.SystemMessage, ToSystem(notice)));
        QueueRecord(session, toSelf);
        return null;
    }

    string? Join(
        ZoneSession session,
        ZoneEntity player,
        SevenSignsActionRequestMsg request,
        List<SevenSignsNotice> notices,
        List<Pending> toSelf)
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
            toSelf.Add(Pack(session, MsgType.InventoryUpdate, new InventoryUpdateMsg { Changes = changes.ToArray() }));
            SaveInventory(session, inventory);
        }

        return null;
    }

    string? Contribute(
        ZoneSession session,
        PlayerInventory inventory,
        SevenSignsActionRequestMsg request,
        List<Pending> toSelf)
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
            toSelf.Add(Pack(session, MsgType.InventoryUpdate, new InventoryUpdateMsg { Changes = changes.ToArray() }));
            SaveInventory(session, inventory);
        }

        return null;
    }

    void QueueRecord(ZoneSession session, List<Pending> toSelf)
    {
        toSelf.Add(Pack(session, MsgType.SevenSignsStatus, _sevenSigns.StatusFor(session.ObjectId)));
        toSelf.Add(Pack(session, MsgType.SevenSignsSky, _sevenSigns.Sky()));
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
}
