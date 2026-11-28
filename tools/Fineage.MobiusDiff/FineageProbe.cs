using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.MobiusDiff;

/// <summary>
/// Fineage side of the differential harness. It reuses the production pure layers
/// (<see cref="PlayerEffectiveAttributes"/>, <see cref="PlayerGearBases"/>, <see cref="PlayerFormulaBases"/>,
/// <see cref="CombatStatMods"/>, <see cref="ZoneCombat"/>) and mirrors the order in
/// <c>ZoneWorldStats.PlayerStats</c> (template → gear → formula → abnormal funcs).
/// NPC / summon rows use the same catalog numbers <c>ZoneWorldStats.More</c> folds (no extras).
/// </summary>
public static class FineageProbe
{
    public static ActualMetrics Measure(Scenario scenario, GameData data, NpcCatalog npcs)
    {
        var attacker = Resolve(scenario, data, npcs, attacker: true);
        var target = Resolve(scenario, data, npcs, attacker: false);

        int critChance = Math.Clamp(attacker.CritPermille, 0, ZoneCombat.MaxCritPermille);
        int hitChance = ZoneCombat.HitChancePermille(attacker.Accuracy, target.Evasion);
        int damageNoCrit = ZoneCombat.PhysicalDamage(attacker.PAtk, target.PDef, Proximity(scenario.Contact.Side));

        var metrics = new ActualMetrics
        {
            ["pAtk"] = attacker.PAtk,
            ["pDef"] = attacker.PDef,
            ["pAtkSpd"] = attacker.PAtkSpd,
            ["accuracy"] = attacker.Accuracy,
            ["evasion"] = attacker.Evasion,
            ["critPermille"] = attacker.CritPermille,
            ["hitChancePermille"] = hitChance,
            ["critChancePermille"] = critChance,
            ["damageNoCrit"] = damageNoCrit
        };

        foreach (var cp in scenario.CpSplits)
        {
            if (string.IsNullOrWhiteSpace(cp.Key))
                throw new InvalidDataException($"{scenario.Id}: every cpSplits row needs a key.");
            var split = TargetDamage.Split(cp.Damage, cp.TargetCp, cp.TargetHp, cp.AttackerPlayable, cp.IgnoreCp);
            metrics["cpLoss_" + cp.Key] = split.CpLoss;
            metrics["hpLoss_" + cp.Key] = split.HpLoss;
        }

        if (scenario.Samples > 0)
        {
            var rnd = new Random(scenario.Seed);
            long total = 0;
            for (int i = 0; i < scenario.Samples; i++)
            {
                var (damage, _) = ZoneCombat.RollMelee(
                    rnd, attacker.PAtk, target.PDef, attacker.Accuracy, target.Evasion, critChance,
                    attacker.RandomDamage, scenario.Contact.Soulshot, 0, 0, 0, attacker.Bow,
                    1d, attacker.CritDamageMod, 1d, attacker.CritDamageAdd);
                total += damage;
            }

            metrics["damageMeanPerSwing"] = (double)total / scenario.Samples;
        }

        if (scenario.Matrix)
            PhysAutoMatrix.Add(metrics, scenario, attacker, target);
        if (scenario.StatsMatrix)
            AttrStatsMatrix.Add(metrics, attacker);
        if (scenario.MagicMatrix)
            MagicDamageMatrix.Add(metrics, scenario, attacker, target);

        return metrics;
    }

    internal static ActorSnapshot Resolve(Scenario scenario, GameData data, NpcCatalog npcs, bool attacker)
    {
        if (attacker)
        {
            return scenario.Attacker.Kind == "player"
                ? MeasurePlayer(scenario.Id, scenario.Attacker.ClassId, scenario.Attacker.Level,
                    scenario.Attacker.Gear, scenario.Attacker.Buffs, scenario.Attacker.Passives, data)
                : MeasureNpc(scenario.Id, scenario.Attacker.NpcId, playable: scenario.Attacker.Kind == "summon",
                    data, npcs);
        }

        return scenario.Target.Kind == "player"
            ? MeasurePlayer(scenario.Id, scenario.Target.ClassId, scenario.Target.Level,
                scenario.Target.Gear, scenario.Target.Buffs, passives: [], data)
            : MeasureNpc(scenario.Id, scenario.Target.NpcId, playable: scenario.Target.Kind == "summon",
                data, npcs);
    }

