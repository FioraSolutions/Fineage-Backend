using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.Rewards;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius SystemMessageId 96 <c>YOUR_LEVEL_HAS_INCREASED</c>.</summary>
    internal const int LevelUpMessageId = 96;

    /// <summary>Mobius <c>SocialAction.LEVEL_UP</c> (interlude id 15).</summary>
    internal const int SocialLevelUp = 15;

    /// <summary>
    /// Mobius <c>PlayerStat.addLevel</c>: <c>rewardSkills()</c> (auto-learn the rows the new level unlocked, AutoLearnSkills
    /// is on in Interlude), <c>broadcastPacket(SocialAction.LEVEL_UP)</c>, <c>YOUR_LEVEL_HAS_INCREASED</c>,
    /// <c>refreshOverloaded()</c> + <c>refreshExpertisePenalty()</c>, then a LEVEL/MAX_CP/MAX_HP/MAX_MP StatusUpdate and a
    /// full UserInfo. The new SkillList is what puts the freshly learned skill on the client skill window — without it the
    /// character levels up and still has the old book.
    /// </summary>
    void ApplyLevelUp(ZoneSession session, ZoneEntity player)
    {
        // rewardSkills: rebuild the book from the tree at the new level. RefreshRunSpeed inside also folds any
        // passive mastery the new rows brought (Quick Step / Agile Movement), so the live speed is correct.
        RefreshKnownSkills(player);
        RefreshGradePenalty(player);      // refreshExpertisePenalty
        RefreshWeightPenalty(player);     // refreshOverloaded (max load grew with the level)

        // Mobius broadcastPacket(new SocialAction(this, LEVEL_UP)): the gesture goes to self and every peer in view.
        FanoutSocial(player, SocialLevelUp);

        var status = new StatusUpdateMsg
        {
            ObjectId = player.ObjectId,
            AttributeIds =
            [
                InterludeStatus.Level,
                InterludeStatus.MaxCp,
                InterludeStatus.MaxHp,
                InterludeStatus.MaxMp
            ],
            Values =
            [
                player.Level,
                BuildUserInfoVitals(player).MaxCp,
                player.MaxHp,
                player.MaxMp
            ]
        };
        SelfUserInfo(player);

        if (NetBridge != null)
        {
            int[] self = [player.ObjectId];
            NetBridge.FanoutSystemMessage(self, new SystemMessageMsg
            {
                Id = LevelUpMessageId,
                Text = "Your level has increased!",
                Params = []
            });
            NetBridge.FanoutStatusUpdate(self, status);
            NetBridge.FanoutSkillList(self,
                Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
            NetBridge.FanoutPlayerStats(self, new Fineage.Protocol.PlayerStatsMsg
            {
                ObjectId = player.ObjectId,
                Level = player.Level,
                Exp = player.Exp,
                Sp = player.Sp,
                Hp = player.CurHp,
                MaxHp = player.MaxHp,
                Mp = player.CurMp,
                MaxMp = player.MaxMp,
                Cp = BuildUserInfoVitals(player).CurCp,
                MaxCp = BuildUserInfoVitals(player).MaxCp
            });
        }
    }

    void QueueList(ZoneSession session)
    {
        if (!TryPlayer(session, out var player))
            return;
        var list = _quests.List(player.Quests);
        var active = list.Active.Select(entry => new QuestJournalEntryMsg
        {
            QuestId = entry.QuestId,
            Name = entry.Name,
            State = entry.State,
            Cond = entry.Cond,
            ProgressFlags = entry.ProgressFlags,
            JournalWord = entry.JournalWord,
            Repeatable = entry.Repeatable
        }).ToArray();
        if (NetBridge != null)
        {
            NetBridge.FanoutQuestList([session.ObjectId], new QuestListMsg
            {
                Active = active,
                CompletedMask = list.CompletedMask ?? [],
                CompletedQuestIds = list.CompletedQuestIds ?? [],
                MaxActive = list.MaxActive
            });
        }
    }

    /// <summary>
    /// Quest journal rows for the live player, for the Net enter-world burst. Mobius sends its journal on the
    /// first <c>RequestQuestList</c>; the Net client shows the window on the menu button, so sending it with the
    /// rest of the enter packet leaves the tabs filled even if the player opens the window before the reply lands.
    /// </summary>
    internal QuestListMsg NetQuestList(ZoneSession session)
    {
        lock (_gate)
        {
            if (!TryPlayer(session, out var player))
                return new QuestListMsg();

            var list = _quests.List(player.Quests);
            return new QuestListMsg
            {
                Active = list.Active.Select(entry => new QuestJournalEntryMsg
                {
                    QuestId = entry.QuestId,
                    Name = entry.Name ?? "",
                    State = entry.State,
                    Cond = entry.Cond,
                    ProgressFlags = entry.ProgressFlags,
                    JournalWord = entry.JournalWord,
                    Repeatable = entry.Repeatable
                }).ToArray(),
                CompletedMask = list.CompletedMask ?? [],
                CompletedQuestIds = list.CompletedQuestIds ?? [],
                MaxActive = list.MaxActive
            };
        }
    }

    void LoadQuestProgress(ZoneSession session, ZoneEntity entity, CharacterRecord? stored = null)
    {
        entity.Quests = new QuestJournal();
        if (stored == null)
            return;
        entity.Exp = stored.Exp;
        entity.Sp = stored.Sp;
        entity.Quests = QuestJournal.Load(stored.Quests);
    }

    /// <summary>
    /// Mobius <c>NewbieHelper.onFirstTalk</c>: one-time beginner kit under level 18.
    /// Does not open its own HTML — the guide page still comes from <see cref="SendNpcPage"/>.
    /// </summary>
    void TryNewbieHelperGift(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        if (!NewbieHelperGift.IsGuide(npc.NpcId))
            return;
        if (player.Level >= NewbieHelperGift.MaxLevelExclusive)
            return;
        if (player.Inventory == null)
            return;

        var prior = player.Quests.Find(NewbieHelperGift.QuestId);
        if (prior != null && prior.Phase == QuestPhase.Completed)
            return;

        if (!_game.Items.TryGet(NewbieHelperGift.ScrollRecovery, out _) ||
            !_game.Items.TryGet(NewbieHelperGift.TravelToken, out _) ||
            !_game.Items.TryGet(NewbieHelperGift.SoulshotNovice, out _) ||
            !_game.Items.TryGet(NewbieHelperGift.SpiritshotNovice, out _))
        {
            ZoneLog.Info($"newbie-helper-gift skipped — missing item templates (npc={npc.NpcId})");
            return;
        }

        var (shotId, shotCount) = NewbieHelperGift.ShotForClass(_newbieBuffs, player.ClassId);
        var changes = new List<InventoryUpdateEntryMsg>(3);
        void Grant(int itemId, int count)
        {
            var added = player.Inventory.Add(itemId, count);
            changes.Add(InventoryMessages.ChangeRow(InventoryChange.Add, added));
        }

        Grant(shotId, shotCount);
        Grant(NewbieHelperGift.TravelToken, NewbieHelperGift.TokenCount);
        Grant(NewbieHelperGift.ScrollRecovery, NewbieHelperGift.ScrollCount);

        player.Quests.Add(NewbieHelperGift.QuestId, QuestPhase.Completed);
        var update = new InventoryUpdateMsg { Changes = changes.ToArray() };
        // Net clients only learn about the kit from the bridge; without this they get the page but never the items.
        NetBridge?.FanoutInventoryUpdate([session.ObjectId], update);
        SaveInventory(session, player.Inventory);
        SaveQuestProgress(session, player);
        QueueList(session);
        ZoneLog.Info(
            $"newbie-helper-gift object={session.ObjectId} npc={npc.NpcId} shot={shotId}x{shotCount}");
    }

    /// <summary>
    /// Mobius-style kill reward. <c>Attackable.calculateRewards</c> splits the template exp/SP between every attacker
    /// inside <c>ALT_PARTY_RANGE</c> by its share of the damage; a party pools its damage, takes the
    /// <c>Party.BONUS_EXP_SP</c> bonus and divides the pool by level². Each rewarded player then gets
    /// SystemMessage 95, a StatusUpdate and a full UserInfo.
    /// </summary>
}
