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
        double speed = player ? baseSpeed * SafeBonus(PlayerClasses.StatBonuses, StatSlot.Dex, entity.Dex) : baseSpeed;
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

    /// <summary>Mobius <c>ConditionUsingItemType</c>: weapon kinds test the right-hand weapon type; armor kinds (LIGHT/HEAVY/MAGIC)    /// need the chest armor type and, unless the chest is full-body, matching legs; SHIELD needs a left-hand shield.
    /// </summary>
    internal static bool Wearing(ZoneEntity player, string[] kinds)
    {
        var inv = player.Inventory;
        if (inv == null)
            return false;
        var weapon = inv.GetEquipped(PaperdollSlot.RHand)?.Template;
        var chest = inv.GetEquipped(PaperdollSlot.Chest)?.Template;
        var legs = inv.GetEquipped(PaperdollSlot.Legs)?.Template;
        var lhand = inv.GetEquipped(PaperdollSlot.LHand)?.Template;
        foreach (var kind in kinds)
        {
            switch (kind)
            {
                case "LIGHT" or "HEAVY" or "MAGIC":
                    var want = kind == "LIGHT" ? ArmorType.Light : kind == "HEAVY" ? ArmorType.Heavy : ArmorType.Magic;
                    if (chest != null && chest.ArmorType == want
                        && (chest.BodySlot == BodySlot.FullArmor || (legs != null && legs.ArmorType == want)))
                        return true;
                    break;
                case "SHIELD":
                    if (lhand != null && lhand.Kind == ItemKind.Armor && lhand.ShieldDef > 0)
                        return true;
                    break;
                default:
                    if (weapon == null || weapon.Kind != ItemKind.Weapon)
                        break;
                    var wt = weapon.WeaponType;
                    if (kind switch
                        {
                            "SWORD" => wt == WeaponType.Sword, "BLUNT" => wt == WeaponType.Blunt,
                            "DAGGER" or "DUALDAGGER" => wt == WeaponType.Dagger, "BOW" => wt == WeaponType.Bow,
                            "POLE" => wt == WeaponType.Pole, "DUAL" => wt == WeaponType.Dual,
                            "FIST" or "DUALFIST" => wt == WeaponType.Fist, "FISHINGROD" => wt == WeaponType.FishingRod,
                            "ETC" => wt == WeaponType.Etc, _ => false
                        })
                        return true;
                    break;
            }
        }
        return false;
    }

    /// <summary>Live buff/debuff stat funcs on <paramref name="entity"/> (players and NPCs).</summary>
    internal CombatStatMods StatMods(ZoneEntity entity) =>
        entity.Abnormals.Count == 0 ? CombatStatMods.None : _statEffects.Collect(entity.Abnormals);

    double NpcAccuracy(ZoneEntity npc) =>
        StatMods(npc).Apply(CombatStat.Accuracy, ZoneCombat.AccuracyOrEvasion(npc.BaseAccuracy, npc.Dex, npc.Level));
    double NpcEvasion(ZoneEntity npc) =>
        StatMods(npc).Apply(CombatStat.Evasion, ZoneCombat.AccuracyOrEvasion(npc.BaseEvasion, npc.Dex, npc.Level));
    int NpcCritPermille(ZoneEntity npc) =>
        StatMods(npc).Apply(CombatStat.CritRate, (int)(npc.BaseCritical * 10 * SafeBonus(PlayerClasses.StatBonuses, StatSlot.Dex, npc.Dex)));

    double NpcShieldRate(ZoneEntity npc) =>
        npc.ShieldRate * SafeBonus(PlayerClasses.StatBonuses, StatSlot.Dex, npc.Dex);

    static double SafeBonus(StatBonusTable table, StatSlot slot, int value)
    {
        try { return table.Bonus(slot, value); }
        catch (ArgumentOutOfRangeException) { return 1d; }
    }
}