    static ActorSnapshot MeasurePlayer(
        string scenarioId, int classId, int level, List<GearSpec> gearList, List<BuffSpec> buffs,
        List<BuffSpec> passives, GameData data)
    {
        if (classId < 0)
            throw new InvalidDataException($"{scenarioId}: classId must be non-negative.");
        var template = data.Classes?.Get(classId)
            ?? throw new InvalidDataException($"{scenarioId}: classes.json is unavailable.");
        if (level < 1)
            throw new InvalidDataException($"{scenarioId}: player level must be positive.");

        var inventory = new PlayerInventory(data.Items);
        foreach (var gear in gearList)
        {
            if (!Enum.TryParse<PaperdollSlot>(gear.Slot, ignoreCase: true, out var slot))
                throw new InvalidDataException($"{scenarioId}: unknown paperdoll slot '{gear.Slot}'.");
            var pieces = inventory.AddPieces(gear.ItemId, 1, gear.Enchant);
            inventory.Equip(pieces[0]);
            if (inventory.GetEquipped(slot) == null)
                throw new InvalidDataException(
                    $"{scenarioId}: item {gear.ItemId} did not occupy slot {gear.Slot}; body slot is {pieces[0].Template.BodySlot}.");
        }

        var attrs = PlayerEffectiveAttributes.From(
            template,
            new HennaStatBonus(Str: 0, Con: 0, Dex: 0, Int: 0, Men: 0, Wit: 0),
            data.Classes!.StatBonuses);
        var gearBases = PlayerGearBases.From(
            template,
            slot => inventory.GetEquipped(slot),
            ZoneCombat.DefaultWeaponRange,
            ZoneCombat.DefaultPlayerRandomDamage,
            ZoneCombat.NakedPDefBySlot,
            ZoneCombat.NakedMDefBySlot,
            ZoneCombat.MysticNakedChestPDef,
            ZoneCombat.MysticNakedLegsPDef,
            ZoneCombat.NakedLegsPDef,
            ZoneCombat.DefaultPlayerPDef);
        var formula = PlayerFormulaBases.From(attrs, gearBases, template.MAtkSpd, level);
        var mods = BuffMods(scenarioId, buffs, inventory);
        mods = PassiveMods(scenarioId, passives, inventory, mods);

        double pAtk = mods.Apply(CombatStat.PAtk, formula.PAtk);
        double pDef = Math.Max(1d, mods.Apply(CombatStat.PDef, formula.PDef));
        double pAtkSpd = Math.Max(1d, mods.Apply(CombatStat.PAtkSpd, formula.PAtkSpd));
        double mAtk = mods.Apply(CombatStat.MAtk, formula.MAtk);
        double mDef = Math.Max(1d, mods.Apply(CombatStat.MDef, formula.MDef));
        double mAtkSpd = Math.Max(1d, mods.Apply(CombatStat.MAtkSpd, formula.MAtkSpd));
        int accuracy = mods.Apply(CombatStat.Accuracy, formula.Accuracy);
        int evasion = mods.Apply(CombatStat.Evasion, formula.Evasion);
        int critPermille = mods.Apply(CombatStat.CritRate, formula.CritRate);
        int mCritPermille = (int)mods.Apply(CombatStat.MCritRate, formula.MagicCritBase) * 10;
        double critDamageMod = mods.Apply(CombatStat.CritDmg, 1d);
        double critDamageAdd = mods.Apply(CombatStat.CritDmgAdd, 0d);
        int attackRange = Math.Max(0, mods.Apply(CombatStat.PAtkRange, gearBases.AttackRange));
        int shieldDef = Math.Max(0, mods.Apply(CombatStat.SDef, gearBases.ShieldDef));
        double shieldRate = mods.Apply(CombatStat.RShld, formula.ShieldRate);
        bool bow = gearBases.Weapon == WeaponType.Bow;

        return new ActorSnapshot(
            pAtk, pDef, pAtkSpd, accuracy, evasion, critPermille, critDamageMod, critDamageAdd,
            attackRange, shieldDef, shieldRate, gearBases.RandomDamage, Playable: true, bow,
            ZoneCombat.WeaponReuseDelayMs(gearBases.Weapon),
            attrs.Str, attrs.Dex, attrs.Con, attrs.Int, attrs.Wit, attrs.Men,
            attrs.StrBonus, attrs.DexBonus, attrs.ConBonus, attrs.IntBonus, attrs.WitBonus, attrs.MenBonus,
            mAtk, mDef, mAtkSpd, mCritPermille,
            template.ModifiedHp(level), template.ModifiedMp(level), template.ModifiedCp(level),
            level, gearBases.Weapon != WeaponType.None);
    }

