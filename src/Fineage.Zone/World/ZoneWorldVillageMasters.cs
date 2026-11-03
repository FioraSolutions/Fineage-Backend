using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;
using MessagePack;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius village_master scripts on Net (<c>Quest FirstClassTransferTalk</c>, <c>Quest ElfHumanWizardChange1 26</c>, ...):
/// decisions come from <see cref="VillageMasterCatalog"/> (data/village-masters.json), pages from data/html/village_master.
/// A transfer takes the proof item, sets the class (Mobius setClassId + setBaseClass), gives 15 D-grade exchange coupons,
/// refreshes vitals / skills / UserInfo / CharInfo, and saves class, bag and progress.
/// </summary>
public sealed partial class ZoneWorld
{
    VillageMasterCatalog? _villageMasters;

    internal VillageMasterCatalog VillageMasters
    {
        get => _villageMasters ??= VillageMasterCatalog.LoadDefault();
        set => _villageMasters = value;
    }

    /// <summary>Mobius SystemMessageId 1308 CONGRATULATIONS_YOU_VE_COMPLETED_THE_CLASS_TRANSFER (sent by setClassId).</summary>
    internal const int ClassTransferMessageId = 1308;

    string? VillageMasterBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string script, string evt, List<Pending> toSelf, List<Pending> toOthers)
    {
        var masters = VillageMasters;
        if (!masters.Knows(script))
        {
            SendHtml(session, player, npc, QuestBypassHandler.Unavailable, toSelf);
            return null;
        }

        var who = masters.Actor(PlayerClasses, player.Race, player.ClassId, player.Level);
        var inventory = player.Inventory!;
        var result = evt.Length == 0
            ? masters.Talk(script, npc.NpcId, who)
            : masters.Event(script, npc.NpcId, evt, who, inventory.CountOf);
        if (result == null)
            return null; // Mobius: script returned no page.

        if (result.Change is { } change && !ApplyClassChange(session, player, change, result.RewardItemId, result.RewardCount, toSelf, toOthers))
            return "bad_request";

        if (!_html.TryGet(result.HtmlPath, out var html))
        {
            ZoneLog.Info($"village-master page missing {result.HtmlPath}");
            html = QuestBypassHandler.Unavailable;
        }

        SendHtml(session, player, npc, html, toSelf);
        return null;
    }

    bool ApplyClassChange(ZoneSession session, ZoneEntity player, ClassChangeOption change, int rewardItemId, int rewardCount, List<Pending> toSelf, List<Pending> toOthers)
    {
        var inventory = player.Inventory!;
        if (!PlayerClasses.TryGet(change.ClassId, out var template) || template == null)
            return false;
        int proof = inventory.CountOf(change.ProofItemId);
        if (proof <= 0)
            return false;

        var touches = new List<BagTouch>();
        if (!inventory.TryTake(change.ProofItemId, proof, touches))
            return false;
        if (rewardItemId > 0 && rewardCount > 0 && _game.Items.TryGet(rewardItemId, out _))
        {
            var before = new HashSet<int>(inventory.Items.Select(i => i.ObjectId));
            foreach (var piece in inventory.AddPieces(rewardItemId, rewardCount))
                touches.Add(new BagTouch(before.Contains(piece.ObjectId) ? BagTouchKind.Modify : BagTouchKind.Add, piece));
        }

        int previous = player.ClassId;
        player.ClassId = change.ClassId;
        session.ChangeClass(change.ClassId);
        player.RunSpeed = template.RunSpeed;
        player.WalkSpeed = template.WalkSpeed;
        player.MaxHp = template.ModifiedHp(player.Level);
        player.MaxMp = template.ModifiedMp(player.Level);
        player.CurHp = Math.Min(player.CurHp, player.MaxHp);
        player.CurMp = Math.Min(player.CurMp, player.MaxMp);
        RefreshKnownSkills(player);

        var invUpdate = new InventoryUpdateMsg
        {
            Changes = touches.Select(touch => InventoryMessages.ChangeRow((int)touch.Kind, touch.Item)).ToArray()
        };
        toSelf.Add(Pack(session, MsgType.InventoryUpdate, invUpdate));
        NetBridge?.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(invUpdate));

        Notice(session, ClassTransferMessageId, "Congratulations. You have completed the class transfer.", toSelf);
        toSelf.Add(Pack(session, MsgType.SkillList, SkillListFor(player)));
        NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
        toSelf.Add(SelfUserInfo(session, player));

        // Mobius broadcastUserInfo: peers see the new class.
        byte[] body = MessagePackSerializer.Serialize(CharInfo(player));
        List<int>? netPeers = null;
        foreach (int id in player.Knownlist)
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
            {
                toOthers.Add(new Pending(other.Session, MsgType.CharInfo, body));
                (netPeers ??= []).Add(id);
            }
        }

        if (netPeers != null)
            NetBridge?.FanoutCharInfo(netPeers, player.ObjectId);

        _accounts?.TrySaveClass(session.Account, session.ObjectId, change.ClassId);
        SaveInventory(session, inventory);
        SaveQuestProgress(session, player);
        ZoneLog.Info($"class-change object={session.ObjectId} {previous}->{change.ClassId} proof={change.ProofItemId}x{proof}");
        return true;
    }

    /// <summary>Test hook: run a village-master bypass against a stand-in NPC.</summary>
    internal string? VillageMasterForTest(ZoneSession session, int npcId, string script, string evt, out string html)
    {
        var toSelf = new List<Pending>();
        var toOthers = new List<Pending>();
        html = "";
        lock (_gate)
        {
            if (!TryPlayer(session, out var player))
                return "world";
            var npc = new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = npcId, Name = "Master", ObjectId = 0 };
            var error = VillageMasterBypass(session, player, npc, script, evt, toSelf, toOthers);
            foreach (var p in toSelf)
            {
                if (p.Type == MsgType.NpcHtml)
                    html = MessagePackSerializer.Deserialize<NpcHtmlMsg>(p.Body).Html ?? "";
            }

            return error;
        }
    }
}
