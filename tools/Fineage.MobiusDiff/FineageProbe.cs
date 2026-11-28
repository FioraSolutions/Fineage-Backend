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

        return metrics;
    }

    internal static ActorSnapshot Resolve(Scenario scenario, GameData data, NpcCatalog npcs, bool attacker)
    {
        if (attacker)
        {
            return scenario.Attacker.Kind == "player"
                ? MeasurePlayer(scenario.Id, scenario.Attacker.ClassId, scenario.Attacker.Level,
                    scenario.Attacker.Gear, scenario.Attacker.Buffs, data)
                : MeasureNpc(scenario.Id, scenario.Attacker.NpcId, playable: scenario.Attacker.Kind == "summon",
                    data, npcs);
        }

        return scenario.Target.Kind == "player"
            ? MeasurePlayer(scenario.Id, scenario.Target.ClassId, scenario.Target.Level,
                scenario.Target.Gear, scenario.Target.Buffs, data)
            : MeasureNpc(scenario.Id, scenario.Target.NpcId, playable: scenario.Target.Kind == "summon",
                data, npcs);
    }

    static ActorSnapshot MeasurePlayer(
        string scenarioId, int classId, int level, List<GearSpec> gearList, List<BuffSpec> buffs, GameData data)
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

        double pAtk = mods.Apply(CombatStat.PAtk, formula.PAtk);
        double pDef = Math.Max(1d, mods.Apply(CombatStat.PDef, formula.PDef));
        double pAtkSpd = Math.Max(1d, mods.Apply(CombatStat.PAtkSpd, formula.PAtkSpd));
        int accuracy = mods.Apply(CombatStat.Accuracy, formula.Accuracy);
        int evasion = mods.Apply(CombatStat.Evasion, formula.Evasion);
        int critPermille = mods.Apply(CombatStat.CritRate, formula.CritRate);
        double critDamageMod = mods.Apply(CombatStat.CritDmg, 1d);
        double critDamageAdd = mods.Apply(CombatStat.CritDmgAdd, 0d);
        int attackRange = Math.Max(0, mods.Apply(CombatStat.PAtkRange, gearBases.AttackRange));
        int shieldDef = Math.Max(0, mods.Apply(CombatStat.SDef, gearBases.ShieldDef));
        double shieldRate = mods.Apply(CombatStat.RShld, formula.ShieldRate);
        bool bow = gearBases.Weapon == WeaponType.Bow;

        return new ActorSnapshot(
            pAtk, pDef, pAtkSpd, accuracy, evasion, critPermille, critDamageMod, critDamageAdd,
            attackRange, shieldDef, shieldRate, gearBases.RandomDamage, Playable: true, bow,
            ZoneCombat.WeaponReuseDelayMs(gearBases.Weapon));
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
            playable, bow, ZoneCombat.WeaponReuseDelayMs(bow ? WeaponType.Bow : WeaponType.None));
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
    int ReuseDelayMs);

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