    static ActorSnapshot MeasureNpc(string scenarioId, int npcId, bool playable, GameData data, NpcCatalog npcs)
    {
        var npc = npcs.Get(npcId);
        double dexBonus = data.Classes!.StatBonuses.Bonus(StatSlot.Dex, npc.Dex);
        double accuracy = ZoneCombat.AccuracyOrEvasion(npc.Accuracy, npc.Dex, npc.Level);
        double evasion = ZoneCombat.AccuracyOrEvasion(npc.Evasion, npc.Dex, npc.Level);
        int critPermille = (int)(npc.Critical * 10 * dexBonus);
        double pAtkSpd = Math.Max(1, npc.AttackSpeed);
        bool bow = string.Equals(npc.AttackType, "BOW", StringComparison.OrdinalIgnoreCase);
        return new ActorSnapshot(
            npc.PAtk, Math.Max(1d, npc.PDef), pAtkSpd, accuracy, evasion, critPermille, 1d, 0d,
            Math.Max(0, npc.AttackRange), npc.ShieldDef, npc.ShieldRate * dexBonus, npc.RandomDamage,
            playable, bow, ZoneCombat.WeaponReuseDelayMs(bow ? WeaponType.Bow : WeaponType.None),
            MAtk: npc.MAtk, MDef: npc.MDef, Level: npc.Level);
    }


    static CombatStatMods PassiveMods(
        string scenarioId, List<BuffSpec> passives, PlayerInventory inventory, CombatStatMods mods)
    {
        if (passives.Count == 0)
            return mods;
        var catalog = SkillStatEffectCatalog.LoadPassives();
        var levels = new Dictionary<int, int>();
        foreach (var p in passives)
            levels[p.SkillId] = p.Level;
        return catalog.CollectPassives(
            levels,
            kinds => ConditionUsingItemType.Matches(inventory, kinds),
            ReferenceEquals(mods, CombatStatMods.None) ? null : mods,
            wearingSlot: slots => ConditionUsingItemType.MatchesSlot(inventory, slots),
            onMissing: id => throw new InvalidDataException(
                $"{scenarioId}: passive skill {id} is missing from {SkillStatEffectCatalog.PassivesFileName}."));
    }
    static CombatStatMods BuffMods(string scenarioId, List<BuffSpec> buffs, PlayerInventory inventory)
    {
        if (buffs.Count == 0)
            return CombatStatMods.None;
        var catalog = SkillStatEffectCatalog.LoadDefault();
        var sources = buffs.Select(b => new SkillStatSource(b.SkillId, b.Level, b.Level)).ToArray();
        return catalog.Collect(
            sources,
            onMissing: id => throw new InvalidDataException(
                $"{scenarioId}: buff skill {id} is missing from {SkillStatEffectCatalog.FileName}."),
            wearing: kinds => ConditionUsingItemType.Matches(inventory, kinds),
            wearingSlot: slots => ConditionUsingItemType.MatchesSlot(inventory, slots));
    }

    internal static double Proximity(string side) => side.ToLowerInvariant() switch
    {
        "front" => 1d,
        "side" => 1.1,
        "behind" => 1.2,
        _ => throw new InvalidDataException($"side must be front, side or behind: {side}")
    };
}

internal readonly record struct ActorSnapshot(
    double PAtk,
    double PDef,
    double PAtkSpd,
    double Accuracy,
    double Evasion,
    int CritPermille,
    double CritDamageMod,
    double CritDamageAdd,
    int AttackRange,
    int ShieldDef,
    double ShieldRate,
    int RandomDamage,
    bool Playable,
    bool Bow,
    int ReuseDelayMs,
    int Str = 0,
    int Dex = 0,
    int Con = 0,
    int Int = 0,
    int Wit = 0,
    int Men = 0,
    double StrBonus = 0,
    double DexBonus = 0,
    double ConBonus = 0,
    double IntBonus = 0,
    double WitBonus = 0,
    double MenBonus = 0,
    double MAtk = 0,
    double MDef = 0,
    double MAtkSpd = 0,
    int MCritPermille = 0,
    int MaxHp = 0,
    int MaxMp = 0,
    int MaxCp = 0,
    int Level = 0,
    bool Armed = false);

/// <summary>Normalized metric bag. Values are doubles so the same comparator handles points, permille and HP.</summary>
public sealed class ActualMetrics : Dictionary<string, double>
{
}

