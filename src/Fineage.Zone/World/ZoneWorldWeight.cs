using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>
/// Inventory overload: load limit, penalty level, and the client notification (Mobius
/// <c>Player.refreshOverloaded</c>, <c>Player.java:1970-2013</c>).
/// <para>
/// The level comes from <see cref="WeightPenalty.Level"/> over
/// <c>(Inventory.TotalWeight − Stat.WEIGHT_PENALTY) * 1000 / getMaxLoad()</c>. Mobius applies it by adding passive
/// skill 4270 at that level (<c>Player.java:2005</c>: <c>addSkill(getSkill(4270, newWeightPenalty))</c>), whose
/// <c>runSpd</c>/<c>regHp</c>/<c>regMp</c> funcs then fold through the normal stat calculators.
/// </para>
/// <para>
/// Here the same XML funcs are folded as a <b>virtual passive source</b> (<see cref="FoldWeightPenalty"/>) instead of
/// writing 4270 into <see cref="ZoneEntity.KnownSkills"/>: that dictionary is the persisted character skill book and
/// <see cref="RefreshKnownSkills"/> rebuilds it from the skill tree on every class change / trainer purchase, which
/// would silently drop the injected level. The funcs still come from <c>data/skill-passives.json</c> and still go
/// through <see cref="CombatStatMods"/>, so buffs, masteries and the penalty compose in one MOVE_SPEED evaluation.
/// </para>
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>getCurrentLoad()</c> (<c>Creature.java:6302-6309</c>): inventory template weight × count.</summary>
    internal static long CurrentLoadOf(ZoneEntity player) => player.Inventory?.TotalWeight ?? 0;

    /// <summary>
    /// Mobius <c>getBonusWeightPenalty()</c> (<c>Creature.java:6290-6297</c>):
    /// <c>(int) calcStat(Stat.WEIGHT_PENALTY, 1, …)</c>. Armor sets add ~5760–5795 so the penalty starts later.
    /// </summary>
    internal long BonusWeightPenaltyOf(ZoneEntity player) =>
        (long)PlayerStatMods(player).Apply(CombatStat.WeightPenalty, 1d);

    /// <summary>
    /// Mobius <c>getMaxLoad()</c> (<c>Creature.java:6278-6288</c>):
    /// <c>(int) calcStat(Stat.WEIGHT_LIMIT, floor(CON bonus * 69000 * AltWeightLimit), …)</c>.
    /// <c>weightLimit</c> passives (Weight Limit 150 ×2, Quiver of Holding, robe-set enchants ×1.3) fold here, so the
    /// shop / warehouse guards and the penalty level share one limit.
    /// </summary>
    internal int MaxLoadOf(ZoneEntity player)
    {
        double bonus = 1;
        if (PlayerClasses.TryGet(player.ClassId, out var template) && template != null)
            bonus = StatBonus(PlayerClasses.StatBonuses, StatSlot.Con, template.Con + player.HennaCon);
        int baseLoad = WeightPenalty.BaseMaxLoad(bonus, ZoneWeightSettings.AltWeightLimit);
        return (int)PlayerStatMods(player).Apply(CombatStat.WeightLimit, (double)baseLoad);
    }

    /// <summary>The level <see cref="WeightPenalty.Level"/> would return right now, without storing it.</summary>
    internal int WeightPenaltyLevelOf(ZoneEntity player) =>
        WeightPenalty.Level(CurrentLoadOf(player), MaxLoadOf(player), BonusWeightPenaltyOf(player));

    /// <summary>
    /// Mobius <c>refreshOverloaded</c>: recompute the level and, when it moved, refresh the live speeds and tell the
    /// client (<c>Player.java:2007-2012</c>: <c>broadcastUserInfo()</c> + <c>sendPacket(new EtcStatusUpdate(this))</c>).
    /// The run/walk gain is applied because <see cref="PlayerStatMods"/> folds 4270 into MOVE_SPEED.
    /// </summary>
    /// <returns>True when the penalty level changed.</returns>
    internal bool RefreshWeightPenalty(ZoneEntity player)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Inventory == null)
            return false;

        int level = WeightPenaltyLevelOf(player);
        if (level == player.WeightPenaltyLevel)
            return false;

        int before = player.WeightPenaltyLevel;
        player.WeightPenaltyLevel = level;
        ZoneLog.Info($"[weight] obj={player.ObjectId} penalty={before}->{level} load={CurrentLoadOf(player)} max={MaxLoadOf(player)}");

        // Same fanout as a buff that changes runSpd: recompute, then ChangeMoveType to self + knownlist. Level 1 keeps
        // runSpd ×1.0, so it legitimately sends no ChangeMoveType — only the EtcStatusUpdate below changes.
        RefreshRunSpeedAndFanout(player);
        FanoutNetEtcStatus(player);
        return true;
    }

    /// <summary>
    /// Mobius <c>sendPacket(new EtcStatusUpdate(this))</c> (<c>Player.java:2011</c>): the packet is self-only, so the
    /// watcher list is just the owner. Skipped before the session is in world — enter sends its own copy.
    /// </summary>
    void FanoutNetEtcStatus(ZoneEntity player)
    {
        if (NetBridge == null || player.Session is not { InWorld: true })
            return;
        NetBridge.FanoutEtcStatusUpdate([player.ObjectId], EtcStatusOf(player));
    }

    /// <summary>Net enter path: the same EtcStatusUpdate a client would get from <c>refreshOverloaded</c>.</summary>
    internal bool TryBuildNetEtcStatus(ZoneSession session, out Fineage.Protocol.EtcStatusUpdateMsg status)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(session.ObjectId, out var e) && e.Kind == ZoneEntityKind.Player && e.Inventory != null)
            {
                status = EtcStatusOf(e);
                return true;
            }
        }
        status = null!;
        return false;
    }

    /// <summary>
    /// Hook for every inventory write. Mobius recomputes the penalty from
    /// <c>PlayerInventory.refreshWeight()</c> → <c>_owner.refreshOverloaded()</c>
    /// (<c>PlayerInventory.java:749-753</c>), which every add/remove/equip path funnels through; our writes funnel
    /// through <see cref="SaveInventory"/>, so that is where the recompute lives.
    /// </summary>
    void RefreshWeightPenaltyAfterInventoryChange(ZoneSession session)
    {
        if (!_byId.TryGetValue(session.ObjectId, out var entity))
            return;
        if (entity.Kind != ZoneEntityKind.Player || !ReferenceEquals(entity.Session, session))
            return;
        RefreshWeightPenalty(entity);
    }

    /// <summary>
    /// Mobius <c>EtcStatusUpdate</c> body (<c>EtcStatusUpdate.java:32-45</c>). Fields this server does not model yet
    /// (force charges, chat ban, danger area, charm of courage, death penalty) go out as 0, exactly like a clean
    /// character in Mobius.
    /// </summary>
    internal static Fineage.Protocol.EtcStatusUpdateMsg EtcStatusOf(ZoneEntity player) => new()
    {
        Charges = 0,
        WeightPenalty = player.WeightPenaltyLevel,
        ChatBlocked = false,
        DangerArea = false,
        // Mobius writes 1 when either expertise penalty is above zero, not the level itself.
        GradePenalty = player.WeaponGradePenalty > 0 || player.ArmorGradePenalty > 0,
        CharmOfCourage = false,
        DeathPenalty = player.DeathPenaltyLevel
    };

    /// <summary>
    /// Folds skill 4270 at <see cref="ZoneEntity.WeightPenaltyLevel"/> into <paramref name="mods"/>
    /// (Mobius <c>addSkill(4270, level)</c>). Levels 1–4 are <c>runSpd</c> ×1/×0.5/×0.5/×0 and every level halves
    /// <c>regHp</c>/<c>regMp</c>; level 4 is a legal 0 speed.
    /// </summary>
    CombatStatMods FoldWeightPenalty(ZoneEntity player, CombatStatMods? mods)
    {
        int level = player.WeightPenaltyLevel;
        if (level <= 0 || !_passives.TryGet(WeightPenalty.SkillId, out var effect))
            return mods ?? CombatStatMods.None;

        // CombatStatMods.None is a shared static: never accumulate into it.
        var target = mods == null || ReferenceEquals(mods, CombatStatMods.None) ? new CombatStatMods() : mods;
        foreach (var func in effect.Funcs)
            target.Add(func, level);
        return target;
    }

    /// <summary>
    /// Folds skill 4267 at <see cref="ZoneEntity.GradePenaltyLevel"/> into <paramref name="mods"/>
    /// (Mobius <c>Player.refreshExpertisePenalty</c> → <c>addSkill(GRADE_PENALTY, max(weapon, armor))</c>,
    /// <c>Player.java:2059</c>). Levels 1–4 scale <c>pAtkSpd</c>, <c>mAtkSpd</c>, <c>rEvas</c>, <c>runSpd</c>,
    /// <c>regHp</c> and <c>regMp</c> by 0.22 / 0.19 / 0.16 / 0.13 and subtract 20 accuracy, so gear worn above your
    /// expertise really does slow and weaken the character instead of only drawing a flag in the window.
    /// </summary>
    CombatStatMods FoldGradePenalty(ZoneEntity player, CombatStatMods? mods)
    {
        int level = player.GradePenaltyLevel;
        if (level <= 0 || !_passives.TryGet(GradePenalty.SkillId, out var effect))
            return mods ?? CombatStatMods.None;

        var target = mods == null || ReferenceEquals(mods, CombatStatMods.None) ? new CombatStatMods() : mods;
        foreach (var func in effect.Funcs)
            target.Add(func, level);
        return target;
    }

    /// <summary>
    /// Folds skill 5076 at <see cref="ZoneEntity.DeathPenaltyLevel"/> into <paramref name="mods"/>
    /// (Mobius <c>Player.increaseDeathPenaltyBuffLevel</c> → <c>addSkill(DEATH_PENALTY, level)</c>,
    /// <c>Player.java:11718-11730</c>). Unlike 4267/4270 this one is a real entry in the skill book on live Mobius,
    /// but it is stored outside the trainer list, so it is added as a virtual source here exactly like the other two.
    /// Levels 1–15 scale <c>pAtk</c>/<c>pAtkSpd</c>/<c>mAtk</c>/<c>mAtkSpd</c>/<c>runSpd</c> down, <c>pDef</c>/<c>mDef</c>
    /// down, and subtract <c>accCombat</c>/<c>rEvas</c>.
    /// </summary>
    CombatStatMods FoldDeathPenalty(ZoneEntity player, CombatStatMods? mods)
    {
        int level = Math.Clamp(player.DeathPenaltyLevel, 0, DeathRuleCatalog.MaxDeathPenaltyLevel);
        if (level <= 0 || !_passives.TryGet(DeathPenalty.SkillId, out var effect))
            return mods ?? CombatStatMods.None;

        var target = mods == null || ReferenceEquals(mods, CombatStatMods.None) ? new CombatStatMods() : mods;
        foreach (var func in effect.Funcs)
            target.Add(func, level);
        return target;
    }
}
