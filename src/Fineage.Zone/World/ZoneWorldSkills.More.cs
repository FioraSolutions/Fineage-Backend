using Fineage.Game;
using Fineage.Game.Economy;
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

        if (row.Items.Count > 0 &&
            !RecordBagChange(
                player,
                LedgerTxKind.TrainerSkillLearn,
                Fees,
                () =>
                {
                    var touches = new List<BagTouch>();
                    foreach (var item in row.Items)
                    {
                        if (!inventory.TryTake(item.ItemId, item.Count, touches))
                            return false;
                    }

                    if (touches.Count > 0)
                    {
                        var invUpdate = new InventoryUpdateMsg
                        {
                            Changes = touches.Select(touch => InventoryMessages.ChangeRow((int)touch.Kind, touch.Item)).ToArray()
                        };
                        NetBridge?.FanoutInventoryUpdate([session.ObjectId], invUpdate);
                        SaveInventory(session, player.Inventory!);
                    }

                    return true;
                },
                _ => row.SkillId))
        {
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
        NetBridge?.FanoutSkillList([player.ObjectId], Fineage.Zone.Net.NetEnterWorldProjector.BuildSkills(player, NowUnixMs()));
        string skillName = _game.Skills.Find(row.SkillId)?.Name ?? $"Skill {row.SkillId}";
        // Mobius SystemMessage 277 "You have earned $s1."
        Notice(session, 277, $"You have earned {skillName}.");
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
        long now = NowUnixMs();
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
        long now = NowUnixMs();
        // A toggle that is already running is switched OFF by this press, and Mobius handles that in
        // checkUseMagicConditions before the reuse / MP gates (isSkillDisabled is checked much later), so the active
        // toggle's own reuse delay must not refuse the second press.
        bool activeToggle = IsActiveToggle(player, template);
        bool onReuse = !activeToggle && player.SkillReuseUntilMs.TryGetValue(request.SkillId, out long reuseUntil)
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
                mpConsume: SkillMpCost(player, template, levelData?.MpConsume ?? 0)) is { } precheckError)
            return precheckError;

        var liveTemplate = template!;
        var liveLevelData = levelData!;

        // Net timed move: measure range from where the pawn really is now, not the last move order.
        AdvanceMove(player, now);

        // Mobius Skill.checkCondition (single <see cref="SkillCastConditionEvaluator"/> path).
        var condTarget = ResolveSkillConditionTarget(session, player, request, liveTemplate);
        if (!SkillCastConditionsMet(session, player, condTarget, liveTemplate, request.SkillId, out int? refuseMsg))
        {
            if (refuseMsg is int msgId)
                SelfLine(session, msgId, msgId == SkillCastConditionRefusal.MultiplePetsOrServitors
                    ? "You may not use multiple pets or servitors at the same time."
                    : $"{liveTemplate.Name} cannot be used due to unsuitable terms.");
            else
                SelfLine(session, SysMsgUnsuitableTerms, $"{liveTemplate.Name} cannot be used due to unsuitable terms.");
            return null;
        }

        // Mobius operateType T: the second press switches the toggle off, otherwise it applies its own abnormal.
        // This branch comes BEFORE the fake-death refuse, mirroring Player.checkUseMagicConditions
        // ("Note: do not check this before TOGGLE reset"): a fake-dead pawn can switch Fake Death (60) off with a
        // second press instead of only through the sit/stand action. Every OTHER toggle keeps Mobius' refusal.
        if (IsToggleSkill(liveTemplate))
        {
            if (player.FakeDeath && request.SkillId != ToggleFakeDeath)
            {
                SelfLine(session, SysMsgCannotUseWhileStanding, "You cannot move while sitting.");
                return ActionProtocol.Blocked;
            }
            return TrySkillUseToggle(session, player, request, liveTemplate, liveLevelData, level, now);
        }

        // Mobius RequestMagicSkillUse: a fake-dead pawn refuses every other skill
        // (YOU_CANNOT_MOVE_WHILE_SITTING + ActionFailed). Only sit/stand, damage or death bring it up.
        if (player.FakeDeath)
        {
            SelfLine(session, SysMsgCannotUseWhileStanding, "You cannot move while sitting.");
            return ActionProtocol.Blocked;
        }

        if (_summonSkills.Find(request.SkillId) is { } summonSkill)
            return TrySkillUseSummon(session, player, request, summonSkill, liveTemplate, liveLevelData, level, now);
        if (Cubics.SummonSkill(request.SkillId) is { } cubicSkill)
            return TrySkillUseCubic(session, player, request, cubicSkill, liveTemplate, liveLevelData, level, now);
        if (IsFishingSkill(request.SkillId))
            return TrySkillUseFishing(session, player, liveTemplate, liveLevelData, level, now);
        if (request.SkillId is SiegeProtocol.SealOfRulerSkillId or SiegeProtocol.BuildHeadquartersSkillId)
            return TrySkillUseSiege(session, player, request, liveTemplate, liveLevelData, level, now);
        if (_spoilRules.IsSpoilSkill(request.SkillId) || _spoilRules.IsSweepSkill(request.SkillId))
            return TrySkillUseSpoilOrSweep(session, player, request, liveTemplate, liveLevelData, level, now,
                _spoilRules.IsSweepSkill(request.SkillId));
        if (_skillEffects.HasResurrection(request.SkillId))
            return TrySkillUseResurrection(session, player, request, liveTemplate, liveLevelData, level, now);

        if (liveTemplate.IsGroundDamageChannel)
            return TrySkillUseChannel(session, player, request, liveTemplate, liveLevelData, level, now);

        // Mobius `isBad()` is `effectPoint < 0 && targetType != SELF`, and RequestMagicSkillUse's target check lets a
        // bad skill pick an attackable victim. Trick (11) / Switch (12) / Bluff (358) / Lure (51) are that family:
        // ONE-target derangement skills with power 0, whose `<for>` payloads (TargetCancel / DeleteHateOfMe /
        // RandomizeHate / AddHate) are the whole behaviour. They must never fall through to the attack handler
        // (which refuses skills missing skill-combat.json as skill_no_combat). Debuff handler when they carry an
        // abnormal, hostile-utility otherwise.
        if (_skillEffects.TryGet(request.SkillId, out var hostileRows) && HasDerangementPayload(hostileRows))
        {
            if (liveTemplate.IsDebuffSkill)
                return TrySkillUseDebuff(session, player, request, liveTemplate, liveLevelData, level, now);
            return TrySkillUseHostileUtility(session, player, request, liveTemplate, liveLevelData, level, now);
        }

        // Mobius nukes that also buff the caster through <selfEffects> (Critical Blow 409, Blinding Blow 321,
        // Evade Shot 369, Aura Flare 1231) are flagged abnormalTime + abnormalType in skills.json while isDebuff
        // stays false. They are still offensive skills, so `_skillCombat` must win over IsBuffSkill: the old order
        // sent Critical Blow down the friendly-buff path, which then refused the hostile monster target.
        if (_skillCombat.TryGet(request.SkillId, out _))
            return TrySkillUseAttack(session, player, request, liveTemplate, liveLevelData, level, now);
        if (liveTemplate.IsBuffSkill)
            return TrySkillUseBuff(session, player, request, liveTemplate, liveLevelData, level, now);
        if (liveTemplate.IsDebuffSkill)
            return TrySkillUseDebuff(session, player, request, liveTemplate, liveLevelData, level, now);
        // Mobius DispelByCategory (Cancellation) / MagicalAttackMp — must not fall into InstantSupport.
        if (_skillEffects.TryDispelByCategory(request.SkillId, out var dispel))
            return TrySkillUseCancel(session, player, request, liveTemplate, liveLevelData, level, now, dispel);
        // Mobius DispelBySlot: Escape Shackle / Break Duress / Cure Poison / Purify / Servitor cures. Vitalize (1020)
        // also heals, so it takes the cleanse first and its Heal comes back through the support path only when the
        // row carries a real power (it does not — 1020's heal is a #amount table, handled by skill-combat power).
        if (_skillEffects.TryDispelBySlot(request.SkillId, out var slotRows))
            return TrySkillUseDispelBySlot(session, player, request, liveTemplate, liveLevelData, level, now, slotRows);
        if (_skillEffects.HasMagicalAttackMp(request.SkillId))
            return TrySkillUseManaBurn(session, player, request, liveTemplate, liveLevelData, level, now);

        // Mobius RestorationRandom (Summon Treasure Key 419) and SummonNpc signets (454–460): self A1 skills whose
        // effect rows are neither a heal nor an attack. Without this they fell into the support/attack fallthrough,
        // which threw on the missing heal power (419) or refused the self target (460).
        if (_skillEffects.TryGet(request.SkillId, out var effectRows) && HasRestorationRandom(effectRows))
            return TrySkillUseRestorationRandom(session, player, request, liveTemplate, liveLevelData, level, now);
        if (SignetSkillIds.Contains(request.SkillId))
            return TrySkillUseSignet(session, player, request, liveTemplate, liveLevelData, level, now);

        if (_skillEffects.TryGet(request.SkillId, out var openRows) && HasOpenChest(openRows))
            return TrySkillUseOpenChest(session, player, request, liveTemplate, liveLevelData, level, now);

        if (_skillEffects.TryGet(request.SkillId, out var scavengeRows) && HasCorpseScavengePayload(scavengeRows) &&
            liveTemplate.TargetType.Equals("CORPSE_MOB", StringComparison.OrdinalIgnoreCase))
            return TrySkillUseLifeScavenge(session, player, request, liveTemplate, liveLevelData, level, now);

        if (liveTemplate.IsInstantSupportSkill(level) || _skillEffects.HasHeal(request.SkillId))
            return TrySkillUseSupport(session, player, request, liveTemplate, liveLevelData, level, now);

        return TrySkillUseAttack(session, player, request, liveTemplate, liveLevelData, level, now);
    }

    static bool HasRestorationRandom(SkillEffectCatalog.Effect[] rows)
    {
        foreach (var row in rows)
        {
            if (row.Name.Equals("RestorationRandom", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Mobius derangement payloads that make an A1 skill hostile despite power 0: <c>DeleteHateOfMe</c> (Trick 11,
    /// Forget 1156, Bluff 358), <c>RandomizeHate</c> (Switch 12, Confusion 2), <c>TargetCancel</c> (Trick/Switch/Bluff)
    /// and <c>Bluff</c> (turn the victim's back). Without this test they fell into <see cref="SkillDefinition.IsInstantSupportSkill"/>
    /// because the datapack says targetType ONE, and the support handler refused the hostile monster.
    /// </summary>
    static bool HasDerangementPayload(SkillEffectCatalog.Effect[] rows)
    {
        foreach (var row in rows)
        {
            if (row.Name.Equals("DeleteHateOfMe", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("RandomizeHate", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("TargetCancel", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("TransferHate", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("AddHate", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("Bluff", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("GetAgro", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("TargetMe", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("CpDamPercent", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("Lethal", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("RunAway", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("Fear", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("Confuse", StringComparison.OrdinalIgnoreCase) ||
                row.Name.Equals("Distrust", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

}
