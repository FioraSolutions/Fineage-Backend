using Fineage.Game;

namespace Fineage.Zone.Augmentation;

/// <summary>Fold equipped item augmentation stat rows into gear bases.</summary>
public static class AugmentationStatMods
{
    public static PlayerGearBases Apply(PlayerGearBases gear, PlayerInventory? inventory, AugmentationCatalog catalog)
    {
        if (inventory == null || !catalog.IsLoaded)
            return gear;

        double pAtk = gear.PAtk;
        double mAtk = gear.MAtk;
        double pDef = gear.PDef;
        double mDef = gear.MDef;
        double pSpd = gear.PAtkSpd;
        int crit = gear.CritRate;

        foreach (var item in inventory.Items)
        {
            if (!item.IsEquipped || !AugmentationRules.IsAugmented(item))
                continue;
            ApplyOption(catalog, item.Option1, ref pAtk, ref mAtk, ref pDef, ref mDef, ref pSpd, ref crit);
            ApplyOption(catalog, item.Option2, ref pAtk, ref mAtk, ref pDef, ref mDef, ref pSpd, ref crit);
        }

        return gear with
        {
            PAtk = pAtk,
            MAtk = mAtk,
            PDef = pDef,
            MDef = mDef,
            PAtkSpd = pSpd,
            CritRate = crit
        };
    }

    static void ApplyOption(
        AugmentationCatalog catalog,
        int optionId,
        ref double pAtk,
        ref double mAtk,
        ref double pDef,
        ref double mDef,
        ref double pSpd,
        ref int crit)
    {
        if (optionId <= 0 || !catalog.TryGetOption(optionId, out var opt))
        {
            if (optionId > 0)
                ZoneLog.Warn($"Augmentation option {optionId} missing from catalog.");
            return;
        }

        foreach (var row in opt.Stats)
        {
            if (row.Kind != "add")
                continue;
            switch (row.Stat.ToLowerInvariant())
            {
                case "patk": pAtk += row.Val; break;
                case "matk": mAtk += row.Val; break;
                case "pdef": pDef += row.Val; break;
                case "mdef": mDef += row.Val; break;
                case "patkspd": pSpd += row.Val; break;
                case "critrate": crit += (int)Math.Round(row.Val); break;
            }
        }
    }

    public static void CollectGearSkills(PlayerInventory? inventory, AugmentationCatalog catalog, IDictionary<int, int> target)
    {
        if (inventory == null || !catalog.IsLoaded)
            return;

        foreach (var item in inventory.Items)
        {
            if (!item.IsEquipped)
                continue;
            AddSkills(catalog, item.Option1, target);
            AddSkills(catalog, item.Option2, target);
        }
    }

    static void AddSkills(AugmentationCatalog catalog, int optionId, IDictionary<int, int> target)
    {
        if (optionId <= 0)
            return;
        if (catalog.TryGetOption(optionId, out var opt))
        {
            foreach (var sk in opt.Skills)
                PutSkill(target, sk.SkillId, sk.Level);
            return;
        }

        if (catalog.TryGetSkillMap(optionId, out var map) && map.SkillId > 0)
            PutSkill(target, map.SkillId, map.SkillLevel);
    }

    static void PutSkill(IDictionary<int, int> target, int skillId, int level)
    {
        if (!target.TryGetValue(skillId, out int have) || level > have)
            target[skillId] = level;
    }
}