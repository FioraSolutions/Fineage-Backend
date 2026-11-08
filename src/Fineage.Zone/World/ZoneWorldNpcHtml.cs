using Fineage.Protocol;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TryBypassAsync(ZoneSession session, int npcObjectId, string? command, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = Bypass(session, npcObjectId, command);
        return Task.FromResult<string?>(error);
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

        var handler = _bypasses.Find(verb);
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
            Command = verb
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
            return VillageMasterBypass(session, player, npc, reply.ScriptName, reply.ScriptEvent);

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
        SendHtml(session, player, npc, NpcHtmlPaths.Read(_html, npc.NpcType, npc.NpcId, page));
    }

    void SendQuestList(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        string html = _quests.QuestChoiceHtml(player.Quests, npc.NpcId, npc.Name, QuestActorOf(player));
        // Net client shows quest choice as the same HTML window.
        FanoutNetHtml(session, npc, html);
    }

    void SendHtml(ZoneSession session, ZoneEntity player, ZoneEntity npc, string html)
    {
        string rendered = NpcHtmlText.Apply(html, new NpcHtmlText.Tokens(npc.ObjectId, npc.NpcId, npc.Name, player.Name));
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
