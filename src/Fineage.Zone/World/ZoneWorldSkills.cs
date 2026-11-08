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
        var entries = new Fineage.Net.Contract.AcquireSkillEntryNet[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            entries[i] = new Fineage.Net.Contract.AcquireSkillEntryNet
            {
                SkillId = row.SkillId,
                NextLevel = row.Level,
                MaxLevel = MaxTreeLevel(player.ClassId, row.SkillId, row.Level),
                SpCost = row.LevelUpSp,
                // Mobius Folk.showSkillList passes 0; the previous-level rule is the client's own display hint.
                Requirements = row.Items.Count > 0 ? 2 : 0
            };
        }

        NetBridge.FanoutAcquireSkillList([player.ObjectId], new Fineage.Net.Contract.AcquireSkillListNetMsg
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
            .Select(item => new Fineage.Net.Contract.AcquireSkillRequirementNet
            {
                Type = 99,
                ItemId = item.ItemId,
                Count = item.Count,
                Unk = 50
            })
            .ToArray();

        NetBridge.FanoutAcquireSkillInfo([player.ObjectId], new Fineage.Net.Contract.AcquireSkillInfoNetMsg
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
        if (player.Dead)
            return SkillProtocol.Dead;
        if (IsActionBlocked(player) || IsSilenced(player))
            return SkillProtocol.Muted;
        if (request.SkillId <= 0)
            return "bad_request";

        if (!player.KnownSkills.TryGetValue(request.SkillId, out int level))
            return SkillProtocol.Unknown;

        var template = _game.Skills.Find(request.SkillId);
        if (template == null || template.Passive)
            return template == null ? SkillProtocol.Unknown : SkillProtocol.Passive;
        if (!template.TryLevelData(level, out var levelData))
            return SkillProtocol.Unknown;

        long now = _clock().ToUnixTimeMilliseconds();
        if (IsCasting(player, now))
        {
            QueueSkillAfterCast(session, player, request);
            return null;
        }
        // Net timed move: measure range from where the pawn really is now, not the last move order.
        AdvanceMove(player, now);
        if (player.SkillReuseUntilMs.TryGetValue(request.SkillId, out long reuseUntil) && reuseUntil > now)
            return SkillProtocol.Reuse;

        if (player.CurMp < levelData.MpConsume)
            return SkillProtocol.Mp;

        if (_spoilRules.IsSpoilSkill(request.SkillId) || _spoilRules.IsSweepSkill(request.SkillId))
            return TrySkillUseSpoilOrSweep(session, player, request, template, levelData, level, now,
                _spoilRules.IsSweepSkill(request.SkillId));
        if (template.IsBuffSkill)
            return TrySkillUseBuff(session, player, request, template, levelData, level, now);
        // Mobius nukes that also apply a short abnormal (Aura Flare A3, Fire Vortex DoT+slow, …)
        // are flagged isDebuff + abnormalTime in skills.json. Prefer skill-combat damage over the
        // pure-CC debuff path so they still roll magic damage; ResolvePendingHit applies the abnormal.
        if (_skillCombat.TryGet(request.SkillId, out _))
            return TrySkillUseAttack(session, player, request, template, levelData, level, now);
        if (template.IsDebuffSkill)
            return TrySkillUseDebuff(session, player, request, template, levelData, level, now);
        if (template.IsInstantSupportSkill(level))
            return TrySkillUseSupport(session, player, request, template, levelData, level, now);

        return TrySkillUseAttack(session, player, request, template, levelData, level, now);
    }

    public const int SysMsgUnsuitableTerms = 113;

    string? TrySkillUseAttack(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        // Mobius Skill.checkCondition: <using kind=..> fails → SysMsg 113 "<skill> cannot be used due to unsuitable terms."
        if (_skillCombat.TryGet(request.SkillId, out var cond) && cond.UsingKinds.Length > 0 && !Wearing(player, cond.UsingKinds))
        {
            ShotNotice(session, SysMsgUnsuitableTerms, $"{cond.Name} cannot be used due to unsuitable terms.");
            return null;
        }
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0)
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (!IsAttackable(player, target))
            return SkillProtocol.Target;
        if (!VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;

        bool inRange = template.CastRange > 0
            ? ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange + 100)
            : ZoneCombat.InMeleeRange(player.X, player.Y, target.X, target.Y);
        if (!inRange)
        {
            // Mobius AI_INTENTION_CAST: moveToPawn(target, castRange) then cast on arrival instead of refusing.
            int reach = template.CastRange > 0 ? Math.Max(40, template.CastRange) : ZoneCombat.MeleeRange;
            long travelMs = ApproachTarget(player, target, reach);
            if (travelMs < 0)
                return SkillProtocol.Range;
            session.AutoAttackTargetId = 0;
            session.SelectedTargetId = targetId;
            session.PendingCastSkillId = request.SkillId;
            session.PendingCastTargetId = targetId;
            session.PendingCastAtUnixMs = now + travelMs;
            return null;
        }

        session.AutoAttackTargetId = 0;
        // Mobius Creature.doCast / Skill.checkCondition: target out of sight → SysMsg 181, no cast.
        if (!CanSeeTarget(player, target))
        {
            CannotSeeTarget(session);
            return null;
        }
        if (_skillCombat.TryGet(request.SkillId, out var combat))
            return CastMobiusAttack(session, player, target, request.SkillId, level, combat, now);

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        // Legacy path: skills missing from skill-combat.json keep the old power stub.
        int damage = Math.Max(1, levelData.Power + ZoneCombat.LegacySkillDamageBonus);
        int hitTime = Math.Max(0, template.HitTimeMs);

        // Mobius Creature.doCast: MagicSkillUse goes out now, damage lands in onMagicHitTimer after hitTime.
        var cast = BuildCast(player, target, request.SkillId, level, template);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));

        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        session.SelectedTargetId = targetId;
        player.CastingUntilMs = now + hitTime;
        SetPendingHit(player, new PendingSkillHit(targetId, damage, now + hitTime));
        if (hitTime == 0)
            ResolvePendingHit(player, now);

        return null;
    }

    string? TrySkillUseBuff(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        int targetId = ResolveFriendlyTargetId(session, player, request, template);
        if (targetId <= 0)
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (target.Kind != ZoneEntityKind.Player || target.Dead || target.CurHp <= 0)
            return SkillProtocol.Target;
        if (targetId != player.ObjectId && !VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        var cast = BuildCast(player, target, request.SkillId, level, template);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, template.HitTimeMs);
        // Mobius onMagicHitTimer: the buff lands at hitTime (interruptible for magic/static skills).
        ScheduleEffect(player, target, template, level, now, template.HitTimeMs,
            (t, at) => ApplyAbnormalFromSkill(template, levelData, t, at));
        return null;
    }

    /// <summary>
    /// Instant heal/utility (Elemental Heal, Heal, Self Heal, …). Datapack heal power is often 0 —
    /// use a Mobius-ish fallback so SELF heals work with a mob selected.
    /// </summary>
    string? TrySkillUseSupport(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        int targetId = ResolveFriendlyTargetId(session, player, request, template);
        if (targetId <= 0)
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (target.Kind != ZoneEntityKind.Player || target.Dead)
            return SkillProtocol.Target;
        if (targetId != player.ObjectId && !VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (template.CastRange > 0 &&
            targetId != player.ObjectId &&
            !ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        int heal = SupportHealAmount(levelData, level, player.Level);
        var cast = BuildCast(player, target, request.SkillId, level, template);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, template.HitTimeMs);
        // Mobius Heal effect at hitTime: HP rises when the cast completes, not when it starts.
        ScheduleEffect(player, target, template, level, now, template.HitTimeMs, (t, _) =>
        {
            if (heal > 0 && t.MaxHp > 0)
                t.CurHp = Math.Min(t.MaxHp, t.CurHp + heal);
            FanoutVitals(t, new StatusUpdateMsg
            {
                ObjectId = t.ObjectId,
                AttributeIds = [InterludeStatus.CurHp, InterludeStatus.MaxHp],
                Values = [t.CurHp, t.MaxHp]
            });
        });
        return null;
    }

    static int SupportHealAmount(SkillLevelData levelData, int skillLevel, int playerLevel)
    {
        if (levelData.Power > 0)
            return levelData.Power;
        // Datapack omitted heal power; scale by skill level and character level until import is fixed.
        return Math.Max(25, 18 + skillLevel * 10 + Math.Max(1, playerLevel) * 4);
    }

    string? TrySkillUseDebuff(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0)
            return SkillProtocol.Target;
        if (!_byId.TryGetValue(targetId, out var target))
            return SkillProtocol.Target;
        if (!IsAttackable(player, target))
            return SkillProtocol.Target;
        if (!VisibleTo(player.ObjectId, targetId))
            return SkillProtocol.Target;
        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, template.CastRange))
            return SkillProtocol.Range;

        player.CurMp = Math.Max(0, player.CurMp - levelData.MpConsume);
        if (template.ReuseDelayMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + template.ReuseDelayMs;

        var cast = BuildCast(player, target, request.SkillId, level, template);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        player.CastingUntilMs = now + Math.Max(0, template.HitTimeMs);
        session.SelectedTargetId = targetId;
        // Mobius onMagicHitTimer: debuff lands at hitTime; a dead/escaped/hidden target aborts it.
        ScheduleEffect(player, target, template, level, now, template.HitTimeMs,
            (t, at) => ApplyAbnormalFromSkill(template, levelData, t, at));
        return null;
    }

    /// <summary>
    /// Friendly skills: SELF always self. Otherwise prefer an explicit player target; if the client
    /// sent a mob (selected target), fall back to self so Heal works without clearing the target.
    /// </summary>
    int ResolveFriendlyTargetId(ZoneSession session, ZoneEntity player, SkillUseIntent request, SkillDefinition template)
    {
        if (string.Equals(template.TargetType, "SELF", StringComparison.OrdinalIgnoreCase))
            return player.ObjectId;

        int requested = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (requested > 0 &&
            _byId.TryGetValue(requested, out var candidate) &&
            candidate.Kind == ZoneEntityKind.Player &&
            !candidate.Dead)
            return requested;

        return player.ObjectId;
    }

    static MagicSkillUseMsg BuildCast(
        ZoneEntity caster,
        ZoneEntity target,
        int skillId,
        int level,
        SkillDefinition template) => new()
        {
            CasterObjectId = caster.ObjectId,
            TargetObjectId = target.ObjectId,
            SkillId = skillId,
            SkillLevel = level,
            HitTimeMs = template.HitTimeMs,
            ReuseDelayMs = template.ReuseDelayMs
        };

    static StatusUpdateMsg CasterMpStatus(ZoneEntity player) => new()
    {
        ObjectId = player.ObjectId,
        AttributeIds = [InterludeStatus.CurMp, InterludeStatus.MaxMp],
        Values = [player.CurMp, player.MaxMp]
    };

    void FanoutSkill(
        ZoneEntity caster,
        ZoneEntity target,
        MagicSkillUseMsg cast,
        StatusUpdateMsg? targetStatus,
        StatusUpdateMsg casterMp)
    {
        List<int>? netWatchers = null;
        foreach (var watcher in Players())
        {
            if (watcher.Session == null)
                continue;
            bool cares = ReferenceEquals(watcher, caster) ||
                         watcher.Knownlist.Contains(target.ObjectId) ||
                         watcher.Knownlist.Contains(caster.ObjectId);
            if (!cares)
                continue;

            netWatchers ??= [];
            netWatchers.Add(watcher.ObjectId);
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
        {
            NetBridge.FanoutMagicSkillUse(netWatchers, new Fineage.Net.Contract.MagicSkillUseNetMsg
            {
                CasterObjectId = cast.CasterObjectId,
                TargetObjectId = cast.TargetObjectId,
                SkillId = cast.SkillId,
                SkillLevel = cast.SkillLevel,
                HitTimeMs = cast.HitTimeMs,
                ReuseDelayMs = cast.ReuseDelayMs
            });
            if (targetStatus != null)
            {
                NetBridge.FanoutStatusUpdate(netWatchers, new Fineage.Net.Contract.StatusUpdateNetMsg
                {
                    EntityValue = (ulong)(uint)targetStatus.ObjectId,
                    EntityGeneration = 1,
                    AttributeIds = targetStatus.AttributeIds ?? [],
                    Values = targetStatus.Values ?? []
                });
            }

            NetBridge.FanoutStatusUpdate([caster.ObjectId], new Fineage.Net.Contract.StatusUpdateNetMsg
            {
                EntityValue = (ulong)(uint)casterMp.ObjectId,
                EntityGeneration = 1,
                AttributeIds = casterMp.AttributeIds ?? [],
                Values = casterMp.Values ?? []
            });
        }
    }
}
