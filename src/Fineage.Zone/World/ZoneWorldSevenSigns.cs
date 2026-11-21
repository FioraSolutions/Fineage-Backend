using Fineage.Game;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.SevenSigns;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public SevenSignsEngine SevenSigns => _sevenSigns.Engine;

    public Task<string?> TrySevenSignsStatusAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        QueueRecord(session);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TrySevenSignsActionAsync(ZoneSession session, SevenSignsActionRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        // Seal stone trade mutates inventory → SaveInventory after the change (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() => ApplySevenSigns(session, request)));
    }

    /// <summary>Admin and test hook: runs the Seven Signs actor now; true when the period moved on.</summary>
    public Task<bool> AdvanceSevenSignsAsync(CancellationToken ct)
    {
        bool advanced;
        using (_affinity.Step())
            advanced = AtHookTick(tick => ApplySevenSignsPeriods(_sevenSigns.Step(tick)));
        return Task.FromResult(advanced);
    }

    bool ApplySevenSignsPeriods(IReadOnlyList<SevenSignsPeriodChange> changes)
    {
        if (changes.Count == 0)
            return false;

        var sky = _sevenSigns.Engine.Sky();
        var live = new List<int>();
        foreach (var player in Players())
        {
            if (player.Session == null)
                continue;
            live.Add(player.ObjectId);
        }

        foreach (var change in changes)
        {
            if (live.Count > 0)
            {
                foreach (var notice in change.Notices)
                    NetBridge?.FanoutSystemMessage(live, ToNet(notice));
                NetBridge?.FanoutSevenSignsSky(live, sky);
            }

            EvictDungeonLosers();
        }

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
            SevenSignsProtocol.ActionClaimReward => ClaimSevenSignsReward(session, player.Inventory),
            SevenSignsProtocol.ActionExchangeStones => ExchangeSealStones(session, player.Inventory, request),
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
        string? error = _sevenSigns.Engine.TryJoin(
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

        string? error = _sevenSigns.Engine.TryContribute(session.ObjectId, request.BlueStones, request.GreenStones, request.RedStones);
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

    string? ClaimSevenSignsReward(ZoneSession session, PlayerInventory inventory)
    {
        if (_sevenSigns.Engine.TryClaimReward(session.ObjectId, out int reward) is { } error)
            return error;
        GiveItem(session, inventory, SevenSignsProtocol.AncientAdenaItemId, reward);
        ZoneLog.Info($"[ssq] reward object={session.ObjectId} aa={reward}");
        return null;
    }

    /// <summary>Mobius priest <c>SevenSigns 17/18</c>: the stones are destroyed and paid for in Ancient Adena.</summary>
    string? ExchangeSealStones(ZoneSession session, PlayerInventory inventory, SevenSignsActionRequestMsg request)
    {
        if (request.BlueStones < 0 || request.GreenStones < 0 || request.RedStones < 0)
            return "bad_request";
        int reward = SevenSignsEngine.StoneAncientAdena(request.BlueStones, request.GreenStones, request.RedStones);
        if (reward <= 0)
            return reward < 0 ? "bad_request" : SevenSignsProtocol.StonesMissing;
        if (!HasStones(inventory, SevenSignsProtocol.BlueSealStoneId, request.BlueStones) ||
            !HasStones(inventory, SevenSignsProtocol.GreenSealStoneId, request.GreenStones) ||
            !HasStones(inventory, SevenSignsProtocol.RedSealStoneId, request.RedStones))
            return SevenSignsProtocol.StonesMissing;

        var changes = new List<InventoryUpdateEntryMsg>();
        Take(inventory, SevenSignsProtocol.BlueSealStoneId, request.BlueStones, changes);
        Take(inventory, SevenSignsProtocol.GreenSealStoneId, request.GreenStones, changes);
        Take(inventory, SevenSignsProtocol.RedSealStoneId, request.RedStones, changes);
        if (changes.Count > 0)
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], new InventoryUpdateMsg { Changes = changes.ToArray() });
        GiveItem(session, inventory, SevenSignsProtocol.AncientAdenaItemId, reward);
        return null;
    }

    void QueueRecord(ZoneSession session)
    {
        var status = _sevenSigns.Engine.StatusFor(session.ObjectId);
        var sky = _sevenSigns.Engine.Sky();
        NetBridge?.FanoutSevenSignsStatus([session.ObjectId], status);
        NetBridge?.FanoutSevenSignsSky([session.ObjectId], sky);
    }

    bool Pay(PlayerInventory inventory, SevenSignsJoinCost cost, List<InventoryUpdateEntryMsg> changes)
    {
        if (cost.CertificateCount > 0)
            return Take(inventory, _sevenSigns.Engine.Options.CertificateItemId, cost.CertificateCount, changes);
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


}
