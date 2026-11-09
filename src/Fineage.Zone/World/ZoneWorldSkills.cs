using Fineage.Game.Quests;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TrySkillUseAsync(
        ZoneSession session,
        int skillId,
        int targetObjectId,
        CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TrySkillUse(session, new SkillUseIntent(skillId, targetObjectId));
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Trainer mode. false (default) keeps the pre-trainer behaviour: NPC-learned tree skills are granted free at their
    /// level. true = Mobius: only autoGet rows are free; NPC rows must be bought with SP at a trainer (learn_skill).
    /// Env <c>FINEAGE_TRAINER_SP_LEARN=1</c>. Learned skills are persisted either way (character_skills).
    /// </summary>
    public bool TrainerSpLearn { get; set; } =
        Environment.GetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN") is "1" or "true";

    void RefreshKnownSkills(ZoneEntity entity)
    {
        entity.KnownSkills.Clear();
        foreach (var row in PlayerSkillBook.AutoSkills(_game.SkillTrees, _game.Skills, entity.ClassId, entity.Level, _game.Classes,
                     includeNpcSkills: !TrainerSpLearn))
            entity.KnownSkills[row.SkillId] = row.Level;
        foreach (var pair in entity.LearnedSkills)
        {
            if (_game.Skills.Find(pair.Key) == null)
                continue;
            if (!entity.KnownSkills.TryGetValue(pair.Key, out int have) || have < pair.Value)
                entity.KnownSkills[pair.Key] = pair.Value;
        }

        // Passive runSpd masteries (Quick Step, Agile Movement, armor sets) are part of the live run speed.
        RefreshRunSpeed(entity);
    }

    internal string? TrainerServiceForTest(ZoneSession session, string intent, int skillId = -1, int npcObjectId = 0)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player))
                return "world";
            var npc = new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = 30157, Name = "Greenis", ObjectId = npcObjectId };
            return intent == NpcServiceIntent.SkillList
                ? ShowTrainerSkills(session, player, npc)
                : LearnTrainerSkill(session, player, npc, skillId);
        }
    }

    /// <summary>
    /// Mobius <c>RequestAcquireSkillInfo</c> (the confirmation page) and <c>RequestAcquireSkill</c> (the purchase)
    /// straight from the trainer window, without a bypass round trip.
    /// </summary>
    public Task<string?> TryAcquireSkillAsync(ZoneSession session, int skillId, int level, int skillType, bool informing, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        if (skillId <= 0 || level <= 0)
            return Task.FromResult<string?>("bad_request");

        string? error = null;
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
                return Task.FromResult<string?>("world");
            if (player.Dead)
                return Task.FromResult<string?>(InventoryProtocol.Dead);
            if (informing)
                PublishAcquireSkillInfo(session, player, skillId, level, skillType);
            else
                error = LearnTrainerSkill(session, player, TrainerNpcOf(session), skillId);
        }

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

    /// <summary>The trainer this player last opened, or a stand-in so the gate does not need the NPC in range.</summary>
    ZoneEntity TrainerNpcOf(ZoneSession session) =>
        _byId.TryGetValue(session.TrainerNpcId, out var npc) && npc.Kind == ZoneEntityKind.Npc
            ? npc
            : new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = 0, Name = "Trainer", ObjectId = 0 };

    void LoadLearnedSkills(ZoneEntity entity, Fineage.Accounts.CharacterRecord? stored)
    {
        entity.LearnedSkills.Clear();
        foreach (var s in stored?.LearnedSkills ?? [])
        {
            if (s.IsValid)
                entity.LearnedSkills[s.SkillId] = s.Level;
        }
    }

    string? ShowTrainerSkills(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var rows = TrainerSkills.Available(_game.SkillTrees, _game.Skills, player.ClassId, player.Level, player.KnownSkills, _game.Classes);
        if (!TrainerSpLearn && rows.Count == 0)
        {
            var html = new System.Text.StringBuilder("<html><body>%npcname%:<br>");
            html.Append("In this world your class skills come to you on their own when they reach their level.<br>");
            int next = TrainerSkills.NextLearnLevel(_game.SkillTrees, player.ClassId, player.Level, _game.Classes);
            if (next > 0)
                html.Append("Your next new skill arrives at level ").Append(next).Append('.');
            html.Append("</body></html>");
            SendHtml(session, player, npc, html.ToString());
            return null;
        }

        // Mobius Folk.showSkillList -> AcquireSkillList: the trainer window is the primary surface on Net.
        session.TrainerNpcId = npc.ObjectId;
        PublishAcquireSkillList(session, player, rows);
        if (rows.Count == 0)
        {
            int next = TrainerSkills.NextLearnLevel(_game.SkillTrees, player.ClassId, player.Level, _game.Classes);
            // Mobius SystemMessage 607 "You do not have any further skills to learn. Come back when you have reached Level S1."
            SendHtml(session, player, npc, NothingToLearnHtml(next));
            return null;
        }

        // The HTML list stays for clients without the acquire window; it links the same learn_skill bypass.
        SendHtml(session, player, npc, TrainerListHtml(player, rows));
        return null;
    }

    static string NothingToLearnHtml(int nextLevel) =>
        new System.Text.StringBuilder("<html><body>%npcname%:<br>")
            .Append(nextLevel > 0
                ? $"There is nothing more I can teach you now. Come back at level {nextLevel}."
                : "There is nothing more I can teach you.")
            .Append("</body></html>")
            .ToString();

    string TrainerListHtml(ZoneEntity player, IReadOnlyList<LearnableSkill> rows)
    {
        var html = new System.Text.StringBuilder("<html><body>%npcname%:<br>");
        html.Append("SP: ").Append(player.Sp).Append("<br>");
        foreach (var r in rows)
        {
            string name = _game.Skills.Find(r.SkillId)?.Name ?? $"Skill {r.SkillId}";
            html.Append("<a action=\"bypass -h npc_%objectId%_learn_skill ").Append(r.SkillId).Append("\">")
                .Append(HtmlText(name)).Append(" Lv ").Append(r.Level).Append(" - ").Append(r.LevelUpSp).Append(" SP</a><br>");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    /// <summary>
    /// Mobius <c>AcquireSkillList</c> (<c>AcquireSkillType.USUAL</c>): one row per offer with the next level, the
    /// skill's highest level in the class tree and the SP price. Requirement bit 1 is Mobius' "previous level learned".
    /// </summary>
    void PublishAcquireSkillList(ZoneSession session, ZoneEntity player, IReadOnlyList<LearnableSkill> rows)
    {
        if (NetBridge == null || session.ObjectId <= 0)
            return;
        var entries = new AcquireSkillEntryMsg[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            entries[i] = new AcquireSkillEntryMsg
            {
                SkillId = row.SkillId,
                NextLevel = row.Level,
                MaxLevel = MaxTreeLevel(player.ClassId, row.SkillId, row.Level),
                SpCost = row.LevelUpSp,
                // Mobius Folk.showSkillList passes 0; the previous-level rule is the client's own display hint.
                Requirements = row.Items.Count > 0 ? 2 : 0
            };
        }

        NetBridge.FanoutAcquireSkillList([player.ObjectId], new AcquireSkillListMsg
        {
            SkillType = AcquireSkillTypeUsual,
            Skills = entries
        });
    }

    /// <summary>Mobius <c>AcquireSkillType.USUAL</c> ordinal.</summary>
    internal const int AcquireSkillTypeUsual = 0;

    /// <summary>SystemMessage 276 "You do not have the necessary materials or prerequisites to learn this skill."</summary>
    internal const int PrerequisiteMessageId = 276;

    /// <summary>SystemMessage 2208 "You do not meet the skill level requirements."</summary>
    internal const int SkillLevelRequirementMessageId = 2208;

    /// <summary>Highest <c>level</c> the class chain's tree lists for this skill (Mobius <c>getMaxLevel</c>).</summary>
    int MaxTreeLevel(int classId, int skillId, int fallback)
    {
        int max = fallback;
        IReadOnlyList<int> chain = _game.Classes != null && _game.Classes.TryGet(classId, out _)
            ? _game.Classes.ParentChain(classId)
            : [classId];
        foreach (int chainClassId in chain)
        {
            var tree = _game.SkillTrees.FindClass(chainClassId);
            if (tree == null)
                continue;
            foreach (var e in tree.Skills)
            {
                if (e.SkillId == skillId && e.Level > max)
                    max = e.Level;
            }
        }

        return max;
    }

    /// <summary>
    /// Mobius <c>AcquireSkillInfo</c>: the confirmation page for one offer. Class skills need no items, so the
    /// requirement list is empty (Mobius only fills it for pledge skills and holy-book rows).
    /// </summary>
    void PublishAcquireSkillInfo(ZoneSession session, ZoneEntity player, int skillId, int level, int skillType)
    {
        if (NetBridge == null || session.ObjectId <= 0)
            return;
        var rows = TrainerSkills.Available(_game.SkillTrees, _game.Skills, player.ClassId, player.Level, player.KnownSkills, _game.Classes);
        var row = rows.FirstOrDefault(r => r.SkillId == skillId && r.Level == level);
        if (row.SkillId == 0)
            return;

        // Mobius AcquireSkillInfo fills one requirement row per book (type 99, count, unk 50).
        var requirements = row.Items
            .Select(item => new AcquireSkillRequirementMsg
            {
                Type = 99,
                ItemId = item.ItemId,
                Count = item.Count,
                Unk = 50
            })
            .ToArray();

        NetBridge.FanoutAcquireSkillInfo([player.ObjectId], new AcquireSkillInfoMsg
        {
            SkillId = row.SkillId,
            Level = row.Level,
            SpCost = row.LevelUpSp,
            SkillType = skillType,
            Requirements = requirements
        });
    }

    string? LearnTrainerSkill(ZoneSession session, ZoneEntity player, ZoneEntity npc, int skillId)
    {
        // Free-skill world: nothing is for sale, so a learn request just re-shows the page (Mobius showSkillList).
        if (!TrainerSpLearn)
            return ShowTrainerSkills(session, player, npc);

        var rows = TrainerSkills.Available(_game.SkillTrees, _game.Skills, player.ClassId, player.Level, player.KnownSkills, _game.Classes);
        var row = rows.FirstOrDefault(r => r.SkillId == skillId);
        if (row.SkillId == 0)
        {
            // Mobius RequestAcquireSkill refuses a row that is not on offer and re-sends the list.
            PublishAcquireSkillList(session, player, rows);
            return "bad_request";
        }

        var inventory = player.Inventory!;
        var block = TrainerSkills.Check(row, player.Level, player.Sp, player.KnownSkills, inventory.CountOf);
        if (block != LearnBlock.None)
        {
            // Mobius checkPlayerSkill order: 278 for SP, 276 for a missing prerequisite or item.
            if (block == LearnBlock.Level)
                Notice(session, SkillLevelRequirementMessageId, "You do not meet the skill level requirements.");
            else if (block == LearnBlock.Sp)
                Notice(session, 278, "You do not have enough SP to learn this skill.");
            else
                Notice(session, PrerequisiteMessageId,
                    "You do not have the necessary materials or prerequisites to learn this skill.");
            PublishAcquireSkillList(session, player, rows);
            return null;
        }

        // Mobius consumes the books before SP; the bag update rides with the skill list.
        var touches = new List<BagTouch>();
        foreach (var item in row.Items)
        {
            if (!inventory.TryTake(item.ItemId, item.Count, touches))
                return "bad_request";
        }

        player.Sp -= row.LevelUpSp;
        player.LearnedSkills[row.SkillId] = row.Level;
        RefreshKnownSkills(player);
        _persist.ReplaceLearnedSkills(
            session.Account,
            session.ObjectId,
            player.LearnedSkills.Select(p => new Fineage.Accounts.StoredSkill(p.Key, p.Value)).ToList());
        SaveQuestProgress(session, player); // persists exp/sp like quest rewards
        NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, _clock().ToUnixTimeMilliseconds()));
        string skillName = _game.Skills.Find(row.SkillId)?.Name ?? $"Skill {row.SkillId}";
        // Mobius SystemMessage 277 "You have earned $s1."
        Notice(session, 277, $"You have earned {skillName}.");
        if (touches.Count > 0)
        {
            // Mobius destroyItemByItemId per book: the bag drops before the trainer window re-opens.
            var invUpdate = new InventoryUpdateMsg
            {
                Changes = touches.Select(touch => InventoryMessages.ChangeRow((int)touch.Kind, touch.Item)).ToArray()
            };
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], invUpdate);
            SaveInventory(session, player.Inventory!);
        }

        // Mobius re-sends the list after a learn so the bought row leaves the window.
        var remaining = TrainerSkills.Available(_game.SkillTrees, _game.Skills, player.ClassId, player.Level, player.KnownSkills, _game.Classes);
        PublishAcquireSkillList(session, player, remaining);
        if (remaining.Count > 0 || TrainerSpLearn)
            SendHtml(session, player, npc,
                remaining.Count > 0 ? TrainerListHtml(player, remaining) : NothingToLearnHtml(0));
        return null;
    }

    SkillListMsg SkillListFor(ZoneEntity entity)
    {
        var rows = entity.KnownSkills
            .OrderBy(pair => pair.Key)
            .Select(pair => new KnownSkill(pair.Key, pair.Value))
            .ToList();
        long now = _clock().ToUnixTimeMilliseconds();
        return SkillMessages.List(rows, entity.SkillReuseUntilMs, now);
    }

    string? TrySkillUse(ZoneSession session, SkillUseIntent request)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
            return "world";

        bool skillKnown = player.KnownSkills.TryGetValue(request.SkillId, out int level);
        var template = skillKnown ? _game.Skills.Find(request.SkillId) : null;
        SkillLevelData? levelData = null;
        bool levelDataOk = false;
        if (template != null && template.TryLevelData(level, out var ld))
        {
            levelData = ld;
            levelDataOk = true;
        }
        long now = _clock().ToUnixTimeMilliseconds();
        bool onReuse = player.SkillReuseUntilMs.TryGetValue(request.SkillId, out long reuseUntil)
            && reuseUntil > now;
        // Casting queue is not a refusal — check before reuse/MP so queued casts still land.
        if (IsCasting(player, now))
        {
            if (SkillUseRules.PrecheckError(
                    dead: player.Dead,
                    actionBlockedOrSilenced: IsActionBlocked(player) || IsSilenced(player),
                    skillId: request.SkillId,
                    skillKnown: skillKnown,
                    templateFound: template != null,
                    passive: template?.Passive == true,
                    levelDataOk: levelDataOk,
                    onReuse: false,
                    curMp: player.CurMp,
                    mpConsume: 0) is { } castQueueError)
                return castQueueError;
            QueueSkillAfterCast(session, player, request);
            return null;
        }

        if (SkillUseRules.PrecheckError(
                dead: player.Dead,
                actionBlockedOrSilenced: IsActionBlocked(player) || IsSilenced(player),
                skillId: request.SkillId,
                skillKnown: skillKnown,
                templateFound: template != null,
                passive: template?.Passive == true,
                levelDataOk: levelDataOk,
                onReuse: onReuse,
                curMp: player.CurMp,
                mpConsume: levelData?.MpConsume ?? 0) is { } precheckError)
            return precheckError;

        var liveTemplate = template!;
        var liveLevelData = levelData!;

        // Net timed move: measure range from where the pawn really is now, not the last move order.
        AdvanceMove(player, now);

        if (_spoilRules.IsSpoilSkill(request.SkillId) || _spoilRules.IsSweepSkill(request.SkillId))
            return TrySkillUseSpoilOrSweep(session, player, request, liveTemplate, liveLevelData, level, now,
                _spoilRules.IsSweepSkill(request.SkillId));
        if (liveTemplate.IsBuffSkill)
            return TrySkillUseBuff(session, player, request, liveTemplate, liveLevelData, level, now);
        // Mobius nukes that also apply a short abnormal (Aura Flare A3, Fire Vortex DoT+slow, …)
        // are flagged isDebuff + abnormalTime in skills.json. Prefer skill-combat damage over the
        // pure-CC debuff path so they still roll magic damage; ResolvePendingHit applies the abnormal.
        if (_skillCombat.TryGet(request.SkillId, out _))
            return TrySkillUseAttack(session, player, request, liveTemplate, liveLevelData, level, now);
        if (liveTemplate.IsDebuffSkill)
            return TrySkillUseDebuff(session, player, request, liveTemplate, liveLevelData, level, now);
        if (liveTemplate.IsInstantSupportSkill(level))
            return TrySkillUseSupport(session, player, request, liveTemplate, liveLevelData, level, now);

        return TrySkillUseAttack(session, player, request, liveTemplate, liveLevelData, level, now);
    }

}
