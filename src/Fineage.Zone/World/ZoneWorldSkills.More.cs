using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
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
        // Same union as NetEnterWorldProjector.BuildSkills (known + clan + gear).
        var skills = PlayerPassiveSources.Merge(entity.KnownSkills, entity.ClanSkills, entity.GearSkills);
        var rows = skills
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
        if (_skillEffects.HasResurrection(request.SkillId))
            return TrySkillUseResurrection(session, player, request, liveTemplate, liveLevelData, level, now);
        if (liveTemplate.IsBuffSkill)
            return TrySkillUseBuff(session, player, request, liveTemplate, liveLevelData, level, now);
        // Mobius nukes that also apply a short abnormal (Aura Flare A3, Fire Vortex DoT+slow, …)
        // are flagged isDebuff + abnormalTime in skills.json. Prefer skill-combat damage over the
        // pure-CC debuff path so they still roll magic damage; ResolvePendingHit applies the abnormal.
        if (_skillCombat.TryGet(request.SkillId, out _))
            return TrySkillUseAttack(session, player, request, liveTemplate, liveLevelData, level, now);
        if (liveTemplate.IsDebuffSkill)
            return TrySkillUseDebuff(session, player, request, liveTemplate, liveLevelData, level, now);
        // Mobius DispelByCategory (Cancellation) / MagicalAttackMp — must not fall into InstantSupport.
        if (_skillEffects.TryDispelByCategory(request.SkillId, out var dispel))
            return TrySkillUseCancel(session, player, request, liveTemplate, liveLevelData, level, now, dispel);
        if (_skillEffects.HasMagicalAttackMp(request.SkillId))
            return TrySkillUseManaBurn(session, player, request, liveTemplate, liveLevelData, level, now);
        if (liveTemplate.IsInstantSupportSkill(level) || _skillEffects.HasHeal(request.SkillId))
            return TrySkillUseSupport(session, player, request, liveTemplate, liveLevelData, level, now);

        return TrySkillUseAttack(session, player, request, liveTemplate, liveLevelData, level, now);
    }

}
