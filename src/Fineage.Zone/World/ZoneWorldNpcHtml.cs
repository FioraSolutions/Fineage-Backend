using Fineage.Protocol;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryBypassAsync(ZoneSession session, int npcObjectId, string? command, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        string? error;
        var toSelf = new List<Pending>();
        lock (_gate)
            error = Bypass(session, npcObjectId, command, toSelf);
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return error;
    }

    string? Bypass(ZoneSession session, int npcObjectId, string? command, List<Pending> toSelf)
    {
        if (!TryPlayer(session, out var player))
            return "world";
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
            SendHtml(session, player, npc, BypassRegistry.NotImplemented, toSelf);
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
            return Talk(session, npc.ObjectId, questId: 0, eventName: null, toSelf, out _);
        if (reply.ShowQuestList)
        {
            SendQuestList(session, player, npc, toSelf);
            return null;
        }

        if (reply.QuestAction)
        {
            if (reply.QuestId <= 0 || reply.QuestEvent.Length > 64 || !EventOk(reply.QuestEvent))
                return "bad_request";
            return Talk(session, npc.ObjectId, reply.QuestId, reply.QuestEvent, toSelf, out _);
        }

        SendHtml(session, player, npc, reply.Html ?? NpcHtmlText.NothingToSay, toSelf);
        return null;
    }

    void SendNpcPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, int page, List<Pending> toSelf)
    {
        SendHtml(session, player, npc, NpcHtmlPaths.Read(_html, npc.NpcType, npc.NpcId, page), toSelf);
    }

    void SendQuestList(ZoneSession session, ZoneEntity player, ZoneEntity npc, List<Pending> toSelf)
    {
        toSelf.Add(Pack(session, MsgType.QuestDialog, new QuestDialogMsg
        {
            NpcObjectId = npc.ObjectId,
            NpcId = npc.NpcId,
            QuestId = 0,
            Hook = "default",
            Event = "",
            Html = _quests.QuestChoiceHtml(player.Quests, npc.NpcId, npc.Name)
        }));
    }

    void SendHtml(ZoneSession session, ZoneEntity player, ZoneEntity npc, string html, List<Pending> toSelf)
    {
        toSelf.Add(Pack(session, MsgType.NpcHtml, new NpcHtmlMsg
        {
            NpcObjectId = npc.ObjectId,
            NpcId = npc.NpcId,
            Html = NpcHtmlText.Apply(html, new NpcHtmlText.Tokens(npc.ObjectId, npc.NpcId, npc.Name, player.Name)),
            ItemId = 0
        }));
    }
}
