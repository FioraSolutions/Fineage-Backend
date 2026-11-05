using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius passive masteries (operateType P, &lt;using kind&gt;) and per-NPC atkRange reach.</summary>
public sealed class ZonePassiveMasteryTests
{
    static (ZoneWorld World, ZoneEntity Player, PlayerInventory Bag) Fighter()
    {
        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 0, Level = 1, Inventory = bag };
        return (new ZoneWorld(), player, bag);
    }

    [Fact]
    public void Passives_catalog_carries_using_conditions()
    {
        var cat = SkillStatEffectCatalog.LoadPassives();
        Assert.True(cat.TryGet(231, out var heavy));
        Assert.Equal(new[] { "HEAVY" }, heavy.Funcs.Single().Using);
        Assert.True(cat.TryGet(141, out var wm));
        Assert.All(wm.Funcs, f => Assert.Null(f.Using));
    }

    [Fact]
    public void Weapon_mastery_and_heavy_armor_mastery_feed_live_stats()
    {
        var (world, p, bag) = Fighter();
        foreach (int id in new[] { 219, 43, 26, 34 }) // Sword Breaker, Wooden Helmet, Bronze Breastplate, Bronze Gaiters (heavy)
            bag.Equip(bag.Add(id));
        p.KnownSkills[141] = 1; // Weapon Mastery: pAtk ×1.085 +2
        p.KnownSkills[231] = 1; // Heavy Armor Mastery: pDef +1.9 using HEAVY
        var s = world.PlayerStats(p);
        Assert.Equal(33, s.PAtk);  // 29 * 1.085 + 2
        Assert.Equal(135, s.PDef); // 134 + 1.9
    }

    [Fact]
    public void Armor_mastery_needs_matching_chest_and_legs()
    {
        var (world, p, bag) = Fighter();
        bag.Equip(bag.Add(26)); // heavy breastplate only, not full-body
        p.KnownSkills[231] = 1;
        Assert.False(ZoneWorld.Wearing(p, ["HEAVY"]));
        int without = world.PlayerStats(p).PDef;
        bag.Equip(bag.Add(34));
        Assert.True(ZoneWorld.Wearing(p, ["HEAVY"]));
        Assert.False(ZoneWorld.Wearing(p, ["LIGHT", "BOW"]));
        Assert.True(world.PlayerStats(p).PDef > without);
    }

    [Fact]
    public void Npc_reach_follows_template_attack_range()
    {
        Assert.Equal(ZoneCombat.MobMeleeRange, ZoneCombat.MobAttackReach(0));
        Assert.Equal(ZoneCombat.MobMeleeRange, ZoneCombat.MobAttackReach(40));
        Assert.Equal(1140, ZoneCombat.MobAttackReach(1100));
        var spawns = Fineage.Zone.Spawns.SpawnCatalog.LoadDefault();
        Assert.Contains(spawns.GetMap(Fineage.Zone.Spawns.SpawnCatalog.DefaultMapId).Npcs, n => n.AttackRange > 0);
    }
}
