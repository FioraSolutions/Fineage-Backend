using Fineage.Game.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius <c>Attackable.isRaid()</c> + <c>giveRaidCurse()</c> (defaults true on raid bosses; raid minions follow the master).
    /// </summary>
    static bool RaidTargetGrantsCurse(ZoneEntity npc) =>
        npc.Kind == ZoneEntityKind.Npc &&
        (npc.NpcType is "RaidBoss" or "GrandBoss" || npc.RaidMinion);

    static bool AttackerLevelTriggersRaidCurse(int attackerLevel, int raidLevel) =>
        attackerLevel > raidLevel + ZoneRaidSettings.LevelGap;

    /// <summary>
    /// Mobius <c>CommonSkill.RAID_CURSE</c> / <c>RAID_CURSE2</c> from the raid onto the over-leveled attacker.
    /// Returns true when a curse was applied (caller may stop further combat resolution).
    /// </summary>
    bool TryApplyRaidCurse(ZoneEntity raid, ZoneEntity attacker, bool magicSkill, long now)
    {
        if (ZoneRaidSettings.DisableRaidCurse || !RaidTargetGrantsCurse(raid))
            return false;

        var credit = CreditOf(attacker);
        if (credit.Kind != ZoneEntityKind.Player || credit.Dead || credit.CurHp <= 0)
            return false;
        if (!AttackerLevelTriggersRaidCurse(credit.Level, raid.Level))
            return false;

        int skillId = magicSkill ? ZoneRaidSettings.MagicSkillId : ZoneRaidSettings.PhysicalSkillId;
        var template = _game.Skills.Find(skillId)
                       ?? throw new InvalidOperationException(
                           $"raid curse skillId={skillId} missing from {Fineage.Game.Skills.SkillCatalog.FileName}");
        if (!template.TryLevelData(1, out var levelData))
            throw new InvalidOperationException($"raid curse skillId={skillId} has no level 1 data");

        FanoutSkillLaunched(raid, skillId, 1, credit);
        ApplyAbnormalFromSkill(template, levelData, credit, now, attacker: raid, force: true);

        credit.Intent.AutoAttackTargetId = 0;
        credit.Intent.ClearApproach();
        _meleeHits.RemoveAll(h => h.AttackerId == credit.ObjectId);
        AbortCast(credit, "raid-curse", notify: false);

        ZoneLog.Info(
            $"[raid-curse] skill={skillId} magic={magicSkill} raid={raid.ObjectId} lv{raid.Level} " +
            $"player={credit.ObjectId} lv{credit.Level}");
        return true;
    }

    internal bool TryApplyRaidCurseForTest(ZoneEntity raid, ZoneEntity attacker, bool magicSkill) =>
        TryApplyRaidCurse(raid, attacker, magicSkill, NowUnixMs());
}