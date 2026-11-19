using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Trick (11) is a negative-effect A1 whose payload is <c>DeleteHateOfMe</c> (EffectType.HATE) plus
/// <c>TargetCancel</c>. Mobius <c>Creature.callSkill</c> therefore does not <c>notifyActionAttacked</c>,
/// so an idle mob must not start attacking the caster.
/// </summary>
public sealed class ZoneTrickTests
{
    const int TrickSkill = 11;
    const int MobId = 9_400_641;

    /// <summary>Every <c>chance</c> roll succeeds (<c>Rnd.get(100)</c> → 0).</summary>
    sealed class AlwaysRoll : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0d;
    }

    /// <summary>Every <c>chance</c> roll fails (<c>Rnd.get(100)</c> → 99).</summary>
    sealed class NeverRoll : Random
    {
        public override int Next(int maxValue) => maxValue > 0 ? maxValue - 1 : 0;
        public override int Next(int minValue, int maxValue) => maxValue > minValue ? maxValue - 1 : minValue;
        public override double NextDouble() => 0.99d;
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob)> EnterAsync(Random combatRandom)
    {
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["trick"] = new("trick", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 641, "Gremlin",
                    NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 40, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        }), "trick", geo: GeoEngine.Empty, combatRandom: combatRandom);
        var (session, player) = await kit.EnterAsync(classId: 8, race: 0, level: 60); // Treasure Hunter
        player.KnownSkills[TrickSkill] = 1;
        player.CurMp = player.MaxMp = 500;
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.MaxHp = mob.CurHp = 50_000;
        mob.GlobalAggro = -10;
        mob.HateList.Clear();
        mob.AggroTargetId = 0;
        return (kit, session, player, mob);
    }

    [Fact]
    public void Trick_row_is_a_hate_effect()
    {
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(TrickSkill, out var rows));
        Assert.Contains(rows, r => r.Name.Equals("DeleteHateOfMe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r => r.Name.Equals("TargetCancel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Trick_on_an_idle_mob_does_not_wake_it()
    {
        // DeleteHateOfMe is refused so effectPoint hate stays on the list — still must not set the aggro target.
        var (kit, session, player, mob) = await EnterAsync(new NeverRoll());
        Assert.Equal(0, mob.AggroTargetId);
        Assert.True(mob.GlobalAggro < 0);

        Assert.Null(await kit.World.TrySkillUseAsync(session, TrickSkill, MobId, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        Assert.Equal(0, mob.AggroTargetId);
        Assert.True(mob.GlobalAggro < 0);
        // effectPoint hate may be recorded, but that alone is not an attack intention.
        Assert.True(mob.HateList.GetValueOrDefault(player.ObjectId) > 0);
    }

    [Fact]
    public async Task Trick_on_a_fighting_mob_still_drops_hate_of_the_caster()
    {
        var (kit, session, player, mob) = await EnterAsync(new AlwaysRoll());
        mob.HateList[player.ObjectId] = 5_000;
        mob.AggroTargetId = player.ObjectId;

        Assert.Null(await kit.World.TrySkillUseAsync(session, TrickSkill, MobId, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        Assert.DoesNotContain(player.ObjectId, mob.HateList.Keys);
        Assert.NotEqual(player.ObjectId, mob.AggroTargetId);
    }
}
