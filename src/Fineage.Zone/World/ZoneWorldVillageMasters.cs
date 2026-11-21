using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Quests;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;

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

    string? VillageMasterBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string script, string evt)
    {
        if (string.Equals(script, NewbieTravelToken.Script, StringComparison.OrdinalIgnoreCase) && _newbieBuffs.TravelToken is { } travel)
            return NewbieTravel(session, player, npc, travel, evt);

        if (TryClanMasterScript(session, player, npc, script, evt))
            return null;

        var masters = VillageMasters;
        if (!masters.Knows(script))
        {
            LogNpcScriptMiss(script, npc.NpcId, evt, "not listed in village-masters.json");
            SendHtml(session, player, npc, QuestBypassHandler.Unavailable);
            return null;
        }

        var who = masters.Actor(PlayerClasses, player.Race, player.ClassId, player.Level);
        var inventory = player.Inventory!;
        var result = evt.Length == 0
            ? masters.Talk(script, npc.NpcId, who)
            : masters.Event(script, npc.NpcId, evt, who, inventory.CountOf);
        if (result == null)
        {
            LogNpcScriptMiss(script, npc.NpcId, evt, "village_master script returned no page for this NPC");
            SendHtml(session, player, npc, QuestBypassHandler.Unavailable);
            return null;
        }

        if (result.Change is { } change && !ApplyClassChange(session, player, change, result.RewardItemId, result.RewardCount))
            return "bad_request";

        if (!TryPage(session.Language, result.HtmlPath, out var html))
        {
            ZoneLog.Info($"village-master page missing {result.HtmlPath}");
            html = QuestBypassHandler.Unavailable;
        }

        SendHtml(session, player, npc, html);
        return null;
    }

    /// <summary>Mobius SystemMessageId INCORRECT_ITEM_COUNT (NewbieTravelToken without a token).</summary>
    internal const int IncorrectItemCountMessageId = 351;

    /// <summary>
    /// Mobius <c>NewbieTravelToken</c>: talk shows <c>&lt;guide&gt;.htm</c> (or <c>cant-travel.htm</c> at level 20+); a guide id event
    /// takes one Newbie Travel Token and teleports to that village. Destinations outside the loaded maps are refused before the token is taken.
    /// </summary>
    string? NewbieTravel(ZoneSession session, ZoneEntity player, ZoneEntity npc, NewbieTravelToken travel, string evt)
    {
        string Page(string file) => TryPage(session.Language, $"newbie_travel/{file}", out var h) ? h : QuestBypassHandler.Unavailable;
        if (evt.Length == 0)
        {
            if (!travel.Destinations.ContainsKey(npc.NpcId))
                return null;
            SendHtml(session, player, npc, Page(player.Level >= travel.RefusedLevel ? "cant-travel.htm" : $"{npc.NpcId}.htm"));
            return null;
        }

        if (!int.TryParse(evt, out int target) || !travel.Destinations.TryGetValue(target, out var loc))
            return null;
        if (player.Level >= travel.RefusedLevel)
        {
            SendHtml(session, player, npc, Page("cant-travel.htm"));
            return null;
        }

        var inventory = player.Inventory!;
        if (inventory.CountOf(travel.ItemId) <= 0)
        {
            Notice(session, IncorrectItemCountMessageId, "Incorrect item count.");
            return null;
        }

        var (x, y) = Offset(player, loc.X, loc.Y);
        if (!Loaded(x, y))
        {
            Notice(session, NpcServiceProtocol.DestinationUnloaded, NpcServiceProtocol.DestinationUnloadedText);
            SendHtml(session, player, npc, "<html><body>%npcname%:<br>That destination is not on a loaded map.</body></html>");
            return null;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        if (!Take(inventory, travel.ItemId, 1, changes))
            return "bad_request";
        FanInventory(session, Collapse(changes));
        SaveInventory(session, inventory);
        Relocate(player, x, y, loc.Z);
        return null;
    }

    bool ApplyClassChange(ZoneSession session, ZoneEntity player, ClassChangeOption change, int rewardItemId, int rewardCount)
    {
        var inventory = player.Inventory!;
        if (!PlayerClasses.TryGet(change.ClassId, out var template) || template == null)
            return false;
        foreach (int itemId in change.ProofItemIds)
        {
            if (inventory.CountOf(itemId) <= 0)
                return false;
        }

        int classId = change.ClassId;
        if (!RecordBagChange(
                player,
                LedgerTxKind.ClassChange,
                Fees,
                () =>
                {
                    var touches = new List<BagTouch>();
                    foreach (int itemId in change.ProofItemIds)
                    {
                        int have = inventory.CountOf(itemId);
                        if (!inventory.TryTake(itemId, have, touches))
                            return false;
                    }

                    if (rewardItemId > 0 && rewardCount > 0 && _game.Items.TryGet(rewardItemId, out _))
                    {
                        var before = new HashSet<int>(inventory.Items.Select(i => i.ObjectId));
                        foreach (var piece in inventory.AddPieces(rewardItemId, rewardCount))
                            touches.Add(new BagTouch(before.Contains(piece.ObjectId) ? BagTouchKind.Modify : BagTouchKind.Add, piece));
                    }

                    if (touches.Count > 0)
                    {
                        var invUpdate = new InventoryUpdateMsg
                        {
                            Changes = touches.Select(touch => InventoryMessages.ChangeRow((int)touch.Kind, touch.Item)).ToArray()
                        };
                        NetBridge?.FanoutInventoryUpdate([session.ObjectId], invUpdate);
                        SaveInventory(session, inventory);
                    }

                    return true;
                },
                _ => classId))
            return false;

        int previous = player.ClassId;
        player.ClassId = change.ClassId;
        // Mobius setClassId: on the base class it is also setBaseClass; on a subclass only that slot changes.
        bool trackBase = !player.SubClassActive && player.Identity is { BaseClassId: > 0 };
        if (trackBase)
            player.BaseClassId = change.ClassId;
        session.ChangeClass(change.ClassId);
        GraduateFromAcademy(session, player);
        player.BaseRunSpeed = template.RunSpeed;
        player.BaseWalkSpeed = template.WalkSpeed;
        player.Dex = template.Dex;
        player.MaxHp = template.ModifiedHp(player.Level);
        player.MaxMp = template.ModifiedMp(player.Level);
        player.CurHp = Math.Min(player.CurHp, player.MaxHp);
        player.CurMp = Math.Min(player.CurMp, player.MaxMp);
        RefreshKnownSkills(player); // also folds passive runSpd (Quick Step / Agile Movement) into the live speed
        FanoutNetMoveType(player);
        FanoutMoveType(player);

        Notice(session, ClassTransferMessageId, "Congratulations. You have completed the class transfer.");
        NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
        SelfUserInfo(player);
        NetBridge?.FanoutMagicSkillUse(KnownNetPlayersOf(player), new MagicSkillUseMsg
        {
            CasterObjectId = player.ObjectId,
            TargetObjectId = player.ObjectId,
            SkillId = ClassChangeAnimationSkillId,
            SkillLevel = 1
        });

        // Mobius broadcastUserInfo: peers see the new class.
        List<int>? netPeers = null;
        foreach (int id in player.Knownlist)
        {
            if (_byId.TryGetValue(id, out var other) && other.Session != null)
                (netPeers ??= []).Add(id);
        }

        if (netPeers != null)
            NetBridge?.FanoutCharInfo(netPeers, player.ObjectId);

        _persist.SaveClass(session.Account, session.ObjectId, change.ClassId);
        if (trackBase || player.SubClassActive)
            SaveClassState(session, player);
        SaveQuestProgress(session, player);
        ZoneLog.Info($"class-change object={session.ObjectId} {previous}->{change.ClassId} " +
                     $"proof={string.Join(",", change.ProofItemIds)}");
        return true;
    }

    /// <summary>Test hook: run a village-master bypass against a stand-in NPC.</summary>
    /// <summary>Last HTML rendered by <see cref="SendHtml"/>: the test seam that replaced reading a V2 frame.</summary>
    internal string? LastNpcHtmlForTest { get; private set; }

    internal string? VillageMasterForTest(ZoneSession session, int npcId, string script, string evt, out string html)
    {
        html = "";
        using (_affinity.Step())
        {
            if (!TryPlayer(session, out var player))
                return "world";
            var npc = new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = npcId, Name = "Master", ObjectId = 0 };
            var error = VillageMasterBypass(session, player, npc, script, evt);
            html = LastNpcHtmlForTest ?? "";
            return error;
        }
    }
}
