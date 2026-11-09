using Fineage.Game;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>CreatureStat.getSwimWalkSpeed()</c> (<c>CreatureStat.java:465-473</c>).</summary>
    internal float SwimWalkSpeedStat(ZoneEntity entity, CombatStatMods mods, int runSpeedBoost = 0)
    {
        float baseSpeed = SwimWalkBaseOf(entity);
        return baseSpeed <= 0 ? 0f : FoldSpeed(entity, baseSpeed, mods, runSpeedBoost);
    }

    /// <summary>
    /// One <c>Stat.MOVE_SPEED</c> evaluation in Mobius order:
    /// <list type="number">
    /// <item><c>FuncMoveSpeed</c> × DEX bonus — order 1, so it runs before every XML func
    /// (<c>FuncMoveSpeed.java:34-45</c>). Registered only for players and summons
    /// (<c>Formulas.java:206,234</c>); an NPC's calculator set has no <c>MOVE_SPEED</c> entry
    /// (<c>Formulas.getStdNPCCalculators</c>), so NPC bases are never DEX-scaled.</item>
    /// <item>XML funcs by <c>order</c>: <c>SET</c> 0 → <c>DIV</c>/<c>MUL</c> 20 → <c>ADD</c>/<c>SUB</c> 30
    /// (<c>StatFunction.java:24-31</c>).</item>
    /// <item>Players only: <c>+Config.RUN_SPD_BOOST</c> then the <c>MaxRunSpeed</c> cap
    /// (<c>PlayerStat.java:512-525</c>, <c>:540-553</c>).</item>
    /// </list>
    /// There is <b>no lower floor</b>: <c>MOVE_SPEED</c> is absent from the <c>calcStat</c> "never ≤ 0" switch
    /// (<c>CreatureStat.java:99-125</c>), and 0 is exactly what Weight Penalty 4 produces (<c>runSpd</c> ×0).
    /// </summary>
    float FoldSpeed(ZoneEntity entity, float baseSpeed, CombatStatMods mods, int runSpeedBoost)
    {
        bool player = entity.Kind == ZoneEntityKind.Player;
        double speed = player
            ? baseSpeed * EffectiveAttributes(entity).DexBonus
            : baseSpeed;
        speed = mods.Apply(CombatStat.RunSpd, speed);
        if (player)
        {
            speed += runSpeedBoost;
            if (speed > ZoneCombat.MaxRunSpeed)
                speed = ZoneCombat.MaxRunSpeed;
        }
        return (float)speed;
    }

    /// <summary>Mobius <c>CreatureTemplate.getBaseMoveSpeed(RUN)</c>, default 120 (<c>CreatureTemplate.java:145</c>).</summary>
    static float RunBaseOf(ZoneEntity entity) =>
        entity.BaseRunSpeed > 0 ? entity.BaseRunSpeed : ZoneCombat.DefaultPlayerRunSpeed;

    /// <summary>Mobius <c>getBaseMoveSpeed(WALK)</c>, default 50 (<c>CreatureTemplate.java:146</c>).</summary>
    static float WalkBaseOf(ZoneEntity entity) =>
        entity.BaseWalkSpeed > 0 ? entity.BaseWalkSpeed : ZoneCombat.DefaultWalkSpeed;

    /// <summary>
    /// <c>baseSwimRunSpd</c> falls back to <c>baseRunSpd</c> when a template omits it
    /// (<c>CreatureTemplate.java:147</c>). Player templates declare 50 in <c>baseMoveSpd/fastSwim</c>.
    /// </summary>
    static float SwimRunBaseOf(ZoneEntity entity) =>
        entity.BaseSwimRunSpeed > 0 ? entity.BaseSwimRunSpeed : RunBaseOf(entity);

    /// <summary><c>baseSwimWalkSpd</c> falls back to <c>baseWalkSpd</c> (<c>CreatureTemplate.java:148</c>).</summary>
    static float SwimWalkBaseOf(ZoneEntity entity) =>
        entity.BaseSwimWalkSpeed > 0 ? entity.BaseSwimWalkSpeed : WalkBaseOf(entity);

    /// <summary>
    /// Recomputes the cached live <see cref="ZoneEntity.RunSpeed"/> and <see cref="ZoneEntity.WalkSpeed"/> from the
    /// current stat funcs and reports whether either moved, so callers can re-send ChangeMoveType (Mobius
    /// <c>Player.updateStats</c> → broadcast move speeds).
    /// Players fold passive masteries too (Quick Step / Agile Movement / armor-set runSpd).
    /// </summary>
    internal bool RefreshRunSpeed(ZoneEntity entity)
    {
        float beforeRun = entity.RunSpeed;
        float beforeWalk = entity.WalkSpeed;
        float beforeSwimRun = entity.SwimRunSpeed;
        float beforeSwimWalk = entity.SwimWalkSpeed;
        var mods = entity.Kind == ZoneEntityKind.Player ? PlayerStatMods(entity) : StatMods(entity);
        int boost = ZoneSpeedSettings.RunSpeedBoost;
        entity.SetLiveRunSpeed(RunSpeedStat(entity, mods, boost));
        entity.SetLiveWalkSpeed(WalkSpeedStat(entity, mods, boost));
        entity.SetLiveSwimSpeed(SwimRunSpeedStat(entity, mods, boost), SwimWalkSpeedStat(entity, mods, boost));
        return Math.Abs(beforeRun - entity.RunSpeed) > 0.001f
            || Math.Abs(beforeWalk - entity.WalkSpeed) > 0.001f
            || Math.Abs(beforeSwimRun - entity.SwimRunSpeed) > 0.001f
            || Math.Abs(beforeSwimWalk - entity.SwimWalkSpeed) > 0.001f;
    }

    /// <summary>Mobius <c>ConditionUsingItemType</c> — delegates to <see cref="ConditionUsingItemType"/>.</summary>
    internal static bool Wearing(ZoneEntity player, string[] kinds) =>
        ConditionUsingItemType.Matches(player.Inventory, kinds);

    /// <summary>Live buff/debuff stat funcs on <paramref name="entity"/> (players and NPCs).</summary>
    internal CombatStatMods StatMods(ZoneEntity entity) =>
        entity.Abnormals.Count == 0 ? CombatStatMods.None : _statEffects.Collect(entity.Abnormals);

    double NpcAccuracy(ZoneEntity npc) =>
        StatMods(npc).Apply(CombatStat.Accuracy, ZoneCombat.AccuracyOrEvasion(npc.BaseAccuracy, npc.Dex, npc.Level));
    double NpcEvasion(ZoneEntity npc) =>
        StatMods(npc).Apply(CombatStat.Evasion, ZoneCombat.AccuracyOrEvasion(npc.BaseEvasion, npc.Dex, npc.Level));
    int NpcCritPermille(ZoneEntity npc) =>
        StatMods(npc).Apply(CombatStat.CritRate, (int)(npc.BaseCritical * 10 * StatBonus(PlayerClasses.StatBonuses, StatSlot.Dex, npc.Dex)));

    double NpcShieldRate(ZoneEntity npc) =>
        npc.ShieldRate * StatBonus(PlayerClasses.StatBonuses, StatSlot.Dex, npc.Dex);

    /// <summary>
    /// Stat bonus table lookup. Out-of-range values (henna push, bad data) clamp and warn —
    /// never silently return 1.0 like the old SafeBonus swallow. Prefer <see cref="EffectiveAttributes"/> for players.
    /// </summary>
    static double StatBonus(StatBonusTable table, StatSlot slot, int value)
    {
        double result = table.BonusClamped(slot, value, out bool clamped);
        if (clamped)
            ZoneLog.Warn($"stat-bonus out of range slot={slot} value={value} — clamped");
        return result;
    }

    /// <summary>Template DEX + henna (Mobius calcStat STAT_DEX before FuncMoveSpeed).</summary>
    int EffectiveDex(ZoneEntity player) => EffectiveAttributes(player).Dex;

    int EffectiveStr(ZoneEntity player) => EffectiveAttributes(player).Str;

    int EffectiveCon(ZoneEntity player) => EffectiveAttributes(player).Con;

    int EffectiveMen(ZoneEntity player) => EffectiveAttributes(player).Men;
}
