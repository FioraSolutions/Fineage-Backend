using Fineage.Game;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>Player physical/magic stats after Mobius base-stat, level, weapon and armor functions.</summary>
public readonly record struct PlayerCombatStats(
    int PAtk, int MAtk, int PDef, int MDef, int PAtkSpd, int MAtkSpd, int Accuracy, int CritRate,
    int AttackRange, WeaponType Weapon, int Evasion = 0, int RandomDamage = ZoneCombat.DefaultPlayerRandomDamage,
    int ShieldDef = 0, double ShieldRate = 0, int Soulshots = 0, CrystalGrade WeaponGrade = CrystalGrade.None,
    int MCritRate = 0, double MagicCritDmg = 1d, double CritDmg = 1d, double BlowRate = 1d,
    double CritDmgAdd = 0d,
    int ShieldDefAngle = 0, double PReuse = 1d, double MReuse = 1d, double DReuse = 1d,
    double PhysicalMpConsumeRate = 1d, double MagicalMpConsumeRate = 1d, double DanceMpConsumeRate = 1d,
    double ReflectDam = 0d, double AbsorbDam = 0d, double HealEffect = 1d,
    int SkillCritical = 0, double SkillCriticalProbability = 1d, int AtkCountMax = 1,
    double DarkRes = 0d, double HolyRes = 0d, double FireRes = 0d, double WaterRes = 0d,
    double WindRes = 0d, double EarthRes = 0d, double TransDam = 0d, double ManaShield = 0d,
    double TransDamToPlayer = 0d, double DamageZoneVuln = 0d,
    double HolyPower = 0d, double FirePower = 0d,
    double PAtkPrecise = 0d);

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius Interlude player stat pipeline (layered, no Calculator rewrite):
    /// <list type="number">
    /// <item><see cref="PlayerEffectiveAttributes"/> — template + henna → bonus table</item>
    /// <item><see cref="PlayerGearBases"/> — weapon/armor/jewels + <c>FuncEnchant</c></item>
    /// <item><see cref="PlayerFormulaBases"/> — Mobius STR/levelMod/… formulas</item>
    /// <item><see cref="PlayerStatMods"/> — abnormals → passives → sets → weight/grade/death</item>
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
        var formula = PlayerFormulaBases.From(attrs, gear, t.MAtkSpd, level);
        var mods = PlayerStatMods(player);
        double pAtkPrecise = mods.Apply(CombatStat.PAtk, formula.PAtk);

        return new PlayerCombatStats(
            PAtk: (int)pAtkPrecise,
            PAtkPrecise: pAtkPrecise,
            MAtk: mods.Apply(CombatStat.MAtk, formula.MAtk),
            PDef: Math.Max(1, mods.Apply(CombatStat.PDef, formula.PDef)),
            MDef: Math.Max(1, mods.Apply(CombatStat.MDef, formula.MDef)),
            PAtkSpd: Math.Max(1, mods.Apply(CombatStat.PAtkSpd, formula.PAtkSpd)),
            MAtkSpd: Math.Max(1, mods.Apply(CombatStat.MAtkSpd, formula.MAtkSpd)),
            Accuracy: mods.Apply(CombatStat.Accuracy, formula.Accuracy),
            CritRate: mods.Apply(CombatStat.CritRate, formula.CritRate),
            AttackRange: Math.Max(0, mods.Apply(CombatStat.PAtkRange, gear.AttackRange)),
            Weapon: gear.Weapon,
            Evasion: mods.Apply(CombatStat.Evasion, formula.Evasion),
            RandomDamage: gear.RandomDamage,
            ShieldDef: Math.Max(0, mods.Apply(CombatStat.SDef, gear.ShieldDef)),
            ShieldRate: mods.Apply(CombatStat.RShld, formula.ShieldRate),
            Soulshots: gear.Soulshots,
            WeaponGrade: gear.WeaponGrade,
            MCritRate: (int)mods.Apply(CombatStat.MCritRate, formula.MagicCritBase) * 10,
            MagicCritDmg: mods.Apply(CombatStat.MagicCritDmg, 1d),
            CritDmg: mods.Apply(CombatStat.CritDmg, 1d),
            BlowRate: mods.Apply(CombatStat.BlowRate, 1d),
            CritDmgAdd: mods.Apply(CombatStat.CritDmgAdd, 0d),
            ShieldDefAngle: mods.Apply(CombatStat.ShieldDefAngle, 0),
            PReuse: mods.Apply(CombatStat.PReuse, 1d),
            MReuse: mods.Apply(CombatStat.MReuse, 1d),
            DReuse: mods.Apply(CombatStat.DReuse, 1d),
            PhysicalMpConsumeRate: mods.Apply(CombatStat.PhysicalMpConsumeRate, 1d),
            MagicalMpConsumeRate: mods.Apply(CombatStat.MagicalMpConsumeRate, 1d),
            DanceMpConsumeRate: mods.Apply(CombatStat.DanceMpConsumeRate, 1d),
            ReflectDam: mods.Apply(CombatStat.ReflectDam, 0d),
            AbsorbDam: mods.Apply(CombatStat.AbsorbDam, 0d),
            HealEffect: mods.Apply(CombatStat.HealEffect, 1d),
            SkillCritical: mods.Apply(CombatStat.SkillCritical, 0),
            SkillCriticalProbability: mods.Apply(CombatStat.SkillCriticalProbability, 1d),
            // Mobius calcStat(ATTACK_COUNT_MAX, 1): a pole starts at 1 hit; Fell Swoop adds extras.
            AtkCountMax: Math.Max(1, mods.Apply(CombatStat.AtkCountMax, 1)),
            DarkRes: mods.Apply(CombatStat.DarkRes, 0d),
            HolyRes: mods.Apply(CombatStat.HolyRes, 0d),
            FireRes: mods.Apply(CombatStat.FireRes, 0d),
            WaterRes: mods.Apply(CombatStat.WaterRes, 0d),
            WindRes: mods.Apply(CombatStat.WindRes, 0d),
            EarthRes: mods.Apply(CombatStat.EarthRes, 0d),
            // Mobius TRANSFER_DAMAGE_PERCENT (transDam): percent of incoming damage sent to the servitor.
            TransDam: mods.Apply(CombatStat.TransDam, 0d),
            ManaShield: mods.Apply(CombatStat.ManaShield, 0d),
            TransDamToPlayer: mods.Apply(CombatStat.TransDamToPlayer, 0d),
            DamageZoneVuln: mods.Apply(CombatStat.DamageZoneVuln, 0d),
            HolyPower: mods.Apply(CombatStat.HolyPower, 0d),
            FirePower: mods.Apply(CombatStat.FirePower, 0d));
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
        var gear = PlayerGearBases.From(
            template,
            Equipped,
            ZoneCombat.DefaultWeaponRange,
            ZoneCombat.DefaultPlayerRandomDamage,
            ZoneCombat.NakedPDefBySlot,
            ZoneCombat.NakedMDefBySlot,
            ZoneCombat.MysticNakedChestPDef,
            ZoneCombat.MysticNakedLegsPDef,
            ZoneCombat.NakedLegsPDef,
            ZoneCombat.DefaultPlayerPDef,
            _olympiadArena.Contains(player.ObjectId) ? _olympiadEnchantLimit : -1);
        return Augmentation.AugmentationStatMods.Apply(gear, player.Inventory, _augmentation);
    }

    /// <summary>Mobius GameTimeTaskManager.isNight for HitConditionBonus dark −10.</summary>
    internal bool IsNight => _gameTime.IsNight(NowUnixMs());

    /// <summary>
    /// Named mod stages shared by combat / speed / regen / load getters:
    /// <list type="number">
    /// <item>Abnormals (buff/debuff XML funcs)</item>
    /// <item>Known + clan + gear passives (<see cref="PlayerPassiveSources"/>)</item>
    /// <item>Virtual weight / grade / death penalties</item>
    /// </list>
    /// Gear SA and complete armor-set skills live in <see cref="ZoneEntity.GearSkills"/>
    /// (rebuilt on equip/enter by <see cref="RefreshGearSkills"/>, not on every stat read).
    /// </summary>
    internal CombatStatMods PlayerStatMods(ZoneEntity player)
    {
        var mods = FoldAbnormals(player);
        mods = FoldTestCombatFuncs(player, mods);
        mods = FoldLearnedClanAndGearPassives(player, mods);
        mods = FoldWeightPenalty(player, mods);
        mods = FoldGradePenalty(player, mods);
        return FoldDeathPenalty(player, mods);
    }

    static CombatStatMods? FoldTestCombatFuncs(ZoneEntity entity, CombatStatMods? mods)
    {
        if (entity.TestCombatFuncs is not { Count: > 0 } funcs)
            return mods;
        if (mods is null || ReferenceEquals(mods, CombatStatMods.None))
            mods = new CombatStatMods();
        foreach (var func in funcs)
            mods.Add(func, 1);
        return mods;
    }

    CombatStatMods? FoldAbnormals(ZoneEntity player)
    {
        if (player.Abnormals.Count == 0)
            return null;
        // Buff funcs can carry their own <using kind> gate (Focus Chance / Focus Power need a dagger; Rapid Shot a bow).
        var mods = SkillStatEffects.Collect(_statEffects, player.Abnormals,
            wearing: kinds => ConditionUsingItemType.Matches(player.Inventory, kinds),
            wearingSlot: slots => ConditionUsingItemType.MatchesSlot(player.Inventory, slots));
        return ReferenceEquals(mods, CombatStatMods.None) ? null : mods;
    }

    CombatStatMods? FoldLearnedClanAndGearPassives(ZoneEntity player, CombatStatMods? mods)
    {
        var passiveLevels = ActivePassiveLevels(player);
        return passiveLevels.Count == 0
            ? mods
            : _passives.CollectPassives(
                passiveLevels,
                kinds => ConditionUsingItemType.Matches(player.Inventory, kinds),
                mods,
                resting: player.Sitting,
                hpPercent: player.MaxHp <= 0 ? 100d : 100d * player.CurHp / player.MaxHp,
                moving: player.MoveActive,
                wearingSlot: slots => ConditionUsingItemType.MatchesSlot(player.Inventory, slots),
                onMissing: id => ZoneLog.Warn($"skill-passives missing skillId={id} — passive has no combat mods"));
    }

    /// <summary>
    /// Skill id → level fed into <c>skill-passives.json</c> for combat mods.
    /// KnownSkills + ClanSkills + GearSkills (equip SA / complete sets). Higher level wins.
    /// </summary>
    internal static Dictionary<int, int> PassiveSkillLevels(ZoneEntity player) =>
        PlayerPassiveSources.Merge(player.KnownSkills, player.ClanSkills, player.GearSkills, player.CursedSkills);

    /// <summary>
    /// The passive subset of <see cref="PassiveSkillLevels"/>: ids the skill book marks <c>passive</c>.
    /// <para>
    /// Mobius folds only <c>operateType P</c> funcs into the player's stat line; actives apply their funcs
    /// through an abnormal while the buff is on. Feeding the whole skill book here matched the actives'
    /// buff funcs against <c>skill-passives.json</c> (which carries passives only), so every learned Heal,
    /// Might, Shield or Wind Strike was reported as <c>skill-passives missing skillId=N</c> and — worse —
    /// an id that happens to exist in both tables would have been applied twice: once as a known skill and
    /// again as an abnormal.
    /// </para>
    /// <para>
    /// A gear/set/clan id the skill book does not know is kept: those come from Mobius Inventory/Clan skill
    /// additions that are not in <c>skills.json</c>, and their passives must still fold.
    /// </para>
    /// </summary>
    internal Dictionary<int, int> ActivePassiveLevels(ZoneEntity player)
    {
        var all = PassiveSkillLevels(player);
        var passives = new Dictionary<int, int>(all.Count);
        foreach (var (skillId, level) in all)
        {
            var template = _game.Skills.Find(skillId);
            if (template == null || template.Passive)
                passives[skillId] = level;
        }
        return passives;
    }

    /// <summary>Mobius player DefenceTrait map from jewelry / set passives (poison, hold, …).</summary>
    internal IReadOnlyDictionary<string, double>? PlayerDefenceTraits(ZoneEntity player)
    {
        return _defenceTraits.Collect(ActivePassiveLevels(player));
    }

    /// <summary>
    /// Rebuild ephemeral <see cref="ZoneEntity.GearSkills"/> from equipped item_skill rows and complete armor sets
    /// (Mobius Inventory temporary skills). Called from equip/enter paths, not from read-only stat folds.
    /// </summary>
    internal void RefreshGearSkills(ZoneEntity player)
    {
        AssertBodyOwns(player);
        var gear = player.GearSkills;
        gear.Clear();
        _equipSkills.CollectEquipped(player.Inventory, gear);
        WarnMissingEquipSkillStats(player);
        CollectArmorSetSkills(player, gear);
        Augmentation.AugmentationStatMods.CollectGearSkills(player.Inventory, _augmentation, gear);
    }

    /// <summary>
    /// Mobius <c>item_skill</c> rows must resolve to <c>skill-passives.json</c> funcs (or
    /// <c>skill-stat-effects.json</c> for active buffs). Log once per equipped item/skill with no data.
    /// </summary>
    void WarnMissingEquipSkillStats(ZoneEntity player)
    {
        if (player.Inventory == null)
            return;
        foreach (PaperdollSlot slot in Enum.GetValues<PaperdollSlot>())
        {
            var item = player.Inventory.GetEquipped(slot);
            if (item == null || !_equipSkills.TryGet(item.ItemId, out var skills))
                continue;
            foreach (var skill in skills)
            {
                if (skill.Id <= 0)
                    continue;
                if (_passives.TryGet(skill.Id, out var passive) && passive.Funcs.Count > 0)
                    continue;
                if (_statEffects.TryGet(skill.Id, out var active) && active.Funcs.Count > 0)
                    continue;
                var key = (item.ItemId, skill.Id);
                if (!player.ReportedMissingEquipSkills.Add(key))
                    continue;
                ZoneLog.Warn(
                    $"item-equip-skill missing stat funcs itemId={item.ItemId} skillId={skill.Id} level={skill.Level}");
            }
        }
    }

    void CollectArmorSetSkills(ZoneEntity player, Dictionary<int, int> target)
    {
        if (player.Inventory == null || _armorSets.Count == 0)
            return;
        var inv = player.Inventory;
        var chest = inv.GetEquipped(PaperdollSlot.Chest);
        if (chest == null)
            return;
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
        PlayerPassiveSources.MergeInto(target, skills);
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
