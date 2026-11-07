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
    public Task<string?> TryQuestListAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        lock (_gate)
            QueueList(session);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryQuestAbortAsync(ZoneSession session, int questId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        if (questId <= 0)
            return Task.FromResult<string?>("bad_request");

        QuestOutcome outcome;
        lock (_gate)
        {
            if (!TryPlayer(session, out var player))
                return Task.FromResult<string?>("world");
            outcome = _quests.Abort(player.Quests, player.Inventory!, questId);
            if (outcome.Ok || outcome.Lines.Count > 0)
                Publish(session, player, outcome, npcObjectId: 0, npcId: 0);
        }

        return Task.FromResult<string?>(outcome.Ok ? null : FailureCode(outcome.Failure));
    }

    public Task<string?> TryNpcTalkAsync(ZoneSession session, int npcObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        if (npcObjectId <= 0)
            return Task.FromResult<string?>("bad_request");

        string? error;
        lock (_gate)
            error = Talk(session, npcObjectId, questId: 0, eventName: null, out _);
        return Task.FromResult<string?>(error);
    }

    public Task<string?> TryQuestEventAsync(ZoneSession session, int npcObjectId, int questId, string? eventName, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        if (npcObjectId <= 0 || questId <= 0)
            return Task.FromResult<string?>("bad_request");
        eventName ??= "";
        if (eventName.Length > 64 || !EventOk(eventName))
            return Task.FromResult<string?>("bad_request");

        string? error;
        lock (_gate)
            error = Talk(session, npcObjectId, questId, eventName, out _);
        return Task.FromResult<string?>(error);
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

    string? Talk(ZoneSession session, int npcObjectId, int questId, string? eventName, out QuestOutcome? outcome)
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
            TryNewbieHelperGift(session, player, npc);

        outcome = eventName == null
            ? _quests.FirstTalk(player.Quests, player.Inventory!, npc.NpcId, npc.Name, QuestActorOf(player))
            : eventName.Length == 0
                ? _quests.Talk(player.Quests, player.Inventory!, questId, npc.NpcId, QuestActorOf(player))
                : _quests.AdvEvent(player.Quests, player.Inventory!, questId, npc.NpcId, eventName, QuestActorOf(player));
        if (IsNpcPage(outcome))
        {
            SendNpcPage(session, player, npc, 0);
            return null;
        }

        if (outcome.Ok || outcome.Lines.Count > 0 || outcome.JournalChanged)
            Publish(session, player, outcome, npc.ObjectId, npc.NpcId);
        return outcome.Ok ? null : FailureCode(outcome.Failure);
    }

    static QuestActor QuestActorOf(ZoneEntity player) => new(player.Race, player.Level, player.ClassId);

    /// <summary>
    /// Mobius <c>Quest.onKill</c> for the killer (killer-only steps) plus <c>party: true</c> steps shared to one random eligible member. Quest drops go straight to the
    /// bag like <c>giveItemRandomly</c>; journal / bag / sounds go out through the same Publish path as talk.
    /// </summary>
    internal void QuestKillForTest(ZoneEntity killer, int npcId)
    {
        lock (_gate)
            ApplyQuestKill(killer, new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = npcId, ObjectId = 0 });
    }

    /// <summary>Test hook: run the exp/sp → level-up pipeline the way a kill reward does.</summary>
    internal void GrantExpForTest(ZoneEntity killer, long exp, int sp)
    {
        lock (_gate)
        {
            if (!ApplyExpSp(killer, exp, sp))
                return;
            if (killer.Session != null)
                ApplyLevelUp(killer.Session, killer);
        }
    }

    /// <summary>Test hook: run the kill-reward split exactly like a dying mob does (reads <see cref="ZoneEntity.DamageBy"/>).</summary>
    internal void GrantKillRewardForTest(ZoneEntity killer, ZoneEntity npc)
    {
        lock (_gate)
            GrantKillReward(killer, npc);
    }

    void ApplyQuestKill(ZoneEntity killer, ZoneEntity npc)
    {
        if (killer.Inventory == null || killer.Dead || killer.Session == null)
            return;
        var outcome = _quests.OnKill(killer.Quests, killer.Inventory, npc.NpcId, QuestActorOf(killer));
        PublishKill(killer, outcome, npc);

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
            PublishKill(winner, _quests.OnPartyKill(winner.Quests, winner.Inventory!, questId, npc.NpcId, QuestActorOf(winner)), npc);
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

    void PublishKill(ZoneEntity player, QuestOutcome outcome, ZoneEntity npc)
    {
        if (outcome.QuestId == 0 || !outcome.Ok || player.Session == null)
            return;
        if (outcome.Items.Count == 0 && !outcome.JournalChanged && outcome.Sounds.Count == 0)
            return;
        Publish(player.Session, player, outcome, npc.ObjectId, npc.NpcId);
    }

    internal void JoinPartyForTest(params ZoneEntity[] members) =>
        JoinPartyForTest(Fineage.Game.Services.PartyDistributionType.FindersKeepers, members);

    internal void JoinPartyForTest(Fineage.Game.Services.PartyDistributionType distribution, params ZoneEntity[] members)
    {
        lock (_gate)
        {
            var party = new ZoneParty
            {
                PartyId = _nextPartyId++,
                LeaderObjectId = members[0].ObjectId,
                ItemDistribution = (int)distribution
            };
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
        && outcome.Spawns.Count == 0
        && outcome.Exp == 0
        && outcome.Sp == 0;

    void Publish(ZoneSession session, ZoneEntity player, QuestOutcome outcome, int npcObjectId, int npcId)
    {
        bool levelled = ApplyProgress(player, outcome);
        if (outcome.Spawns.Count > 0 && _byId.TryGetValue(npcObjectId, out var anchor))
            ApplyQuestSpawns(player, anchor, outcome.Spawns);
        if (outcome.Items.Count > 0 && player.Inventory != null)
        {
            var invUpdate = new InventoryUpdateMsg
            {
                Changes = outcome.Items.Select(touch => InventoryMessages.ChangeRow((int)touch.Kind, touch.Item)).ToArray()
            };
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

        // outcome.Sounds had no Net frame; the V2 QuestSound notice is retired with the rest of V2.

        if (outcome.Html.Length > 0)
        {
            if (_byId.TryGetValue(npcObjectId, out var npcEntity))
                FanoutNetHtml(session, npcEntity, outcome.Html);
        }

        if (outcome.JournalChanged)
            QueueList(session);

        // Mobius addLevel runs after the exp/sp that caused it, so the level packets follow the quest reward lines.
        if (levelled)
            ApplyLevelUp(session, player);

        if (outcome.JournalChanged || outcome.Exp != 0 || outcome.Sp != 0)
            SaveQuestProgress(session, player);
    }

    /// <summary>Applies quest exp/sp and reports whether the character gained a level (Mobius <c>addLevel</c>).</summary>
    bool ApplyProgress(ZoneEntity player, QuestOutcome outcome) => ApplyExpSp(player, outcome.Exp, outcome.Sp);

    /// <summary>
    /// Mobius quest <c>addSpawn(npcId, npc, randomOffset, …)</c> + <c>addAttackDesire</c>:
    /// materialize near the dialog NPC and put hate on the player. No respawn slot — death removes them.
    /// </summary>
    void ApplyQuestSpawns(ZoneEntity player, ZoneEntity anchor, IReadOnlyList<int> npcIds)
    {
        var templates = NpcCatalog.LoadDefault();
        foreach (int npcId in npcIds)
        {
            if (!templates.ById.TryGetValue(npcId, out var template))
            {
                ZoneLog.Info($"quest spawn skip unknown npcId={npcId}");
                continue;
            }

            int objectId = AllocateQuestNpcObjectId();
            // Mobius randomOffset around the talking NPC (~40–80 units).
            double angle = _spawnRandom.NextDouble() * Math.PI * 2;
            int dist = 40 + _spawnRandom.Next(0, 41);
            int x = anchor.X + (int)(Math.Cos(angle) * dist);
            int y = anchor.Y + (int)(Math.Sin(angle) * dist);
            int z = anchor.Z;
            if (_geo.HasGeo(x, y))
                z = _geo.GetHeight(x, y, z);

            var entity = new ZoneEntity
            {
                ObjectId = objectId,
                Kind = ZoneEntityKind.Npc,
                InstanceId = anchor.InstanceId,
                Name = template.Name,
                Title = template.Title,
                NpcType = template.Type,
                X = x,
                Y = y,
                Z = z,
                Heading = anchor.Heading,
                NpcId = template.NpcId,
                Attackable = true,
                MaxHp = template.Hp,
                CurHp = template.Hp,
                MaxMp = template.Mp,
                CurMp = template.Mp,
                Level = template.Level,
                RewardExp = template.Exp,
                RewardSp = template.Sp,
                PAtk = template.PAtk,
                HpRegen = template.HpRegen,
                MpRegen = template.MpRegen,
                PDef = template.PDef,
                PAtkSpd = template.AttackSpeed,
                BaseAccuracy = template.Accuracy,
                BaseEvasion = template.Evasion,
                BaseCritical = template.Critical,
                RandomDamage = template.RandomDamage,
                Dex = template.Dex,
                ShieldDef = template.ShieldDef,
                ShieldRate = template.ShieldRate,
                MAtk = template.MAtk,
                MDef = template.MDef,
                AttackRange = template.AttackRange,
                Aggressive = true,
                AggroRange = Math.Max(template.AggroRange, 1000),
                ClanHelpRange = template.ClanHelpRange,
                Clans = template.Clans,
                IgnoreNpcIds = template.IgnoreNpcIds,
                NpcSoulShotLeft = template.SoulShot,
                NpcSoulShotChance = template.SoulShotChance,
                NpcSpiritShotLeft = template.SpiritShot,
                NpcSpiritShotChance = template.SpiritShotChance,
                DefenceTraits = template.DefenceTraits,
                AiSkills = FilterAttackSkills(template.AiSkills, _npcAiSupport.Get(template.NpcId)?.Suicide),
                MinSkillChance = template.MinSkillChance,
                MaxSkillChance = template.MaxSkillChance,
                AiHealSkills = _npcAiSupport.Get(template.NpcId)?.Heal,
                AiBuffSkills = _npcAiSupport.Get(template.NpcId)?.Buff,
                AiDebuffSkills = _npcAiSupport.Get(template.NpcId)?.Debuff,
                AiImmobilizeSkills = _npcAiSupport.Get(template.NpcId)?.Immobilize,
                AiSilenceSkills = _npcAiSupport.Get(template.NpcId)?.Silence,
                AiSuicideSkills = _npcAiSupport.Get(template.NpcId)?.Suicide,
                RunSpeed = template.RunSpeed,
                WalkSpeed = template.WalkSpeed,
                HomeSet = true,
                HomeX = x,
                HomeY = y,
                HomeZ = z
            };
            _byId.Add(objectId, entity);
            _aoi.Enter(objectId, x, y);
            AddHate(entity, player, hate: 100);
            FanoutNewNpc(entity);
        }
    }

    int AllocateQuestNpcObjectId()
    {
        for (int id = NpcObjectIdAllocator.ExclusiveEnd - 1; id >= NpcObjectIdAllocator.First; id--)
        {
            if (_byId.ContainsKey(id) || _spawnSlots.ContainsKey(id))
                continue;
            return id;
        }

        throw new InvalidOperationException("NPC object id range is exhausted for quest spawns.");
    }

    /// <summary>Test hook: place a talk NPC and run <c>spawn_npc</c> outcomes near it.</summary>
    internal int QuestSpawnNearForTest(ZoneEntity player, int anchorNpcId, int x, int y, int z, IReadOnlyList<int> spawnNpcIds)
    {
        lock (_gate)
        {
            int anchorId = AllocateQuestNpcObjectId();
            var anchor = new ZoneEntity
            {
                ObjectId = anchorId,
                Kind = ZoneEntityKind.Npc,
                NpcId = anchorNpcId,
                Name = "QuestAnchor",
                X = x,
                Y = y,
                Z = z,
                Attackable = false,
                MaxHp = 100,
                CurHp = 100
            };
            _byId.Add(anchorId, anchor);
            _aoi.Enter(anchorId, x, y);
            ApplyQuestSpawns(player, anchor, spawnNpcIds);
            return anchorId;
        }
    }

    /// <summary>Test hook: count live NPCs with the given template id.</summary>
    internal int CountNpcTemplateForTest(int npcId)
    {
        lock (_gate)
            return _byId.Values.Count(e => e.Kind == ZoneEntityKind.Npc && e.NpcId == npcId && e.CurHp > 0);
    }

    /// <summary>Test hook: first live NPC with the given template id.</summary>
    internal ZoneEntity? FindNpcTemplateForTest(int npcId)
    {
        lock (_gate)
            return _byId.Values.FirstOrDefault(e => e.Kind == ZoneEntityKind.Npc && e.NpcId == npcId && e.CurHp > 0);
    }

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

    /// <summary>
    /// Quest journal rows for the live player, for the Net enter-world burst. Mobius sends its journal on the
    /// first <c>RequestQuestList</c>; the Net client shows the window on the menu button, so sending it with the
    /// rest of the enter packet leaves the tabs filled even if the player opens the window before the reply lands.
    /// </summary>
    internal Fineage.Net.Contract.QuestListNetMsg NetQuestList(ZoneSession session)
    {
        lock (_gate)
        {
            if (!TryPlayer(session, out var player))
                return new Fineage.Net.Contract.QuestListNetMsg();

            var list = _quests.List(player.Quests);
            return new Fineage.Net.Contract.QuestListNetMsg
            {
                Active = list.Active.Select(entry => new Fineage.Net.Contract.QuestJournalEntryNet
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

    void LoadQuestProgress(ZoneSession session, ZoneEntity entity)    {
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
        NetBridge?.FanoutInventoryUpdate([session.ObjectId], ToNetInventoryUpdate(update));
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
                EntityValue = (ulong)(uint)player.ObjectId,
                EntityGeneration = 1,
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
