using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Game.Skills;
using Fineage.Zone;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Every skill the world can learn or grant as a passive must resolve in the passive stat catalog.
/// This is not cosmetic: <c>ZoneWorldStats</c> folds known/clan/gear passives into PAtk/PDef/regen, so an
/// unresolved id silently drops its combat mods and logs <c>skill-passives missing skillId=N</c> on every
/// stat rebuild. The catalog must therefore answer for every passive the datapack can hand a player, and a
/// passive that simply carries no combat func must still be present as an explicit empty row.
/// </summary>
public sealed class ZonePassiveCatalogCoverageTests
{
    [Fact]
    public void Every_class_tree_passive_resolves_in_the_passive_catalog()
    {
        var passives = SkillStatEffectCatalog.LoadPassives();
        var skills = SkillCatalog.Load(FindDataFile(SkillCatalog.FileName));
        var trees = SkillTreeCatalog.Load(FindDataFile(SkillTreeCatalog.FileName));

        var missing = new SortedSet<int>();
        foreach (var tree in trees.Trees)
            foreach (var entry in tree.Skills)
            {
                if (skills.Find(entry.SkillId)?.Passive != true)
                    continue;
                if (!passives.TryGet(entry.SkillId, out _))
                    missing.Add(entry.SkillId);
            }

        Assert.True(missing.Count == 0,
            $"class-tree passives with no catalog row: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_armor_set_granted_skill_resolves_in_the_passive_catalog()
    {
        var passives = SkillStatEffectCatalog.LoadPassives();
        var sets = ArmorSetCatalog.LoadDefault();
        var missing = new SortedSet<int>();
        foreach (var set in sets.All)
            foreach (var row in set.Skills.Concat(set.ShieldSkills).Concat(set.Enchant6Skills))
                if (!passives.TryGet(row.Id, out _))
                    missing.Add(row.Id);

        Assert.True(missing.Count == 0,
            $"armor set skills with no catalog row: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_clan_skill_resolves_in_the_passive_catalog()
    {
        var passives = SkillStatEffectCatalog.LoadPassives();
        var pledge = PledgeSkillCatalog.LoadDefault();
        var missing = pledge.Rows
            .Select(static row => row.SkillId)
            .Distinct()
            .Where(id => !passives.TryGet(id, out _))
            .Order()
            .ToArray();

        Assert.True(missing.Length == 0,
            $"clan skills with no catalog row: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Active_skills_learned_from_the_book_do_not_fold_as_passives()
    {
        // Heal 1011 / Might 1068 are actives: they reach the stat line through an abnormal, never through the
        // passive catalog. Feeding them to the passive fold both warned and risked applying the buff func twice.
        var (world, player, _) = Fighter();
        player.KnownSkills[1011] = 1;
        player.KnownSkills[1068] = 1;

        var passives = world.ActivePassiveLevels(player);
        Assert.DoesNotContain(1011, passives.Keys);
        Assert.DoesNotContain(1068, passives.Keys);

        // A real passive in the same book still folds.
        player.KnownSkills[141] = 1; // Weapon Mastery
        Assert.Contains(141, world.ActivePassiveLevels(player).Keys);
    }

    [Fact]
    public void An_unknown_gear_skill_id_still_folds_as_a_passive()
    {
        // Gear/set/clan ids come from Mobius Inventory/Clan additions that skills.json does not list.
        // They must not be dropped just because the book cannot classify them.
        var (world, player, _) = Fighter();
        player.GearSkills[3006] = 1; // Equip Set Items, granted by armor sets and absent from skills.json

        Assert.Contains(3006, world.ActivePassiveLevels(player).Keys);
    }

    [Fact]
    public void A_learned_passive_without_combat_mods_still_resolves()
    {
        // Lucky 194 is a real Mobius passive (operateType P) with no <for> funcs.
        var passives = SkillStatEffectCatalog.LoadPassives();
        Assert.True(passives.TryGet(194, out var lucky), "passive 194 (Lucky) missing from the catalog");
        Assert.Empty(lucky.Funcs);

        // It must not poison a fold: a statless passive adds nothing while a real one still applies.
        var known = new Dictionary<int, int> { [194] = 1, [141] = 1 };
        var mods = passives.CollectPassives(known, wearing: _ => true);
        Assert.NotSame(CombatStatMods.None, mods);
    }

    [Fact]
    public void Passive_fold_reports_no_missing_ids_for_the_passives_the_old_catalog_dropped()
    {
        // These are the ids the soak logged: statless passives plus actives that arrive through GearSkills.
        var passives = SkillStatEffectCatalog.LoadPassives();
        var known = new Dictionary<int, int>
        {
            [194] = 1,  // Lucky
            [239] = 1,  // Expertise D
            [1320] = 1, // Create Common Item
            [1405] = 1, // Divine Inspiration
            [3006] = 1, // Equip Set Items
        };
        var reported = new List<int>();
        passives.CollectPassives(known, wearing: _ => true, onMissing: reported.Add);

        Assert.Empty(reported);
    }

    static (ZoneWorld World, ZoneEntity Player, PlayerInventory Bag) Fighter()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 40, Inventory = bag };
        return (new ZoneWorld(), player, bag);
    }

    static string FindDataFile(string name)
    {
        var path = TeleportCatalog.FindFile(name);
        Assert.NotNull(path);
        return path!;
    }
}
