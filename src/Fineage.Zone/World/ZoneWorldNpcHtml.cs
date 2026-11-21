using Fineage.Protocol;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryBypassAsync(ZoneSession session, int npcObjectId, string? command, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        // Bypass may class-change / fee / warehouse → SaveInventory after the change (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() => Bypass(session, npcObjectId, command)));
    }

    string? Bypass(ZoneSession session, int npcObjectId, string? command)
    {
        if (!TryPlayer(session, out var player))
            return "world";

        // Mobius admin_* (gmshop / buy / setlevel): no NPC required; KuK + FINEAGE_ZONE_ADMIN.
        if (TryNormalizeAdminCommand(command, out string adminCmd))
            return TryAdminBypass(session, player, adminCmd);

        if (!BypassCommands.TrySplit(npcObjectId, command, out int resolvedId, out var verb))
            return "bad_request";
        if (!_byId.TryGetValue(resolvedId, out var npc) || npc.Kind != ZoneEntityKind.Npc)
            return QuestProtocol.Npc;
        if (npc.Attackable)
            return NpcHtmlProtocol.Attackable;
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (!InTalkRange(player, npc))
            return "range";

        if (string.Equals(verb, "DepositC", StringComparison.Ordinal) || string.Equals(verb, "WithdrawC", StringComparison.Ordinal))
            return OpenClanWarehouse(session, player, npc, deposit: verb == "DepositC");
        if (TryClanMasterVerb(session, player, npc, verb, out var clanError))
            return clanError;

        IBypassHandler? handler = string.Equals(npc.NpcType, "ClanHallManager", StringComparison.Ordinal) &&
                                 ClanHallManagerBypassHandler.IsHallOnlyCommand(verb)
            ? new ClanHallManagerBypassHandler()
            : _bypasses.Find(verb);
        if (handler == null)
        {
            SendHtml(session, player, npc, BypassRegistry.NotImplemented);
            return null;
        }

        var reply = handler.Handle(new BypassCall
        {
            NpcObjectId = npc.ObjectId,
            NpcId = npc.NpcId,
            NpcName = npc.Name,
            NpcType = npc.NpcType,
            PlayerName = player.Name,
            Command = verb,
            Language = session.Language
        });
        if (reply.Error != null)
            return reply.Error;
        if (reply.ReplayTalk)
            return Talk(session, npc.ObjectId, questId: 0, eventName: null, out _);
        if (reply.ShowQuestList)
        {
            SendQuestList(session, player, npc);
            return null;
        }

        if (reply.ScriptName != null)
            return NpcScriptBypass(session, player, npc, reply.ScriptName, reply.ScriptEvent);

        if (reply.QuestAction)
        {
            if (reply.QuestId <= 0 || reply.QuestEvent.Length > 64 || !EventOk(reply.QuestEvent))
                return "bad_request";
            return Talk(session, npc.ObjectId, reply.QuestId, reply.QuestEvent, out _);
        }

        if (reply.BuyListId is int buyListId)
            return OpenBuyList(session, player, npc, buyListId);
        if (reply.OpenSell)
            return OpenSellList(session, player, npc);
        if (reply.MultiSellListId is int multiListId)
            return OpenMultiSell(session, player, npc, multiListId, reply.MultiSellInventoryOnly);
        if (reply.Service != null)
            return ApplyNpcService(session, player, npc, reply);

        SendHtml(session, player, npc, reply.Html ?? NpcHtmlText.NothingToSay);
        return null;
    }

    void SendNpcPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, int page)
    {
        if (page == 0 && OlympiadFirstPage(player, npc, out var olympiad))
        {
            SendHtml(session, player, npc, olympiad!);
            return;
        }
        if (page == 0 && MammonFirstPage(session, player, npc, out var mammon))
        {
            if (mammon != null)
                SendHtml(session, player, npc, mammon);
            return;
        }
        if (page == 0 && FestivalFirstPage(session, player, npc, out var festival))
        {
            if (festival != null)
                SendHtml(session, player, npc, festival);
            return;
        }
        SendHtml(session, player, npc, _pages.ReadNpc(session.Language, npc.NpcType, npc.NpcId, page));
    }

    void SendQuestList(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        string html = _quests.QuestChoiceHtml(player.Quests, npc.NpcId, npc.Name, QuestActorOf(player));
        // Net client shows quest choice as the same HTML window.
        FanoutNetHtml(session, npc, html);
    }

    void SendHtml(ZoneSession session, ZoneEntity player, ZoneEntity npc, string html)
    {
        string npcName = _pages.Name(session.Language, "npc", npc.NpcId, npc.Name);
        string rendered = NpcHtmlText.Apply(html, new NpcHtmlText.Tokens(npc.ObjectId, npc.NpcId, npcName, player.Name));
        LastNpcHtmlForTest = rendered;
        FanoutNetHtml(session, npc, rendered);
    }

    void FanoutNetHtml(ZoneSession session, ZoneEntity npc, string html)
    {
        if (NetBridge == null || session.ObjectId <= 0)
            return;
        NetBridge.FanoutNpcHtml([session.ObjectId], new NpcHtmlMsg
        {
            NpcObjectId = npc.ObjectId,
            NpcId = npc.NpcId,
            Html = html ?? "",
            ItemId = 0
        });
    }

    static bool TryNormalizeAdminCommand(string? raw, out string command)
    {
        command = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = raw.Trim();
        if (text.StartsWith("bypass -h ", StringComparison.OrdinalIgnoreCase))
            text = text["bypass -h ".Length..].TrimStart();
        else if (text.StartsWith("bypass ", StringComparison.OrdinalIgnoreCase))
            text = text["bypass ".Length..].TrimStart();

        // npc_<id>_admin_buy 9917 from HTML when a fake npc id was baked in — still admin.
        if (text.StartsWith("npc_", StringComparison.OrdinalIgnoreCase))
        {
            int split = text.IndexOf('_', 4);
            if (split > 4)
                text = text[(split + 1)..].TrimStart();
        }

        if (!text.StartsWith("admin_", StringComparison.OrdinalIgnoreCase))
            return false;
        if (text.Length > BypassCommands.MaxLength)
            return false;
        command = text;
        return true;
    }
}
