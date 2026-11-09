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
    void GrantKillReward(ZoneEntity killer, ZoneEntity npc)
    {
        if (killer.Kind != ZoneEntityKind.Player || killer.Session == null || npc.Kind != ZoneEntityKind.Npc)
            return;
        ApplyQuestKill(killer, npc);
        DistributeKillReward(npc);
    }

    /// <summary>
    /// Mobius <c>Attackable.calculateRewards</c>: exp/SP come from the monster template and are shared by the damage
    /// share (not paid to the last hit). Zero outside <c>MonsterExpMaxLevelDifference</c>.
    /// </summary>
    void DistributeKillReward(ZoneEntity npc)
    {
        long rewardExp = npc.RewardExp;
        int rewardSp = npc.RewardSp;
        if (rewardExp <= 0 && rewardSp <= 0)
            return;

        // Mobius: damage above 1 counts, and the attacker has to be in ALT_PARTY_RANGE of the corpse.
        var damage = new Dictionary<int, long>();
        long totalDamage = 0;
        foreach (var (id, dealt) in npc.DamageBy)
        {
            if (dealt <= 1 || !_byId.TryGetValue(id, out var attacker) || attacker.Kind != ZoneEntityKind.Player ||
                attacker.Session == null || attacker.Dead)
                continue;
            if (!ZoneCombat.Within(npc.X, npc.Y, attacker.X, attacker.Y, QuestPartyRange))
                continue;
            damage[id] = dealt;
            totalDamage += dealt;
        }

        if (totalDamage <= 0)
            return;

        int maxLevelDifference = ZoneRewardSettings.MonsterExpMaxLevelDifference;
        var settled = new HashSet<int>();
        foreach (var (id, dealt) in damage)
        {
            if (settled.Contains(id) || !_byId.TryGetValue(id, out var attacker) || attacker.Session == null)
                continue;

            if (_partyByPlayer.TryGetValue(id, out int partyId) && _parties.TryGetValue(partyId, out var party))
            {
                SharePartyKill(party, npc, damage, totalDamage, rewardExp, rewardSp, maxLevelDifference, settled);
                continue;
            }

            settled.Add(id);
            var share = KillRewardFormula.CalculateExpAndSp(
                attacker.Level, npc.Level, rewardExp, rewardSp, dealt, totalDamage, maxLevelDifference);
            SendKillReward(attacker.Session, attacker, share.Exp, share.Sp);
        }
    }

    /// <summary>Mobius <c>Party.distributeXpAndSp</c> over the members of one party that took part in the kill.</summary>
    void SharePartyKill(
        ZoneParty party,
        ZoneEntity npc,
        Dictionary<int, long> damage,
        long totalDamage,
        long rewardExp,
        int rewardSp,
        int maxLevelDifference,
        HashSet<int> settled)
    {
        var members = new List<ZoneEntity>(party.MemberIds.Count);
        long partyDamage = 0;
        int partyLevel = 0;
        foreach (int memberId in party.MemberIds)
        {
            if (!_byId.TryGetValue(memberId, out var member) || member.Kind != ZoneEntityKind.Player ||
                member.Session == null || member.Dead)
                continue;
            if (!ZoneCombat.Within(npc.X, npc.Y, member.X, member.Y, QuestPartyRange))
                continue;
            members.Add(member);
            settled.Add(memberId);
            partyDamage += damage.GetValueOrDefault(memberId);
            partyLevel = Math.Max(partyLevel, member.Level);
        }

        if (members.Count == 0 || partyDamage <= 0)
            return;

        // Mobius PartyXpCutoffMethod = level: members more than PartyXpCutoffLevel below the top level earn nothing.
        var valid = members.Where(m => partyLevel - m.Level <= KillRewardFormula.PartyXpCutoffLevel).ToList();
        if (valid.Count == 0)
            return;

        double partyMul = partyDamage < totalDamage ? (double)partyDamage / totalDamage : 1;
        var pool = KillRewardFormula.CalculateExpAndSp(
            partyLevel, npc.Level, rewardExp, rewardSp, partyDamage, totalDamage, maxLevelDifference);
        double bonus = KillRewardFormula.PartyBonus(valid.Count);
        double expPool = pool.Exp * partyMul * bonus;
        double spPool = pool.Sp * partyMul * bonus;

        long sqLevelSum = 0;
        foreach (var member in valid)
            sqLevelSum += (long)member.Level * member.Level;
        if (sqLevelSum <= 0)
            return;

        foreach (var member in valid)
        {
            double share = (double)((long)member.Level * member.Level) / sqLevelSum;
            SendKillReward(member.Session!, member, expPool * share, spPool * share);
        }
    }

    /// <summary>
    /// Pays one player's share: exp/SP into the counter, SystemMessage 95, StatusUpdate and UserInfo, save, then the
    /// level-up packets when the exp crossed a row.
    /// </summary>
    void SendKillReward(ZoneSession session, ZoneEntity player, double rawExp, double rawSp)
    {
        long exp = KillRewardFormula.Round(rawExp);
        int sp = (int)Math.Min(int.MaxValue, KillRewardFormula.Round(rawSp));
        if (exp <= 0 && sp <= 0)
            return;

        bool levelled = ApplyExpSp(player, exp, sp);
        var notice = new SystemMessageMsg
        {
            Id = KillRewardMessageId,
            Params = [exp.ToString(System.Globalization.CultureInfo.InvariantCulture), sp.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Text = $"You have earned {exp} experience and {sp} SP."
        };
        // StatusUpdate carries live Exp/Sp/Level; UserInfo refreshes the full sheet (vitals + template stats).
        var status = new StatusUpdateMsg
        {
            ObjectId = player.ObjectId,
            AttributeIds =
            [
                InterludeStatus.Level,
                InterludeStatus.Exp,
                InterludeStatus.Sp,
                InterludeStatus.CurHp,
                InterludeStatus.MaxHp,
                InterludeStatus.CurMp,
                InterludeStatus.MaxMp
            ],
            Values =
            [
                player.Level,
                (int)Math.Min(int.MaxValue, player.Exp),
                player.Sp,
                player.CurHp,
                player.MaxHp,
                player.CurMp,
                player.MaxMp
            ]
        };
        SelfUserInfo(player);

        if (NetBridge != null)
        {
            int[] self = [player.ObjectId];
            var vitals = BuildUserInfoVitals(player);
            NetBridge.FanoutSystemMessage(self, new SystemMessageMsg
            {
                Id = notice.Id,
                Text = notice.Text,
                Params = notice.Params ?? []
            });
            NetBridge.FanoutStatusUpdate(self, status);
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
                Cp = vitals.CurCp,
                MaxCp = vitals.MaxCp
            });
        }

        SaveQuestProgress(session, player);

        // Mobius addExpAndSp → addLevel: level up after the exp notice so the client sees "earned exp", then level up.
        if (levelled)
            ApplyLevelUp(session, player);
    }

    internal const int KillRewardMessageId = 95;

    void SaveQuestProgress(ZoneSession session, ZoneEntity entity) =>
        _persist.SaveQuestProgress(session.Account, session.ObjectId, entity);

    bool TryPlayer(ZoneSession session, out ZoneEntity player)
    {
        if (_byId.TryGetValue(session.ObjectId, out player!) &&
            player.Kind == ZoneEntityKind.Player &&
            player.Inventory != null)
            return true;
        player = null!;
        return false;
    }

    bool InTalkRange(ZoneEntity player, ZoneEntity npc) =>
        NpcHtmlProtocol.InRange(player.X, player.Y, player.Z, npc.X, npc.Y, npc.Z, _game.Quests.TalkRange);

    static bool EventOk(string text)
    {
        foreach (char c in text)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
                return false;
        }

        return true;
    }

    static string FailureCode(QuestFailure failure) => failure switch
    {
        QuestFailure.Unknown => QuestProtocol.Unknown,
        QuestFailure.Limit => QuestProtocol.Limit,
        QuestFailure.State => QuestProtocol.State,
        QuestFailure.Abort => QuestProtocol.Abort,
        QuestFailure.Item => QuestProtocol.Item,
        QuestFailure.BadRequest => "bad_request",
        _ => "bad_request"
    };
}