internal static class PhysAutoMatrix
{
    public static void Add(ActualMetrics metrics, Scenario scenario, ActorSnapshot attacker, ActorSnapshot target)
    {
        metrics["targetPDef"] = target.PDef;
        metrics["targetEvasion"] = target.Evasion;
        metrics["targetAccuracy"] = target.Accuracy;
        metrics["attackRange"] = attacker.AttackRange;
        metrics["attackTimeMs"] = ZoneCombat.AttackTimeMs(attacker.PAtkSpd);
        metrics["attackIntervalMs"] = ZoneCombat.AttackIntervalMs(
            attacker.PAtkSpd, attacker.Bow ? WeaponType.Bow : WeaponType.None, attacker.ReuseDelayMs);
        metrics["playableAttacker"] = attacker.Playable ? 1 : 0;
        metrics["pvp"] = attacker.Playable && target.Playable ? 1 : 0;

        metrics["damageFront"] = ZoneCombat.PhysicalDamage(attacker.PAtk, target.PDef, 1d);
        metrics["damageSide"] = ZoneCombat.PhysicalDamage(attacker.PAtk, target.PDef, 1.1);
        metrics["damageBehind"] = ZoneCombat.PhysicalDamage(attacker.PAtk, target.PDef, 1.2);
        metrics["damageSoulshotFront"] = ZoneCombat.PhysicalDamage(attacker.PAtk * 2, target.PDef, 1d);
        metrics["damageCritFront"] = CritDamage(attacker, target.PDef, proximity: 1d, soulshot: false);
        metrics["damageCritSoulshotFront"] = CritDamage(attacker, target.PDef, proximity: 1d, soulshot: true);

        int baseHit = (int)((80 + 2 * (attacker.Accuracy - target.Evasion)) * 10);
        metrics["hitChanceFront"] = ZoneCombat.HitChancePermille(attacker.Accuracy, target.Evasion, 1d);
        metrics["hitChanceSide"] = ZoneCombat.HitChancePermille(attacker.Accuracy, target.Evasion, 1.05);
        metrics["hitChanceBehind"] = ZoneCombat.HitChancePermille(attacker.Accuracy, target.Evasion, 1.10);
        metrics["hitChanceBaseUnclamped"] = baseHit;

        if (target.ShieldDef > 0 && target.ShieldRate > 1e-6)
        {
            metrics["shieldDef"] = target.ShieldDef;
            metrics["shieldBlockRate"] = target.ShieldRate;
            metrics["shieldBlockRateVsBow"] = target.ShieldRate * 1.3;
            metrics["damageShieldFront"] = ZoneCombat.PhysicalDamage(
                attacker.PAtk, target.PDef + target.ShieldDef, 1d);
            metrics["damagePerfectBlock"] = 1;
        }

        if (attacker.Bow)
        {
            metrics["bowRange"] = attacker.AttackRange;
            metrics["bowReuseMs"] = ZoneCombat.WeaponReuseMs(attacker.ReuseDelayMs, attacker.PAtkSpd);
        }

        if (scenario.Target.Kind == "player")
        {
            int front = (int)metrics["damageFront"];
            var playable = TargetDamage.Split(
                front, scenario.Target.Cp, scenario.Target.Hp, attacker.Playable, ignoreCp: false);
            metrics["cpLoss_front"] = playable.CpLoss;
            metrics["hpLoss_front"] = playable.HpLoss;
            var bypass = TargetDamage.Split(
                front, scenario.Target.Cp, scenario.Target.Hp, cpAbsorbs: false, ignoreCp: false);
            metrics["cpLoss_npcBypass"] = bypass.CpLoss;
            metrics["hpLoss_npcBypass"] = bypass.HpLoss;
        }
    }

    static int CritDamage(ActorSnapshot attacker, double pDef, double proximity, bool soulshot)
    {
        double defence = Math.Max(1d, pDef);
        double damage = Math.Max(0d, attacker.PAtk) * (soulshot ? 2d : 1d);
        damage = 2 * attacker.CritDamageMod * ((ZoneCombat.PhysDamCoefficient * damage * proximity) / defence);
        if (attacker.CritDamageAdd > 0)
            damage += attacker.CritDamageAdd * 77d / defence;
        return Math.Max(1, (int)damage);
    }
}

