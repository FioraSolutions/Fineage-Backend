using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius <c>OpenChest</c> (Unlock 27, deluxe/box keys 2065/2229, 2322, 3155) and treasure-box handling.
/// Reimplemented from the Interlude behaviour, not copied from the GPLv3 reference.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius SocialAction on a successful chest open.</summary>
    public const int SocialChestOpenOk = 3;

    /// <summary>Mobius SocialAction when the open fails, or when a mimic is provoked.</summary>
    public const int SocialChestOpenFail = 13;

    /// <summary>Real boxes that Unlock can open (Mobius Chest box id range 18265–18298).</summary>
    public static bool IsTreasureBox(int npcId) => npcId is >= 18265 and <= 18298;

    internal static bool IsChestNpc(ZoneEntity target) =>
        target.Kind == ZoneEntityKind.Npc &&
        target.NpcType.Equals("Chest", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Open chance for a box, in percent (can be ≤ 0). Interlude: deluxe/box keys 2065/2229 lose 40 % per
    /// grade step away from <c>chestLevel / 10</c> (2065 starts at 60 %, 2229 at 100 %); 3155 is 90 %;
    /// Unlock 27 and the other rogue open skills always succeed.
    /// </summary>
    public static int OpenChestSuccessChance(int skillId, int skillLevel, int chestLevel)
    {
        if (skillId is 2065 or 2229)
        {
            int gradesOff = Math.Abs((chestLevel / 10) - Math.Max(1, skillLevel));
            int baseline = skillId == 2065 ? 60 : 100;
            return baseline - gradesOff * 40;
        }

        if (skillId == 3155)
            return 90;
        return 100;
    }

    /// <summary>
    /// Drop table used when the chest dies. A successful Unlock keeps the box's own id. A smash or failed
    /// melee kill remaps 18265–18286 onto 21801–21822, and the later box ids onto the matching mimic tables.
    /// </summary>
    public static int ChestDropNpcId(int npcId, bool specialDrop)
    {
        if (specialDrop)
            return npcId;
        if (npcId is >= 18265 and <= 18286)
            return npcId + 3536;
        return npcId switch
        {
            18287 or 18288 => 21671,
            18289 or 18290 => 21694,
            18291 or 18292 => 21717,
            18293 or 18294 => 21740,
            18295 or 18296 => 21763,
            18297 or 18298 => 21786,
            _ => npcId
        };
    }

    static bool HasOpenChest(SkillEffectCatalog.Effect[] rows)
    {
        foreach (var row in rows)
        {
            if (row.Name.Equals("OpenChest", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Unlock / box-key A1: the target must be a Chest NPC. Thief Keys are refused up front and consumed
    /// when the hit lands. OpenDoor is not implemented in this pass.
    /// </summary>
    string? TrySkillUseOpenChest(
        ZoneSession session,
        ZoneEntity player,
        SkillUseIntent request,
        SkillDefinition template,
        SkillLevelData levelData,
        int level,
        long now)
    {
        int targetId = request.TargetObjectId > 0 ? request.TargetObjectId : session.SelectedTargetId;
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target))
        {
            IncorrectTarget(session);
            return null;
        }

        // OpenDoor is not implemented in this pass — only Chest NPCs are valid Unlock targets.
        if (!IsChestNpc(target) || target.Dead || target.CurHp <= 0 || target.ChestInteracted)
        {
            IncorrectTarget(session);
            return null;
        }

        if (player.Dead)
            return SkillProtocol.Dead;
        if (!VisibleTo(player.ObjectId, targetId))
        {
            IncorrectTarget(session);
            return null;
        }

        if (!ZoneCombat.InCastRange(player.X, player.Y, target.X, target.Y, Math.Max(40, template.CastRange)))
        {
            int reach = Math.Max(40, template.CastRange);
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

        if (!CanSeeTarget(player, target))
        {
            CannotSeeTarget(session);
            return null;
        }

        if (ConsumeItemBlocked(session, player, levelData))
            return null;

        session.AutoAttackTargetId = 0;
        ConsumeSkillMp(player, template, levelData.MpConsume);
        var (hitMs, reuseMs) = ScaledSkillTimes(player, template, level);
        if (reuseMs > 0)
            player.SkillReuseUntilMs[request.SkillId] = now + reuseMs;

        var cast = BuildCast(player, target, request.SkillId, level, hitMs, reuseMs);
        StopForCast(player, target, now);
        FanoutSkill(player, target, cast, targetStatus: null, CasterMpStatus(player));
        player.CastingUntilMs = now + Math.Max(0, hitMs);
        session.SelectedTargetId = targetId;

        ScheduleEffect(player, target, template, level, now, hitMs, (t, at) =>
        {
            if (t.Dead || t.CurHp <= 0)
                return;
            if (!ConsumeSkillItems(session, player, levelData))
                return;
            ApplyOpenChest(player, t, request.SkillId, level);
        });
        return null;
    }

    /// <summary>
    /// Open a chest the moment the skill lands. A mimic cannot be unlocked and turns on the caster instead.
    /// A box that is already interacted is ignored. Success kills it with its own drop table and no exp;
    /// failure despawns it with no loot.
    /// </summary>
    void ApplyOpenChest(ZoneEntity caster, ZoneEntity target, int skillId, int skillLevel, bool? forceSuccess = null)
    {
        if (!IsChestNpc(target) || target.Dead || target.CurHp <= 0 || target.ChestInteracted)
            return;
        if (caster.Kind != ZoneEntityKind.Player || caster.Session == null)
            return;

        target.ChestInteracted = true;

        if (!IsTreasureBox(target.NpcId))
        {
            BroadcastSocial(target, SocialChestOpenFail);
            AddHate(target, caster, hate: 1);
            return;
        }

        int chance = OpenChestSuccessChance(skillId, skillLevel, target.Level);
        bool success = forceSuccess ?? (_combatRandom.Next(100) < chance);
        if (success)
        {
            BroadcastSocial(target, SocialChestOpenOk);
            target.ChestSpecialDrop = true;
            OpenChestKill(target, caster);
            return;
        }

        BroadcastSocial(target, SocialChestOpenFail);
        DespawnNpc(target);
    }

    void OpenChestKill(ZoneEntity chest, ZoneEntity opener)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        int damage = Math.Max(1, chest.CurHp);
        RecordDamage(chest, opener, damage);
        chest.CurHp = 0;
        FanoutVitals(chest, StatusFor(chest));
        opener.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        RemoveDeadNpc(chest, killer: opener);
        GrantKillReward(opener, chest);
    }

    void BroadcastSocial(ZoneEntity actor, int actionId)
    {
        if (NetBridge == null)
            return;
        NetBridge.FanoutSocialAction(KnownNetPlayersOf(actor), actor.ObjectId, actionId);
    }

    /// <summary>
    /// First melee or non-unlock hit on a still-closed box: it bursts (smash drop table, no special loot).
    /// A mimic is left alone and fights like any other monster.
    /// </summary>
    bool TryExplodeTreasureBox(ZoneEntity attacker, ZoneEntity target, long now)
    {
        if (!IsTreasureBox(target.NpcId) || target.ChestInteracted || target.CurHp <= 0)
            return false;
        target.ChestInteracted = true;
        target.ChestSpecialDrop = false;
        target.CurHp = 0;
        FanoutVitals(target, StatusFor(target));
        var credit = CreditOf(attacker);
        RecordDamage(target, credit, Math.Max(1, target.MaxHp));
        attacker.CombatUntilUnixMs = now + ClanProtocol.CombatStanceMillis;
        RemoveDeadNpc(target, killer: credit);
        GrantKillReward(credit, target);
        return true;
    }

    /// <summary>Test hook: run the open payload (keys already assumed spent).</summary>
    internal void ApplyOpenChestForTest(ZoneEntity caster, ZoneEntity chest, int skillId, int skillLevel, bool? forceSuccess = null)
    {
        lock (_gate)
            ApplyOpenChest(caster, chest, skillId, skillLevel, forceSuccess);
    }
}
