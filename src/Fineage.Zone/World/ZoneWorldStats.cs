using Fineage.Game;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>Player physical/magic stats after Mobius base-stat, level, weapon and armor functions.</summary>
public readonly record struct PlayerCombatStats(
    int PAtk, int MAtk, int PDef, int MDef, int PAtkSpd, int MAtkSpd, int Accuracy, int CritRate,
    int AttackRange, WeaponType Weapon, int Evasion = 0, int RandomDamage = ZoneCombat.DefaultPlayerRandomDamage,
    int ShieldDef = 0, double ShieldRate = 0, int Soulshots = 0, CrystalGrade WeaponGrade = CrystalGrade.None,
    int MCritRate = 0, double MagicCritDmg = 1d, double CritDmg = 1d, double BlowRate = 1d);

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius Interlude player stat pipeline (layered, no Calculator rewrite):
    /// <list type="number">
    /// <item><see cref="PlayerEffectiveAttributes"/> — template + henna → bonus table</item>
    /// <item><see cref="PlayerGearBases"/> — weapon/armor/jewels + <c>FuncEnchant</c></item>
    /// <item>Mobius base formulas (STR/levelMod/…)</item>
    /// <item><see cref="PlayerStatMods"/> — buffs, passives, clan/set skills, weight/grade/death</item>
    /// </list>
    /// Thin <c>IStatCalculator</c> stays deferred; these named layers are the SoT for parity tests.
    /// </summary>
    internal PlayerCombatStats PlayerStats(ZoneEntity player)
    {
        int level = Math.Max(1, player.Level);
        if (!PlayerClasses.TryGet(player.ClassId, out var t) || t == null)
            throw new InvalidOperationException($"PlayerStats: unknown classId={player.ClassId} object={player.ObjectId}");

        var attrs = EffectiveAttributes(player, t);
        var gear = GearBases(player, t);
        double levelMod = PlayerGearBases.LevelMod(level);
        var mods = PlayerStatMods(player);

        return new PlayerCombatStats(
            PAtk: mods.Apply(CombatStat.PAtk, (int)(gear.PAtk * attrs.StrBonus * levelMod)),
            MAtk: mods.Apply(CombatStat.MAtk, (int)(gear.MAtk * attrs.IntBonus * attrs.IntBonus * levelMod * levelMod)),
            PDef: Math.Max(1, mods.Apply(CombatStat.PDef, (int)(gear.PDef * levelMod))),
            MDef: Math.Max(1, mods.Apply(CombatStat.MDef, (int)(gear.MDef * attrs.MenBonus * levelMod))),
            PAtkSpd: Math.Max(1, mods.Apply(CombatStat.PAtkSpd, (int)(gear.PAtkSpd * attrs.DexBonus))),
            MAtkSpd: Math.Max(1, mods.Apply(CombatStat.MAtkSpd, (int)(t.MAtkSpd * attrs.WitBonus))),
            Accuracy: mods.Apply(CombatStat.Accuracy, (int)(Math.Sqrt(attrs.Dex) * 6 + level)),
            CritRate: mods.Apply(CombatStat.CritRate, (int)(gear.CritRate * attrs.DexBonus * 10)),
            AttackRange: gear.AttackRange,
            Weapon: gear.Weapon,
            Evasion: mods.Apply(CombatStat.Evasion,
                (int)ZoneCombat.AccuracyOrEvasion(0, attrs.Dex, level) - (gear.Shielded ? gear.ShieldEvasionPenalty : 0)),
            RandomDamage: gear.RandomDamage,
            ShieldDef: gear.ShieldDef,
            ShieldRate: gear.Shielded ? gear.ShieldRate * attrs.DexBonus : 0,
            Soulshots: gear.Soulshots,
            WeaponGrade: gear.WeaponGrade,
            MCritRate: ZoneCombat.PlayerMCritRate(gear.Armed, attrs.WitBonus, mods),
            MagicCritDmg: mods.Apply(CombatStat.MagicCritDmg, 1d),
            CritDmg: mods.Apply(CombatStat.CritDmg, 1d),
            BlowRate: mods.Apply(CombatStat.BlowRate, 1d));
    }

    /// <summary>Template + henna + bonus table (shared by combat, regen, weight, move speed).</summary>
    internal PlayerEffectiveAttributes EffectiveAttributes(ZoneEntity player, ClassTemplate? template = null)
    {
        if (template == null && (!PlayerClasses.TryGet(player.ClassId, out template) || template == null))
            throw new InvalidOperationException($"EffectiveAttributes: unknown classId={player.ClassId}");
        var henna = new HennaStatBonus(
            player.HennaStr, player.HennaCon, player.HennaDex,
            player.HennaInt, player.HennaMen, player.HennaWit);
        return PlayerEffectiveAttributes.From(
            template, henna, PlayerClasses.StatBonuses,
            static (slot, value) => ZoneLog.Warn($"stat-bonus out of range slot={slot} value={value} — clamped"));
    }

    PlayerGearBases GearBases(ZoneEntity player, ClassTemplate template)
    {
        ItemInstance? Equipped(PaperdollSlot slot) => player.Inventory?.GetEquipped(slot);
        return PlayerGearBases.From(
            template,
            Equipped,
            ZoneCombat.DefaultWeaponRange,
            ZoneCombat.DefaultPlayerRandomDamage,
            ZoneCombat.NakedPDefBySlot,
            ZoneCombat.NakedMDefBySlot,
            ZoneCombat.MysticNakedChestPDef,
            ZoneCombat.MysticNakedLegsPDef,
            ZoneCombat.NakedLegsPDef,
            ZoneCombat.DefaultPlayerPDef);
    }

    /// <summary>Mobius GameTimeTaskManager.isNight for HitConditionBonus dark −10.</summary>
    internal bool IsNight => _gameTime.IsNight(_clock().ToUnixTimeMilliseconds());

    /// <summary>
    /// Buffs plus passive masteries (Mobius operateType P) whose <c>using</c> condition matches the gear, plus the
    /// virtual Weight Penalty 4270 (<see cref="FoldWeightPenalty"/>) and Grade Penalty 4267
    /// (<see cref="FoldGradePenalty"/>) sources. Internal so tests can assert the folds that the speed/regen/load
    /// getters share.
    /// <para>
    /// Passive sources merged by <see cref="PassiveSkillLevels"/>:
    /// trainer/auto <see cref="ZoneEntity.KnownSkills"/> (includes skill-enchant levels once EnchantSkill writes them),
    /// and <see cref="ZoneEntity.ClanSkills"/> (Mobius <c>Clan.addSkillEffects</c> — grant path still TBD).
    /// </para>
    /// </summary>
    internal CombatStatMods PlayerStatMods(ZoneEntity player)
    {
        var mods = player.Abnormals.Count == 0 ? null : _statEffects.Collect(player.Abnormals);
        if (mods != null && ReferenceEquals(mods, CombatStatMods.None))
            mods = null;
        var passiveLevels = PassiveSkillLevels(player);
        mods = passiveLevels.Count == 0
            ? mods
            : _passives.CollectPassives(passiveLevels, kinds => Wearing(player, kinds), mods);
        // Weight Penalty 4270 is added by refreshOverloaded instead of the skill book, so it is folded here; Grade
        // Penalty 4267 comes from refreshExpertisePenalty the same way, and Death Penalty 5076 from
        // increaseDeathPenaltyBuffLevel — none of the three live in the trainer skill book.
        mods = FoldArmorSets(player, mods);
        mods = FoldWeightPenalty(player, mods);
        mods = FoldGradePenalty(player, mods);
        return FoldDeathPenalty(player, mods);
    }

    /// <summary>
    /// Skill id → level fed into <c>skill-passives.json</c> for combat mods.
    /// <list type="bullet">
    /// <item>KnownSkills: auto-tree + learned. EnchantSkill (Mobius) bumps the learned level in place — no extra map.</item>
    /// <item>ClanSkills: clan book effects (376 Might, 377 Aegis, …). Populate when clan skill grant is wired;
    /// funcs already live in <c>skill-passives.json</c>.</item>
    /// </list>
    /// Higher level wins on id collision.
    /// </summary>
    internal static Dictionary<int, int> PassiveSkillLevels(ZoneEntity player)
    {
        var map = new Dictionary<int, int>(player.KnownSkills.Count + player.ClanSkills.Count);
        foreach (var (id, level) in player.KnownSkills)
            map[id] = level;
        foreach (var (id, level) in player.ClanSkills)
        {
            if (!map.TryGetValue(id, out int have) || level > have)
                map[id] = level;
        }
        return map;
    }

    /// <summary>
    /// Mobius <c>ArmorSetListener</c>: when the worn chest matches a set and every required part is on,
    /// fold the set's skills (and optional shield / +6 skills) through the passive catalog.
    /// </summary>
    CombatStatMods FoldArmorSets(ZoneEntity player, CombatStatMods? mods)
    {
        if (player.Inventory == null || _armorSets.Count == 0)
            return mods ?? CombatStatMods.None;
        var inv = player.Inventory;
        var chest = inv.GetEquipped(PaperdollSlot.Chest);
        if (chest == null)
            return mods ?? CombatStatMods.None;
        int? ItemId(PaperdollSlot slot) => inv.GetEquipped(slot)?.ItemId;
        int EnchantOf(int itemId)
        {
            foreach (PaperdollSlot slot in new[]
                     {
                         PaperdollSlot.Chest, PaperdollSlot.Legs, PaperdollSlot.Head,
                         PaperdollSlot.Gloves, PaperdollSlot.Feet, PaperdollSlot.LHand
                     })
            {
                var item = inv.GetEquipped(slot);
                if (item != null && item.ItemId == itemId)
                    return item.EnchantLevel;
            }
            return 0;
        }
        var skills = _armorSets.ActiveSkills(
            chest.ItemId,
            ItemId(PaperdollSlot.Legs),
            ItemId(PaperdollSlot.Head),
            ItemId(PaperdollSlot.Gloves),
            ItemId(PaperdollSlot.Feet),
            ItemId(PaperdollSlot.LHand),
            EnchantOf);
        return skills.Count == 0 ? mods ?? CombatStatMods.None : _passives.CollectPassives(skills, kinds => Wearing(player, kinds), mods);
    }

    /// <summary>
    /// Mobius <c>CreatureStat.getRunSpeed()</c> (<c>CreatureStat.java:426-434</c>). Base is the template run speed, or
    /// the fast-swim base in water (<c>:428</c>); <c>baseRunSpd &lt;= 0</c> returns 0 without any stat fold.
    /// </summary>
    internal float RunSpeedStat(ZoneEntity entity, CombatStatMods mods, int runSpeedBoost = 0)
    {
        float baseSpeed = entity.InWater ? SwimRunBaseOf(entity) : RunBaseOf(entity);
        return baseSpeed <= 0 ? 0f : FoldSpeed(entity, baseSpeed, mods, runSpeedBoost);
    }

    /// <summary>
    /// Mobius <c>CreatureStat.getWalkSpeed()</c> (<c>CreatureStat.java:439-447</c>). Mobius has one speed stat
    /// (<c>Stat.MOVE_SPEED("runSpd")</c>, <c>Stat.java:99</c>) and the walk getter folds it onto the walk base too,
    /// so buffs and debuffs move walk speed just like run speed.
    /// </summary>
    internal float WalkSpeedStat(ZoneEntity entity, CombatStatMods mods, int runSpeedBoost = 0)
    {
        float baseSpeed = entity.InWater ? SwimWalkBaseOf(entity) : WalkBaseOf(entity);
        return baseSpeed <= 0 ? 0f : FoldSpeed(entity, baseSpeed, mods, runSpeedBoost);
    }

    /// <summary>Mobius <c>CreatureStat.getSwimRunSpeed()</c> (<c>CreatureStat.java:452-460</c>).</summary>
    internal float SwimRunSpeedStat(ZoneEntity entity, CombatStatMods mods, int runSpeedBoost = 0)
    {
        float baseSpeed = SwimRunBaseOf(entity);
        return baseSpeed <= 0 ? 0f : FoldSpeed(entity, baseSpeed, mods, runSpeedBoost);
    }

}
