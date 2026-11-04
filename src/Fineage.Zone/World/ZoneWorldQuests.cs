using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryQuestListAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        lock (_gate)
            QueueList(session, toSelf);
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryQuestAbortAsync(ZoneSession session, int questId, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        if (questId <= 0)
            return "bad_request";

        QuestOutcome outcome;
        var toSelf = new List<Pending>();
        lock (_gate)
        {
            if (!TryPlayer(session, out var player))
                return "world";
            outcome = _quests.Abort(player.Quests, player.Inventory!, questId);
            if (outcome.Ok || outcome.Lines.Count > 0)
                Publish(session, player, outcome, npcObjectId: 0, npcId: 0, toSelf);
        }

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return outcome.Ok ? null : FailureCode(outcome.Failure);
    }

    public async Task<string?> TryNpcTalkAsync(ZoneSession session, int npcObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        if (npcObjectId <= 0)
            return "bad_request";

        string? error;
        var toSelf = new List<Pending>();
        lock (_gate)
            error = Talk(session, npcObjectId, questId: 0, eventName: null, toSelf, out _);
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return error;
    }

    public async Task<string?> TryQuestEventAsync(ZoneSession session, int npcObjectId, int questId, string? eventName, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        if (npcObjectId <= 0 || questId <= 0)
            return "bad_request";
        eventName ??= "";
        if (eventName.Length > 64 || !EventOk(eventName))
            return "bad_request";

        string? error;
        var toSelf = new List<Pending>();
        lock (_gate)
            error = Talk(session, npcObjectId, questId, eventName, toSelf, out _);
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return error;
    }

    void BootstrapQuestNpcs()
    {
        var templates = NpcCatalog.LoadDefault();
        foreach (var npc in TalkingIsland.Villagers)
        {
            if (_byId.ContainsKey(npc.ObjectId))
                throw new InvalidOperationException($"Quest npc object id {npc.ObjectId} is already used.");

            templates.ById.TryGetValue(npc.NpcId, out var template);
            var entity = new ZoneEntity
            {
                ObjectId = npc.ObjectId,
                Kind = ZoneEntityKind.Npc,
                Name = npc.Name,
                Title = template?.Title ?? "",
                NpcType = template?.Type ?? "",
                X = npc.X,
                Y = npc.Y,
                Z = npc.Z,
                Heading = npc.Heading,
                NpcId = npc.NpcId,
                Attackable = false,
                MaxHp = 100,
                CurHp = 100,
                RunSpeed = npc.RunSpeed,
                WalkSpeed = npc.WalkSpeed
            };
            _byId.Add(entity.ObjectId, entity);
            _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        }
    }

    string? Talk(ZoneSession session, int npcObjectId, int questId, string? eventName, List<Pending> toSelf, out QuestOutcome? outcome)
    {
        outcome = null;
        if (!TryPlayer(session, out var player))
            return "world";
        if (!_byId.TryGetValue(npcObjectId, out var npc) || npc.Kind != ZoneEntityKind.Npc)
            return QuestProtocol.Npc;
        if (npc.Attackable)
            return NpcHtmlProtocol.Attackable;
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (!InTalkRange(player, npc))
            return "range";

        // Mobius NewbieHelper onFirstTalk — before guide HTML / other quest hooks.
        if (eventName == null)
            TryNewbieHelperGift(session, player, npc, toSelf);

        outcome = eventName == null
            ? _quests.FirstTalk(player.Quests, player.Inventory!, npc.NpcId, npc.Name, QuestActorOf(player))
            : eventName.Length == 0
                ? _quests.Talk(player.Quests, player.Inventory!, questId, npc.NpcId, QuestActorOf(player))
                : _quests.AdvEvent(player.Quests, player.Inventory!, questId, npc.NpcId, eventName, QuestActorOf(player));
        if (IsNpcPage(outcome))
        {
            SendNpcPage(session, player, npc, 0, toSelf);
            return null;
        }

        if (outcome.Ok || outcome.Lines.Count > 0 || outcome.JournalChanged)
            Publish(session, player, outcome, npc.ObjectId, npc.NpcId, toSelf);
        return outcome.Ok ? null : FailureCode(outcome.Failure);
    }

    static QuestActor QuestActorOf(ZoneEntity player) => new(player.Race, player.Level, player.ClassId);

    /// <summary>
    /// Mobius <c>Quest.onKill</c> for the killer (killer-only steps) plus <c>party: true</c> steps shared to one random eligible member. Quest drops go straight to the
    /// bag like <c>giveItemRandomly</c>; journal / bag / sounds go out through the same Publish path as talk.
    /// </summary>
    internal void QuestKillForTest(ZoneEntity killer, int npcId)
    {
        var outbound = new List<Pending>();
        lock (_gate)
            ApplyQuestKill(killer, new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = npcId, ObjectId = 0 }, outbound);
    }

    /// <summary>Test hook: run the exp/sp → level-up pipeline the way a kill reward does.</summary>
    internal void GrantExpForTest(ZoneEntity killer, long exp, int sp)
    {
        var outbound = new List<Pending>();
        lock (_gate)
        {
            if (!ApplyExpSp(killer, exp, sp))
                return;
            if (killer.Session != null)
                ApplyLevelUp(killer.Session, killer, outbound);
        }
    }

    void ApplyQuestKill(ZoneEntity killer, ZoneEntity npc, List<Pending> outbound)
    {
        if (killer.Inventory == null || killer.Dead || killer.Session == null)
            return;
        var outcome = _quests.OnKill(killer.Quests, killer.Inventory, npc.NpcId, QuestActorOf(killer));
        PublishKill(killer, outcome, npc, outbound);

        // Mobius getRandomPartyMember(State): one random eligible member (killer included) within ALT_PARTY_RANGE.
        foreach (int questId in _quests.PartyKillQuests(npc.NpcId))
        {
            var eligible = new List<ZoneEntity>();
            foreach (var member in QuestPartyCandidates(killer))
            {
                if (member.Inventory != null && !member.Dead && member.Session != null &&
                    _quests.PartyKillApplies(member.Quests, member.Inventory, questId, npc.NpcId, QuestActorOf(member)))
                    eligible.Add(member);
            }

            if (eligible.Count == 0)
                continue;
            var winner = eligible[Random.Shared.Next(eligible.Count)];
            PublishKill(winner, _quests.OnPartyKill(winner.Quests, winner.Inventory!, questId, npc.NpcId, QuestActorOf(winner)), npc, outbound);
        }
    }

    /// <summary>L2J/Mobius <c>Config.ALT_PARTY_RANGE</c> default (Character.ini AltPartyRange = 1600).</summary>
    public const int QuestPartyRange = 1600;

    IEnumerable<ZoneEntity> QuestPartyCandidates(ZoneEntity killer)
    {
        yield return killer;
        if (!_partyByPlayer.TryGetValue(killer.ObjectId, out int partyId) || !_parties.TryGetValue(partyId, out var party))
            yield break;
        foreach (int id in party.MemberIds)
        {
            if (id == killer.ObjectId || !_byId.TryGetValue(id, out var member) || member.Kind != ZoneEntityKind.Player)
                continue;
            long dx = member.X - killer.X, dy = member.Y - killer.Y;
            if (dx * dx + dy * dy <= (long)QuestPartyRange * QuestPartyRange)
                yield return member;
        }
    }

    void PublishKill(ZoneEntity player, QuestOutcome outcome, ZoneEntity npc, List<Pending> outbound)
    {
        if (outcome.QuestId == 0 || !outcome.Ok || player.Session == null)
            return;
        if (outcome.Items.Count == 0 && !outcome.JournalChanged && outcome.Sounds.Count == 0)
            return;
        Publish(player.Session, player, outcome, npc.ObjectId, npc.NpcId, outbound);
    }

    internal void JoinPartyForTest(params ZoneEntity[] members)
    {
        lock (_gate)
        {
            var party = new ZoneParty { PartyId = _nextPartyId++, LeaderObjectId = members[0].ObjectId };
            _parties[party.PartyId] = party;
            foreach (var m in members)
            {
                party.MemberIds.Add(m.ObjectId);
                _partyByPlayer[m.ObjectId] = party.PartyId;
            }
        }
    }

    static bool IsNpcPage(QuestOutcome outcome) =>
        outcome.Ok
        && outcome.Hook == "default"
        && outcome.QuestId == 0
        && !outcome.JournalChanged
        && outcome.Lines.Count == 0
        && outcome.Items.Count == 0
        && outcome.Sounds.Count == 0
        && outcome.Exp == 0
        && outcome.Sp == 0;

    void Publish(ZoneSession session, ZoneEntity player, QuestOutcome outcome, int npcObjectId, int npcId, List<Pending> toSelf)
    {
        bool levelled = ApplyProgress(player, outcome);
        if (outcome.Items.Count > 0 && player.Inventory != null)
        {
            var invUpdate = new InventoryUpdateMsg
            {
                Changes = outcome.Items.Select(touch => InventoryMessages.ChangeRow((int)touch.Kind, touch.Item)).ToArray()
            };
            toSelf.Add(Pack(session, MsgType.InventoryUpdate, invUpdate));
            if (NetBridge != null)
                NetBridge.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(invUpdate));
            SaveInventory(session, player.Inventory);
        }

        foreach (var line in outcome.Lines)
        {
            var notice = new SystemMessageMsg
            {
                Id = (int)line.Notice,
                Params = line.Params,
                Text = line.Text
            };
            toSelf.Add(Pack(session, MsgType.SystemMessage, notice));
            if (NetBridge != null)
            {
                NetBridge.FanoutSystemMessage([session.ObjectId], new Fineage.Net.Contract.SystemMessageNetMsg
                {
                    Id = notice.Id,
                    Text = notice.Text ?? "",
                    Params = notice.Params ?? []
                });
            }
        }

        foreach (string sound in outcome.Sounds)
            toSelf.Add(Pack(session, MsgType.QuestSound, new QuestSoundMsg { Name = sound }));

        if (outcome.Html.Length > 0)
        {
            toSelf.Add(Pack(session, MsgType.QuestDialog, new QuestDialogMsg
            {
                NpcObjectId = npcObjectId,
                NpcId = npcId,
                QuestId = outcome.QuestId,
                Hook = outcome.Hook,
                Event = outcome.Event,
                Html = outcome.Html
            }));
            if (NetBridge != null && _byId.TryGetValue(npcObjectId, out var npcEntity))
                FanoutNetHtml(session, npcEntity, outcome.Html);
        }

        if (outcome.JournalChanged)
            QueueList(session, toSelf);

        // Mobius addLevel runs after the exp/sp that caused it, so the level packets follow the quest reward lines.
        if (levelled)
            ApplyLevelUp(session, player, toSelf);

        if (outcome.JournalChanged || outcome.Exp != 0 || outcome.Sp != 0)
            SaveQuestProgress(session, player);
    }

    /// <summary>Applies quest exp/sp and reports whether the character gained a level (Mobius <c>addLevel</c>).</summary>
    bool ApplyProgress(ZoneEntity player, QuestOutcome outcome) => ApplyExpSp(player, outcome.Exp, outcome.Sp);

    /// <summary>
    /// Mobius <c>PlayerStat.addExpAndSp</c> → <c>addExp</c> → <c>addLevel</c>: rolls the exp counter, and when it crosses
    /// a row also raises level, refreshes max HP/MP and refills them. Returns true on a level gain so the caller can run
    /// <see cref="ApplyLevelUp"/> (rewardSkills, penalties, packets).
    /// </summary>
    bool ApplyExpSp(ZoneEntity player, long exp, int sp)
    {
        if (exp == 0 && sp == 0)
            return false;

        var table = _game.Classes?.Experience;
        if (table == null)
        {
            player.Exp = exp > long.MaxValue - player.Exp ? long.MaxValue : player.Exp + exp;
            player.Sp = sp > int.MaxValue - player.Sp ? int.MaxValue : player.Sp + sp;
            return false;
        }

        int previous = player.Level;
        var progress = table.Add(player.Exp, player.Level, player.Sp, exp, sp);
        player.Exp = progress.Exp;
        player.Sp = progress.Sp;
        if (progress.Level == previous || _game.Classes == null || !_game.Classes.TryGet(player.ClassId, out var template) || template == null)
        {
            player.Level = progress.Level;
            return progress.Level != previous;
        }

        player.Level = progress.Level;
        player.MaxHp = template.ModifiedHp(progress.Level);
        player.MaxMp = template.ModifiedMp(progress.Level);
        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
        return true;
    }

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
    void ApplyLevelUp(ZoneSession session, ZoneEntity player, List<Pending> toSelf)
    {
        // rewardSkills: rebuild the book from the tree at the new level. RefreshRunSpeed inside also folds any
        // passive mastery the new rows brought (Quick Step / Agile Movement), so the live speed is correct.
        RefreshKnownSkills(player);
        RefreshGradePenalty(player);      // refreshExpertisePenalty
        RefreshWeightPenalty(player);     // refreshOverloaded (max load grew with the level)

        // Mobius broadcastPacket(new SocialAction(this, LEVEL_UP)): the gesture goes to self and every peer in view.
        FanoutSocial(player, SocialLevelUp, toSelf);

        toSelf.Add(Pack(session, MsgType.SystemMessage, new SystemMessageMsg
        {
            Id = LevelUpMessageId,
            Params = [],
            Text = "Your level has increased!"
        }));

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
        toSelf.Add(Pack(session, MsgType.StatusUpdate, status));

        toSelf.Add(Pack(session, MsgType.SkillList, SkillListFor(player)));
        toSelf.Add(SelfUserInfo(session, player));

        if (NetBridge != null)
        {
            int[] self = [player.ObjectId];
            NetBridge.FanoutSystemMessage(self, new Fineage.Net.Contract.SystemMessageNetMsg
            {
                Id = LevelUpMessageId,
                Text = "Your level has increased!",
                Params = []
            });
            NetBridge.FanoutStatusUpdate(self, new Fineage.Net.Contract.StatusUpdateNetMsg
            {
                EntityValue = (ulong)(uint)status.ObjectId,
                EntityGeneration = 1,
                AttributeIds = status.AttributeIds ?? [],
                Values = status.Values ?? []
            });
            NetBridge.FanoutSkillList(self,
                Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
            NetBridge.FanoutPlayerStats(self, new Fineage.Net.Contract.PlayerStatsMsg
            {
                EntityValue = (ulong)(uint)player.ObjectId,
                EntityGeneration = 1,
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

    void QueueList(ZoneSession session, List<Pending> toSelf)
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
        toSelf.Add(Pack(session, MsgType.QuestList, new QuestListMsg
        {
            Active = active,
            CompletedMask = list.CompletedMask,
            CompletedQuestIds = list.CompletedQuestIds,
            MaxActive = list.MaxActive
        }));
        if (NetBridge != null)
        {
            NetBridge.FanoutQuestList([session.ObjectId], new Fineage.Net.Contract.QuestListNetMsg
            {
                Active = active.Select(entry => new Fineage.Net.Contract.QuestJournalEntryNet
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
            });
        }
    }

    void LoadQuestProgress(ZoneSession session, ZoneEntity entity)
    {
        entity.Quests = new QuestJournal();
        if (_accounts == null)
            return;
        var record = _accounts.FindById(session.Account, session.ObjectId);
        if (record == null)
            return;
        entity.Exp = record.Exp;
        entity.Sp = record.Sp;
        entity.Quests = QuestJournal.Load(record.Quests);
    }

    /// <summary>
    /// Mobius <c>NewbieHelper.onFirstTalk</c>: one-time beginner kit under level 18.
    /// Does not open its own HTML — the guide page still comes from <see cref="SendNpcPage"/>.
    /// </summary>
    void TryNewbieHelperGift(ZoneSession session, ZoneEntity player, ZoneEntity npc, List<Pending> toSelf)
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
        toSelf.Add(Pack(session, MsgType.InventoryUpdate, new InventoryUpdateMsg { Changes = changes.ToArray() }));
        SaveInventory(session, player.Inventory);
        SaveQuestProgress(session, player);
        QueueList(session, toSelf);
        ZoneLog.Info(
            $"newbie-helper-gift object={session.ObjectId} npc={npc.NpcId} shot={shotId}x{shotCount}");
    }

    /// <summary>
    /// Mobius-style solo kill reward: template acquire exp/sp to the killer, then UserInfo and
    /// SystemMessage 95 ("You have earned $s1 experience and $s2 SP.").
    /// </summary>
    void GrantKillReward(ZoneEntity killer, ZoneEntity npc, List<Pending> outbound)
    {
        if (killer.Kind != ZoneEntityKind.Player || killer.Session == null || npc.Kind != ZoneEntityKind.Npc)
            return;
        ApplyQuestKill(killer, npc, outbound);
        long exp = npc.RewardExp;
        int sp = npc.RewardSp;
        if (exp <= 0 && sp <= 0)
            return;

        bool levelled = ApplyExpSp(killer, exp, sp);
        var session = killer.Session;
        var notice = new SystemMessageMsg
        {
            Id = KillRewardMessageId,
            Params = [exp.ToString(System.Globalization.CultureInfo.InvariantCulture), sp.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Text = $"You have earned {exp} experience and {sp} SP."
        };
        outbound.Add(Pack(session, MsgType.SystemMessage, notice));
        // StatusUpdate carries live Exp/Sp/Level; UserInfo refreshes the full sheet (vitals + template stats).
        var status = new StatusUpdateMsg
        {
            ObjectId = killer.ObjectId,
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
                killer.Level,
                (int)Math.Min(int.MaxValue, killer.Exp),
                killer.Sp,
                killer.CurHp,
                killer.MaxHp,
                killer.CurMp,
                killer.MaxMp
            ]
        };
        outbound.Add(Pack(session, MsgType.StatusUpdate, status));
        outbound.Add(SelfUserInfo(session, killer));

        if (NetBridge != null)
        {
            int[] self = [killer.ObjectId];
            var vitals = BuildUserInfoVitals(killer);
            NetBridge.FanoutSystemMessage(self, new Fineage.Net.Contract.SystemMessageNetMsg
            {
                Id = notice.Id,
                Text = notice.Text,
                Params = notice.Params ?? []
            });
            NetBridge.FanoutStatusUpdate(self, new Fineage.Net.Contract.StatusUpdateNetMsg
            {
                EntityValue = (ulong)(uint)status.ObjectId,
                EntityGeneration = 1,
                AttributeIds = status.AttributeIds ?? [],
                Values = status.Values ?? []
            });
            NetBridge.FanoutPlayerStats(self, new Fineage.Net.Contract.PlayerStatsMsg
            {
                EntityValue = (ulong)(uint)killer.ObjectId,
                EntityGeneration = 1,
                Level = killer.Level,
                Exp = killer.Exp,
                Sp = killer.Sp,
                Hp = killer.CurHp,
                MaxHp = killer.MaxHp,
                Mp = killer.CurMp,
                MaxMp = killer.MaxMp,
                Cp = vitals.CurCp,
                MaxCp = vitals.MaxCp
            });
        }

        SaveQuestProgress(session, killer);

        // Mobius addExpAndSp → addLevel: level up after the exp notice so the client sees "earned exp", then level up.
        if (levelled)
            ApplyLevelUp(session, killer, outbound);
    }

    internal const int KillRewardMessageId = 95;

    void SaveQuestProgress(ZoneSession session, ZoneEntity entity)
    {
        if (_accounts == null)
            return;
        _accounts.TryReplaceQuests(session.Account, session.ObjectId, entity.Quests.Save());
        _accounts.TrySaveProgress(
            session.Account,
            session.ObjectId,
            entity.Level,
            entity.Exp,
            entity.Sp,
            entity.MaxHp,
            entity.CurHp,
            entity.MaxMp,
            entity.CurMp);
    }

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
