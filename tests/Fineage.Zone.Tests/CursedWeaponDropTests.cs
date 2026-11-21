using Fineage.Protocol;
using Fineage.Zone.CursedWeapons;
using Fineage.Zone.Geo;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mob death cursed-blade drop, ground pickup activation, and disappearance tick (issue #787).
/// </summary>
public sealed class CursedWeaponDropTests
{
    const int Gremlin = 20001;

    sealed class AlwaysRoll : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0;
    }

    static SystemMessageMsg LastSystem(NetBridgeRecorder rec) =>
        Assert.IsType<SystemMessageMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);

    [Fact]
    public async Task Mob_death_drops_blade_pickup_activates_and_expiry_broadcasts_and_persists()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        var bench = new WorldBench(
            clock: () => clock.Now,
            geo: GeoEngine.Empty,
            dropRandom: new AlwaysRoll(),
            combatRandom: new AlwaysRoll());
        var kit = await bench.EnterAsync(level: 76);
        var mob = bench.World.SpawnMonsterForTests(Gremlin, kit.Player.X + 40, kit.Player.Y, kit.Player.Z);
        mob.MaxHp = mob.CurHp = 1;
        bench.Rec.Calls.Clear();

        bench.World.KillForTest(mob, kit.Player);

        var runtime = bench.World.CursedWeaponsForTest.Get(OlympiadProtocol.Zariche);
        Assert.NotNull(runtime);
        Assert.True(runtime!.Dropped);
        Assert.False(runtime.Activated);
        Assert.True(runtime.GroundObjectId > 0);

        Assert.True(bench.World.TryGetEntityForTest(runtime.GroundObjectId, out var ground));
        Assert.Equal(OlympiadProtocol.Zariche, ground!.GroundItemId);
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutCursedWeaponList)));
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutCursedWeaponLocation)));
        Assert.Equal(ZoneWorld.CursedSysDroppedInRegion, LastSystem(bench.Rec).Id);
        Assert.True(bench.Store.ListCursedWeapons().Single(r => r.ItemId == OlympiadProtocol.Zariche).Dropped);

        bench.Rec.Calls.Clear();
        Assert.Null(await bench.World.TryPickupAsync(kit.Session, runtime.GroundObjectId, default));

        Assert.True(runtime.Activated);
        Assert.False(runtime.Dropped);
        Assert.Equal(kit.ObjectId, runtime.OwnerObjectId);
        Assert.Equal(CursedWeaponRuntime.KarmaWhileWielding, kit.Player.Karma);
        Assert.True(bench.World.WieldsCursedWeaponForTest(kit.Player));
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutCursedWeaponList)));
        Assert.Equal(ZoneWorld.CursedSysOwnerAppeared, LastSystem(bench.Rec).Id);
        Assert.True(bench.Store.ListCursedWeapons().Single(r => r.ItemId == OlympiadProtocol.Zariche).Activated);

        runtime.EndTimeUnixMs = clock.Now.ToUnixTimeMilliseconds() - 1;
        bench.Rec.Calls.Clear();
        bench.World.RunStepForTest();

        Assert.Equal(ZoneWorld.CursedSysDisappeared, LastSystem(bench.Rec).Id);
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutCursedWeaponList)));
        Assert.False(runtime.IsActive);
        Assert.DoesNotContain(bench.Store.ListCursedWeapons(), r => r.ItemId == OlympiadProtocol.Zariche && r.Activated);
    }

    [Fact]
    public async Task Cursed_drop_skips_excluded_npc_types()
    {
        var bench = new WorldBench(geo: GeoEngine.Empty, dropRandom: new AlwaysRoll());
        var kit = await bench.EnterAsync(level: 76);
        bench.Rec.Calls.Clear();

        const int QueenAntGrandBoss = 29001;
        var grandBoss = bench.World.SpawnMonsterForTests(QueenAntGrandBoss, kit.Player.X + 40, kit.Player.Y, kit.Player.Z);
        grandBoss.MaxHp = grandBoss.CurHp = 1;
        bench.World.KillForTest(grandBoss, kit.Player);

        foreach (var call in bench.Rec.Of(nameof(IWorldEventPublisher.FanoutDropItem)))
        {
            var drop = Assert.IsType<DropItemMsg>(call[1]);
            Assert.NotEqual(OlympiadProtocol.Zariche, drop.ItemId);
            Assert.NotEqual(OlympiadProtocol.Akamanah, drop.ItemId);
        }

        Assert.False(bench.World.CursedWeaponsForTest.Get(OlympiadProtocol.Zariche)?.Dropped ?? false);
    }
}