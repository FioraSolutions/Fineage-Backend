using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Game.Quests;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.Rewards;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    readonly IRegionRandom _questRandom;

    public Task<string?> TryQuestListAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        QueueList(session);
        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryQuestAbortAsync(ZoneSession session, int questId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        if (questId <= 0)
            return Task.FromResult<string?>("bad_request");

        // Abort may strip quest items → SaveInventory after the change (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() =>
        {
            if (!TryPlayer(session, out var player))
                return "world";
            var outcome = RecordQuest(player, () => _quests.Abort(player.Quests, player.Inventory!, questId));
            if (outcome.Ok || outcome.Lines.Count > 0)
                Publish(session, player, outcome, npcObjectId: 0, npcId: 0);
            return outcome.Ok ? null : FailureCode(outcome.Failure);
        }));
    }

    public Task<string?> TryNpcTalkAsync(ZoneSession session, int npcObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        if (npcObjectId <= 0)
            return Task.FromResult<string?>("bad_request");

        return Task.FromResult(WithDeferredInventoryPersist(
            () => Talk(session, npcObjectId, questId: 0, eventName: null, out _, allowApproach: true)));
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

        return Task.FromResult(WithDeferredInventoryPersist(
            () => Talk(session, npcObjectId, questId, eventName, out _)));
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

    string? Talk(ZoneSession session, int npcObjectId, int questId, string? eventName, out QuestOutcome? outcome, bool allowApproach = false)
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
        {
            // Mobius NpcAction: already targeted, not auto-attackable, !canInteract →
            // AI_INTENTION_INTERACT → moveToPawn, then PlayerAI.thinkInteract → doInteract → onAction.
            // Bypass links stay a hard range check (Npc.onBypassFeedback requires canInteract).
            if (!allowApproach || eventName != null)
                return "range";
            long now = NowUnixMs();
            if (player.CastingUntilMs > now || player.Sitting)
                return null;
            player.Intent.AutoAttackTargetId = 0;
            player.Intent.PendingCastSkillId = 0;
            player.Intent.PendingCastAtUnixMs = 0;
            long ms = ApproachTarget(player, npc, _game.Quests.TalkRange);
            if (ms < 0)
                return "range";
            if (ms > 0)
            {
                player.Intent.PendingTalkNpcId = npc.ObjectId;
                player.Intent.PendingTalkAtUnixMs = now + ms;
                return null;
            }
        }

        player.Intent.PendingTalkNpcId = 0;
        player.Intent.PendingTalkAtUnixMs = 0;
        // Mobius NpcAction sends MoveToPawn(player, npc, 100) before the chat window so the NPC faces the talker.
        npc.Heading = ZoneCombat.HeadingFrom(npc.X, npc.Y, player.X, player.Y);

        // Mobius NewbieHelper onFirstTalk — before guide HTML / other quest hooks.
        if (eventName == null && npc.NpcId == _weddingSettings.ManagerNpcId && _weddingSettings.AllowWedding)
            return WeddingNpcTalk(session, player, npc);

        if (eventName == null)
            TryNewbieHelperGift(session, player, npc);

        outcome = RecordQuest(player, () => eventName == null
            ? _quests.FirstTalk(player.Quests, player.Inventory!, npc.NpcId, npc.Name, QuestActorOf(player))
            : eventName.Length == 0
                ? _quests.Talk(player.Quests, player.Inventory!, questId, npc.NpcId, QuestActorOf(player))
                : _quests.AdvEvent(player.Quests, player.Inventory!, questId, npc.NpcId, eventName, QuestActorOf(player)));
        if (IsNpcPage(outcome))
        {
            SendNpcPage(session, player, npc, 0);
            return null;
        }

        if (outcome.Ok || outcome.Lines.Count > 0 || outcome.JournalChanged)
            Publish(session, player, outcome, npc.ObjectId, npc.NpcId);
        return outcome.Ok ? null : FailureCode(outcome.Failure);
    }

    /// <summary>Mobius <c>PlayerAI.thinkInteract</c> on <c>EVT_ARRIVED</c>: open the chat once the run is in range.</summary>
    void TickPendingTalks(long now)
    {
        foreach (var player in Players().ToList())
        {
            var session = player.Session;
            if (session == null || player.Intent.PendingTalkNpcId <= 0 || now < player.Intent.PendingTalkAtUnixMs)
                continue;
            int npcId = player.Intent.PendingTalkNpcId;
            player.Intent.PendingTalkNpcId = 0;
            player.Intent.PendingTalkAtUnixMs = 0;
            if (!_byId.TryGetValue(npcId, out var npc) || npc.Kind != ZoneEntityKind.Npc)
                continue;
            if (!InTalkRange(player, npc))
            {
                long ms = ApproachTarget(player, npc, _game.Quests.TalkRange);
                if (ms > 0)
                {
                    player.Intent.PendingTalkNpcId = npcId;
                    player.Intent.PendingTalkAtUnixMs = now + ms;
                    continue;
                }
                if (ms < 0)
                {
                    NotifyLine(player.ObjectId, SysMsgTargetOutOfRange, "Your target is out of range.");
                    continue;
                }
            }

            Talk(session, npcId, questId: 0, eventName: null, out _, allowApproach: false);
        }
    }

    QuestActor QuestActorOf(ZoneEntity player, bool byPet = false) =>
        new(player.Race, player.Level, player.ClassId, player.SubClassActive)
        {
            PetControlObjectId = PetControlObjectIdOf(player),
            ByPet = byPet,
            Roll = Rng(_questRandom, player).NextDouble
        };

    /// <summary>
    /// Mobius <c>Quest.onKill</c> for the killer (killer-only steps) plus <c>party: true</c> steps shared to one random eligible member. Quest drops go straight to the
    /// bag like <c>giveItemRandomly</c>; journal / bag / sounds go out through the same Publish path as talk.
    /// </summary>
    internal void QuestKillForTest(ZoneEntity killer, int npcId)
    {
        using (_affinity.Step())
            ApplyQuestKill(killer, new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = npcId, ObjectId = 0 });
    }

    /// <summary>Test hook: run the exp/sp → level-up pipeline the way a kill reward does.</summary>
    internal void GrantExpForTest(ZoneEntity killer, long exp, int sp)
    {
        using (_affinity.Step())
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
        using (_affinity.Step())
            GrantKillReward(killer, npc);
    }

    /// <summary>Mobius <c>Quest.onAttack</c>: a player's hit on an NPC some quest listens to (Q234 Pipette Knife on Baium).</summary>
    void ApplyQuestAttack(ZoneEntity attacker, ZoneEntity npc, bool byPet = false)
    {
        if (attacker.Kind != ZoneEntityKind.Player || attacker.Inventory == null || attacker.Dead || attacker.Session == null)
            return;
        if (npc.QuestSpawn != null && ApplyOwnedAttack(attacker, npc))
            return;
        if (npc.QuestAttackSpawned || !_quests.HasAttackSteps(npc.NpcId))
            return;
        var outcome = RecordQuest(attacker,
            () => _quests.OnAttack(attacker.Quests, attacker.Inventory, npc.NpcId, QuestActorOf(attacker, byPet)));
        if (outcome.Ok && outcome.Spawns.Count > 0)
            npc.QuestAttackSpawned = true;
        PublishKill(attacker, outcome, npc);
    }

    /// <summary>Test hook: one landed hit by <paramref name="attacker"/> on <paramref name="npc"/>.</summary>
    internal void QuestAttackForTest(ZoneEntity attacker, ZoneEntity npc, bool byPet = false)
    {
        using (_affinity.Step())
            ApplyQuestAttack(attacker, npc, byPet);
    }

    /// <summary>Mobius VarkaKetra range for the alliance penalty around the corpse.</summary>
    const int AlliancePenaltyRange = 1500;

    /// <summary>Mobius VarkaKetra.onKill: allies of the slain monster's side near the corpse lose a mark level.</summary>
    void ApplyAllianceBetrayal(ZoneEntity killer, ZoneEntity npc)
    {
        if (!QuestMachine.KetraMobs.Contains(npc.NpcId) && !QuestMachine.VarkaMobs.Contains(npc.NpcId))
            return;
        foreach (var member in QuestPartyCandidates(killer))
        {
            if (member.Inventory == null || member.Session == null)
                continue;
            long dx = member.X - (long)npc.X, dy = member.Y - (long)npc.Y;
            if (npc.ObjectId != 0 && dx * dx + dy * dy > (long)AlliancePenaltyRange * AlliancePenaltyRange)
                continue;
            var outcomes = RecordBagChange(member, LedgerTxKind.QuestReward, LedgerAccount.Of(SystemAccount.Quests),
                () => _quests.BreakAlliance(member.Quests, member.Inventory, npc.NpcId).ToList(),
                list => list.Count == 1 ? list[0].QuestId : 0);
            foreach (var outcome in outcomes)
                PublishKill(member, outcome, npc);
        }
    }

    void ApplyQuestKill(ZoneEntity killer, ZoneEntity npc)
    {
        if (npc.QuestSpawn != null)
            ApplyOwnedKill(killer, npc);
        if (killer.Inventory == null || killer.Dead || killer.Session == null)
            return;
        ApplyAllianceBetrayal(killer, npc);
        var outcome = RecordQuest(killer, () => _quests.OnKill(killer.Quests, killer.Inventory, npc.NpcId, QuestActorOf(killer)));
        PublishKill(killer, outcome, npc);

        // Mobius executeForEachPlayer: party members near the corpse share raid-head style steps.
        if (_quests.HasEveryoneSteps(npc.NpcId))
        {
            foreach (var member in QuestPartyCandidates(killer))
            {
                if (member == killer || member.Inventory == null || member.Dead || member.Session == null)
                    continue;
                PublishKill(member, RecordQuest(member,
                    () => _quests.OnEveryoneKill(member.Quests, member.Inventory, npc.NpcId, QuestActorOf(member))), npc);
            }
        }

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
            var winner = eligible[Rng(_questRandom, killer).Next(eligible.Count)];
            PublishKill(winner, RecordQuest(winner,
                () => _quests.OnPartyKill(winner.Quests, winner.Inventory!, questId, npc.NpcId, QuestActorOf(winner))), npc);
        }
    }

    /// <summary>L2J/Mobius <c>Config.ALT_PARTY_RANGE</c> default (Character.ini AltPartyRange = 1600).</summary>
    public const int QuestPartyRange = 1600;

    IEnumerable<ZoneEntity> QuestPartyCandidates(ZoneEntity killer)
    {
        yield return killer;
        if (_party.Of(killer.ObjectId) is not { } party)
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
        if (outcome.Items.Count == 0 && !outcome.JournalChanged && outcome.Sounds.Count == 0 && outcome.Spawns.Count == 0 &&
            outcome.SpawnsAt.Count == 0 && outcome.Extras.Empty)
            return;
        Publish(player.Session, player, outcome, npc.ObjectId, npc.NpcId);
    }

    internal void JoinPartyForTest(params ZoneEntity[] members) =>
        JoinPartyForTest(Fineage.Game.Services.PartyDistributionType.FindersKeepers, members);

    internal void JoinPartyForTest(Fineage.Game.Services.PartyDistributionType distribution, params ZoneEntity[] members)
    {
        using (_affinity.Step())
        {
            var party = _party.Create(members[0].ObjectId, (int)distribution);
            foreach (var m in members.Skip(1))
                _party.Join(party, m.ObjectId);
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
        && outcome.SpawnsAt.Count == 0
        && !outcome.Noble
        && outcome.Teleport == null
        && outcome.Social == 0
        && outcome.Extras.Empty
        && outcome.Exp == 0
        && outcome.Sp == 0;

    void Publish(ZoneSession session, ZoneEntity player, QuestOutcome outcome, int npcObjectId, int npcId)
    {
        bool levelled = ApplyProgress(player, outcome);
        if (outcome.Spawns.Count > 0 && _byId.TryGetValue(npcObjectId, out var anchor))
            ApplyQuestSpawns(player, anchor, outcome.Spawns);
        foreach (var point in outcome.SpawnsAt)
            SpawnQuestNpc(player, point.NpcId, point.X, point.Y, point.Z, player.InstanceId, 0);
        if (!outcome.Extras.Empty)
            ApplyQuestExtras(session, player, outcome.Extras, npcObjectId);
        if (outcome.Noble)
            SetNoble(player, true);
        if (outcome.Social > 0)
            NetBridge?.FanoutSocialAction(KnownNetPlayersOf(player), player.ObjectId, outcome.Social);
        if (outcome.Items.Count > 0 && player.Inventory != null)
        {
            var invUpdate = new InventoryUpdateMsg
            {
                Changes = outcome.Items.Select(touch => InventoryMessages.ChangeRow((int)touch.Kind, touch.Item)).ToArray()
            };
            if (NetBridge != null)
                NetBridge.FanoutInventoryUpdate([session.ObjectId], invUpdate);
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
                NetBridge.FanoutSystemMessage([session.ObjectId], new SystemMessageMsg
                {
                    Id = notice.Id,
                    Text = notice.Text ?? "",
                    Params = notice.Params ?? []
                });
            }
        }

        // Mobius QuestState.playSound → player.sendPacket(new PlaySound(file)): the quest owner only, type 0,
        // not bound to an object. Observers never hear someone else's quest jingle.
        if (NetBridge != null)
        {
            foreach (string sound in outcome.Sounds)
            {
                if (!string.IsNullOrWhiteSpace(sound))
                    NetBridge.FanoutPlaySound([session.ObjectId], 0, sound, 0, 0, 0, 0, 0);
            }
        }

        if (outcome.Html.Length > 0)
        {
            if (_byId.TryGetValue(npcObjectId, out var npcEntity))
                FanoutNetHtml(session, npcEntity, outcome.Html);
        }

        ApplyTutorialUiOutcome(session, outcome.TutorialUi);

        if (outcome.JournalChanged)
        {
            if (outcome.QuestId > 0)
            {
                var row = player.Quests.Find(outcome.QuestId);
                if (row != null)
                    EmitCondClientFx(session, player, outcome.QuestId, row.Cond, playSounds: true);
            }
            QueueList(session);
        }

        // Mobius addLevel runs after the exp/sp that caused it, so the level packets follow the quest reward lines.
        if (levelled)
            ApplyLevelUp(session, player);

        if (outcome.JournalChanged || outcome.Exp != 0 || outcome.Sp != 0)
            SaveQuestProgress(session, player);

        // Mobius teleToLocation from a quest step (Possessor of a Precious Soul 4 → Lady of the Lake).
        if (outcome.Teleport is { } to)
        {
            var (x, y) = Offset(player, to.X, to.Y);
            if (Loaded(x, y))
                Relocate(player, x, y, to.Z);
            else
                Notice(session, NpcServiceProtocol.DestinationUnloaded, NpcServiceProtocol.DestinationUnloadedText);
        }
    }

}
