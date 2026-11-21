using Fineage.Game.Economy;
using Fineage.Game.Quests;
using Fineage.Game.Shops;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Block G1 noblesse after Mobius <c>Player.setNoble</c>: the flag is persisted with the class state, the noble skill tree
/// (nobleSkillTree.xml) is granted on every class index without being stored, CharInfo / UserInfo carry it, the clan
/// pledge class floor is 5, and the Olympiad board knows the character. Hero skills (heroSkillTree.xml) follow
/// <see cref="ZoneEntity.Hero"/> on the base class only (Mobius setHero: only when the base class is active).
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius nobleSkillTree: Strider Siege Assault, Build Advanced Headquarters, Wyvern Aegis, Noblesse Blessing,
    /// Summon CP Potion, Fortune / Harmony / Symphony of Noblesse.</summary>
    internal static readonly int[] NobleSkillIds = [325, 326, 327, 1323, 1324, 1325, 1326, 1327];

    /// <summary>Mobius heroSkillTree: Heroic Miracle, Heroic Berserker, Heroic Valor, Heroic Grandeur, Heroic Dread.</summary>
    internal static readonly int[] HeroSkillIds = [395, 396, 1374, 1375, 1376];

    void AddStatusSkills(ZoneEntity entity)
    {
        if (entity.Kind != ZoneEntityKind.Player)
            return;
        if (entity.Noble)
            AddLevelOne(entity, NobleSkillIds);
        if (entity.Hero && !entity.SubClassActive && !_olympiadArena.Contains(entity.ObjectId))
            AddLevelOne(entity, HeroSkillIds);
    }

    void AddLevelOne(ZoneEntity entity, int[] ids)
    {
        foreach (int id in ids)
        {
            if (_game.Skills.Find(id) != null && !entity.KnownSkills.ContainsKey(id))
                entity.KnownSkills[id] = 1;
        }
    }

    void SyncOlympiadNoblesse(ZoneEntity entity)
    {
        // Block G2: a claimed hero of the current era is a hero again on every login (Mobius Hero.isHero).
        if (_olympiad.Board.IsHero(entity.ObjectId))
            entity.Hero = true;
        if (entity.Noble)
        {
            if (!_olympiad.Board.IsNoble(entity.ObjectId))
                _olympiad.Board.GrantNoblesse(entity.ObjectId);
        }
        else if (_olympiad.Board.IsNoble(entity.ObjectId))
            entity.Noble = true; // granted by the Olympiad admin path before G1 persisted the flag
    }

    /// <summary>
    /// Mobius <c>setNoble(true)</c>: flag, noble skills, skill list, UserInfo / CharInfo, clan pledge class, Olympiad.
    /// Persisted through the class state. Returns false when the player is not live.
    /// </summary>
    internal bool SetNoble(ZoneEntity player, bool noble)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Session is not { } session)
            return false;
        if (player.Noble == noble)
            return true;
        player.Noble = noble;
        if (noble)
            _olympiad.Board.GrantNoblesse(player.ObjectId);
        RefreshKnownSkills(player);
        RefreshPledgeClass(player);
        NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
        SelfUserInfo(player);
        var peers = KnownNetPlayersOf(player).Where(id => id != player.ObjectId).ToList();
        if (peers.Count > 0)
            NetBridge?.FanoutCharInfo(peers, player.ObjectId);
        SaveClassState(session, player);
        ZoneLog.Info($"noble object={player.ObjectId} noble={noble}");
        return true;
    }

    /// <summary>Admin / test entry: grant or revoke noblesse on a live character.</summary>
    public bool TrySetNoble(int objectId, bool noble)
    {
        using (_affinity.Step())
            return _byId.TryGetValue(objectId, out var player) && SetNoble(player, noble);
    }

    static bool IsVillageMaster(ZoneEntity npc) =>
        npc.NpcType != null && npc.NpcType.StartsWith("VillageMaster", StringComparison.Ordinal);

    /// <summary>
    /// Interlude retail-style <c>RequestNobless</c>: Q247 at the lake gate (cond 2+), subclass 75+, pay certification, then
    /// <see cref="SetNoble"/> and complete the quest (Lady path stays quest-only in Mobius CT0).
    /// </summary>
    string? RequestNoblessBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!IsVillageMaster(npc))
            return "bad_request";
        if (player.Noble)
        {
            SendHtml(session, player, npc,
                "<html><body>%npcname%:<br>You are already a Noblesse.</body></html>");
            return null;
        }

        var gate = player.Quests.Find(NoblesseGrant.GateQuestId);
        if (gate is not { Phase: QuestPhase.Started } || gate.Cond < NoblesseGrant.GateReadyCond)
        {
            SendHtml(session, player, npc,
                "<html><body>%npcname%:<br>Complete the Noblesse gate quest before I can certify you.</body></html>");
            return null;
        }

        if (!player.SubClassActive || player.Level < SubclassMinLevel)
        {
            SendHtml(session, player, npc,
                "<html><body>%npcname%:<br>Only a subclass at level 75 or above may become Noblesse.</body></html>");
            return null;
        }

        var quest = _game.Quests.Find(NoblesseGrant.GateQuestId);
        if (!NoblesseGrant.TryReadVillageMasterCosts(quest, out var costs))
            return "bad_request";

        var inventory = player.Inventory!;
        if (player.Sp < costs.Sp)
        {
            Notice(session, 278, "You do not have enough SP to learn this skill.");
            return null;
        }

        if (inventory.CountOf(ShopTrade.AdenaItemId) < costs.Adena)
        {
            Notice(session, IncorrectItemCountMessageId, "Incorrect item count.");
            return null;
        }

        if (!RecordBagChange(
                player,
                LedgerTxKind.QuestReward,
                LedgerAccount.Of(SystemAccount.Quests),
                () =>
                {
                    var touches = new List<BagTouch>();
                    if (!inventory.TryTake(ShopTrade.AdenaItemId, costs.Adena, touches))
                        return false;
                    var before = new HashSet<int>(inventory.Items.Select(i => i.ObjectId));
                    foreach (var piece in inventory.AddPieces(costs.TiaraItemId, 1))
                        touches.Add(new BagTouch(before.Contains(piece.ObjectId) ? BagTouchKind.Modify : BagTouchKind.Add, piece));
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
                _ => NoblesseGrant.GateQuestId))
            return "bad_request";

        player.Sp -= costs.Sp;
        _persist.SaveProgress(session.Account, session.ObjectId, player.Level, player.Exp, player.Sp, player.MaxHp, player.CurHp,
            player.MaxMp, player.CurMp, player.MaxCp, player.CurCp);
        if (costs.RewardExp > 0)
            ApplyExpSp(player, costs.RewardExp, 0);

        if (!SetNoble(player, true))
            return "bad_request";

        gate.Phase = QuestPhase.Completed;
        gate.ClearVars();
        SaveQuestProgress(session, player);
        Notice(session, (int)QuestNotice.Completed, $"{quest!.Name} is complete.");
        NetBridge?.FanoutSocialAction(KnownNetPlayersOf(player), player.ObjectId, 3);
        SendHtml(session, player, npc,
            "<html><body>%npcname%:<br>Congratulations. You are now a Noblesse.</body></html>");
        return null;
    }

    void SendNoblesPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, bool noble)
    {
        string file = noble ? "teleporter/nobles_select.htm" : "teleporter/not_nobles.htm";
        SendHtml(session, player, npc, TryPage(session.Language, file, out var html)
            ? html
            : "<html><body>%npcname%:<br>" + NpcServiceProtocol.NoblesOnlyText + "</body></html>");
    }

    void RefreshPledgeClass(ZoneEntity player)
    {
        if (player.ClanId == 0)
        {
            player.PledgeClass = PledgeClassOf(null, player.ObjectId, 0, player.Hero);
            return;
        }
        var clan = ClanOf(player.ObjectId);
        if (clan == null)
            return;
        var member = clan.Members.FirstOrDefault(m => m.CharacterId == player.ObjectId);
        player.PledgeClass = PledgeClassOf(clan, player.ObjectId, member?.PledgeType ?? 0, player.Hero);
    }
}
