using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.MobiusDiff;

/// <summary>
/// Fineage side of the differential harness. It reuses the production pure layers
/// (<see cref="PlayerEffectiveAttributes"/>, <see cref="PlayerGearBases"/>, <see cref="PlayerFormulaBases"/>,
/// <see cref="CombatStatMods"/>, <see cref="ZoneCombat"/>) and mirrors the order in
/// <c>ZoneWorldStats.PlayerStats</c> (template → gear → formula → abnormal funcs).
/// </summary>
public static class FineageProbe
{
    public static ActualMetrics Measure(Scenario scenario, GameData data, NpcCatalog npcs)
    {
        if (scenario.Attacker.ClassId < 0)
            throw new InvalidDataException($"{scenario.Id}: classId must be non-negative.");
        var template = data.Classes?.Get(scenario.Attacker.ClassId)
            ?? throw new InvalidDataException($"{scenario.Id}: classes.json is unavailable.");
        int level = scenario.Attacker.Level;

        var inventory = new PlayerInventory(data.Items);
        foreach (var gear in scenario.Attacker.Gear)
        {
            if (!Enum.TryParse<PaperdollSlot>(gear.Slot, ignoreCase: true, out var slot))
                throw new InvalidDataException($"{scenario.Id}: unknown paperdoll slot '{gear.Slot}'.");
            var pieces = inventory.AddPieces(gear.ItemId, 1, gear.Enchant);
            inventory.Equip(pieces[0]);
            if (inventory.GetEquipped(slot) == null)
                throw new InvalidDataException(
                    $"{scenario.Id}: item {gear.ItemId} did not occupy slot {gear.Slot}; body slot is {pieces[0].Template.BodySlot}.");
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
        var mods = BuffMods(scenario, inventory);

        // Mobius getPAtk/getPDef stay doubles through calcPhysDam; truncating here was the 71-vs-73 leftover
        // after the 76-coefficient fix.
        double pAtk = mods.Apply(CombatStat.PAtk, formula.PAtk);
        double pDef = Math.Max(1, mods.Apply(CombatStat.PDef, formula.PDef));
        int pAtkSpd = Math.Max(1, mods.Apply(CombatStat.PAtkSpd, formula.PAtkSpd));
        int accuracy = mods.Apply(CombatStat.Accuracy, formula.Accuracy);
        int evasion = mods.Apply(CombatStat.Evasion, formula.Evasion);
        int critPermille = mods.Apply(CombatStat.CritRate, formula.CritRate);
        double critDamageMod = mods.Apply(CombatStat.CritDmg, 1d);
        double critDamageAdd = mods.Apply(CombatStat.CritDmgAdd, 0d);

        var npc = npcs.Get(scenario.Target.NpcId);
        double npcEvasion = ZoneCombat.AccuracyOrEvasion(npc.Evasion, npc.Dex, npc.Level);
        int critChance = Math.Clamp(critPermille, 0, ZoneCombat.MaxCritPermille);
        int hitChance = ZoneCombat.HitChancePermille(accuracy, npcEvasion);
        int damageNoCrit = ZoneCombat.PhysicalDamage(pAtk, npc.PDef);

        var metrics = new ActualMetrics
        {
            ["pAtk"] = pAtk,
            ["pDef"] = pDef,
            ["pAtkSpd"] = pAtkSpd,
            ["accuracy"] = accuracy,
            ["evasion"] = evasion,
            ["critPermille"] = critPermille,
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
                    rnd, pAtk, npc.PDef, accuracy, npcEvasion, critChance, gearBases.RandomDamage,
                    scenario.Contact.Soulshot, (int)gearBases.WeaponGrade, 0, 0, scenario.Contact.AttackerBow,
                    1d, critDamageMod, 1d, critDamageAdd);
                total += damage;
            }

            metrics["damageMeanPerSwing"] = (double)total / scenario.Samples;
        }

        return metrics;
    }

    static CombatStatMods BuffMods(Scenario scenario, PlayerInventory inventory)
    {
        if (scenario.Attacker.Buffs.Count == 0)
            return CombatStatMods.None;
        var catalog = SkillStatEffectCatalog.LoadDefault();
        var sources = scenario.Attacker.Buffs
            .Select(b => new SkillStatSource(b.SkillId, b.Level, b.Level))
            .ToArray();
        return catalog.Collect(
            sources,
            onMissing: id => throw new InvalidDataException(
                $"{scenario.Id}: buff skill {id} is missing from {SkillStatEffectCatalog.FileName}."),
            wearing: kinds => ConditionUsingItemType.Matches(inventory, kinds),
            wearingSlot: slots => ConditionUsingItemType.MatchesSlot(inventory, slots));
    }
}

/// <summary>Normalized metric bag. Values are doubles so the same comparator handles points, permille and HP.</summary>
public sealed class ActualMetrics : Dictionary<string, double>
{
}