internal static class AttrStatsMatrix
{
    public static void Add(ActualMetrics metrics, ActorSnapshot a)
    {
        metrics["str"] = a.Str;
        metrics["dex"] = a.Dex;
        metrics["con"] = a.Con;
        metrics["int"] = a.Int;
        metrics["wit"] = a.Wit;
        metrics["men"] = a.Men;
        metrics["strBonus"] = a.StrBonus;
        metrics["dexBonus"] = a.DexBonus;
        metrics["conBonus"] = a.ConBonus;
        metrics["intBonus"] = a.IntBonus;
        metrics["witBonus"] = a.WitBonus;
        metrics["menBonus"] = a.MenBonus;
        metrics["mAtk"] = a.MAtk;
        metrics["mDef"] = a.MDef;
        metrics["mAtkSpd"] = a.MAtkSpd;
        metrics["mCritPermille"] = a.MCritPermille;
        metrics["maxHp"] = a.MaxHp;
        metrics["maxMp"] = a.MaxMp;
        metrics["maxCp"] = a.MaxCp;
    }
}

internal static class MagicDamageMatrix
{
    public static void Add(ActualMetrics metrics, Scenario scenario, ActorSnapshot a, ActorSnapshot t)
    {
        var skills = SkillCombatCatalog.LoadDefault();
        if (!skills.TryGet(scenario.Magic.SkillId, out var skill))
            throw new InvalidDataException($"{scenario.Id}: magic skill {scenario.Magic.SkillId} is not in skill-combat.json.");
        int lvl = scenario.Magic.Level;
        double power = skill.PowerAt(lvl);
        int magicLevel = skill.MagicLevelAt(lvl);
        if (magicLevel <= 0)
            magicLevel = a.Level;

        metrics["magicPower"] = power;
        metrics["magicLevel"] = magicLevel;
        metrics["casterLevel"] = a.Level;
        metrics["targetLevel"] = t.Level;
        metrics["mAtk"] = a.MAtk;
        metrics["targetMDef"] = t.MDef;

        int rate = ZoneCombat.MagicSuccessRate(t.Level, magicLevel);
        metrics["magicSuccessRate"] = rate;
        metrics["magicHalfFailEligible"] = t.Level - a.Level <= 9 ? 1 : 0;

        double crit = scenario.Magic.CritPower;
        bool pvp = scenario.Target.Kind == "player";
        double baseNormal = ZoneCombat.MagicBaseDamage(a.MAtk, t.MDef, power);
        double baseSps = ZoneCombat.MagicBaseDamage(a.MAtk * ZoneCombat.SpiritshotMAtkMultiplier(1), t.MDef, power);
        double baseBss = ZoneCombat.MagicBaseDamage(a.MAtk * ZoneCombat.SpiritshotMAtkMultiplier(2), t.MDef, power);
        metrics["damageNormal"] = ZoneCombat.MagicDamageFor(baseNormal, ZoneCombat.MagicOutcome.Normal, false);
        metrics["damageSps"] = ZoneCombat.MagicDamageFor(baseSps, ZoneCombat.MagicOutcome.Normal, false);
        metrics["damageBss"] = ZoneCombat.MagicDamageFor(baseBss, ZoneCombat.MagicOutcome.Normal, false);
        metrics["damageCrit"] = ZoneCombat.MagicDamageFor(baseNormal, ZoneCombat.MagicOutcome.Critical, false, crit);
        metrics["damageCritBss"] = ZoneCombat.MagicDamageFor(baseBss, ZoneCombat.MagicOutcome.Critical, false, crit);
        metrics["damageCritPvp"] = ZoneCombat.MagicDamageFor(baseNormal, ZoneCombat.MagicOutcome.Critical, true, crit);
        metrics["damageHalfFail"] = ZoneCombat.MagicDamageFor(baseNormal, ZoneCombat.MagicOutcome.HalfFail, pvp);
        metrics["damageResisted"] = ZoneCombat.MagicDamageFor(baseNormal, ZoneCombat.MagicOutcome.Resisted, pvp);

        if (t.ShieldDef > 0)
        {
            double baseShield = ZoneCombat.MagicBaseDamage(a.MAtk, t.MDef + t.ShieldDef, power);
            metrics["damageShieldBlock"] = ZoneCombat.MagicDamageFor(baseShield, ZoneCombat.MagicOutcome.Normal, false);
            metrics["damagePerfectBlock"] = 1;
        }

        int random = ZoneCombat.MagicRandomPercent(a.Armed, a.RandomDamage, a.Level);
        metrics["randomPercent"] = random;
        metrics["damageRandomMin"] = ZoneCombat.MagicDamageFor(
            baseNormal, ZoneCombat.MagicOutcome.Normal, false, 1d, 1 + -random / 100d);
        metrics["damageRandomMax"] = ZoneCombat.MagicDamageFor(
            baseNormal, ZoneCombat.MagicOutcome.Normal, false, 1d, 1 + random / 100d);
    }
}
