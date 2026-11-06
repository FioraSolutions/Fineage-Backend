using Fineage.Game.Services;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class DeathDropRulesTests
{
    static DeathDropRules Rules() => DeathDropRules.LoadDefault();

    [Fact]
    public void Karma_path_requires_pk_limit()
    {
        var r = Rules();
        // Below PK limit: no karma rates; also no NPC path when killer is a player.
        Assert.Null(r.RatesFor(karma: 100, pkKills: r.KarmaPkLimit - 1, killerIsNpc: false, victimLevel: 20));
        Assert.NotNull(r.RatesFor(karma: 100, pkKills: r.KarmaPkLimit, killerIsNpc: false, victimLevel: 1));
    }

    [Fact]
    public void Npc_path_requires_level_above_4()
    {
        var r = Rules();
        Assert.Null(r.RatesFor(0, 0, killerIsNpc: true, victimLevel: 4));
        Assert.NotNull(r.RatesFor(0, 0, killerIsNpc: true, victimLevel: 5));
        Assert.Null(r.RatesFor(0, 0, killerIsNpc: false, victimLevel: 40));
    }

    [Fact]
    public void SelectDrops_skips_adena_and_respects_limit()
    {
        var r = Rules();
        var rates = new DeathDropRules.DropRates(Percent: 100, Item: 100, Equip: 100, EquipWeapon: 100, Limit: 2);
        var items = new List<DeathDropRules.DropCandidate>
        {
            new(1, WarehouseRules.AdenaItemId, 100, false, false),
            new(2, 1864, 1, false, false),
            new(3, 1865, 1, false, false),
            new(4, 1866, 1, false, false),
        };
        var dropped = r.SelectDrops(items, rates, new Seq(0, 0, 0, 0)); // always succeed
        Assert.Equal(2, dropped.Count);
        Assert.DoesNotContain(1, dropped);
    }

    [Fact]
    public void SelectDrops_uses_weapon_equip_rate()
    {
        var r = Rules();
        // Overall 100%, equip weapon 0% → nothing from equipped weapon
        var rates = new DeathDropRules.DropRates(100, Item: 0, Equip: 0, EquipWeapon: 0, Limit: 5);
        var items = new List<DeathDropRules.DropCandidate> { new(9, 2, 1, Equipped: true, Weapon: true) };
        Assert.Empty(r.SelectDrops(items, rates, new Seq(0, 0)));
    }

    sealed class Seq : Random
    {
        readonly Queue<int> _q;
        public Seq(params int[] values) => _q = new Queue<int>(values);
        public override int Next(int maxValue) => _q.Count > 0 ? _q.Dequeue() % Math.Max(1, maxValue) : 0;
    }
}
