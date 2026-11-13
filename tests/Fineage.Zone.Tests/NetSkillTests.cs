using Fineage.Game;
using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Skills on the zone: the auto-get book the enter burst carries, Power Strike / Wind Strike casts that damage a
/// gremlin, lock movement while casting, and pull aggro. Ported off the V2 wire; the assertions read the sheet the
/// Net enter projector builds and the recorded <see cref="IWorldEventPublisher"/> combat fanout.
/// </summary>
public sealed class NetSkillTests
{
    const int SquiresSword = 2369;
    const int PowerStrike = 3;
    const int WindStrike = 1177;
    const int AuraFlare = 1231;
    const int FireVortex = 1339;

    static DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    [Fact]
    public async Task Enter_includes_mobius_auto_get_skills()
    {
        var bench = new WorldBench(clock: () => Now);
        BenchPlayer actor = await bench.EnterAsync("skill", "Caster");

        // The Net enter burst ships the same book the projector builds from the live entity.
        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var entity));
        var packets = NetEnterWorldProjector.FromEntity(entity, EntityId.FromObjectId(actor.ObjectId), 0);
        Assert.Contains(packets.Skills.Skills, row => row.SkillId == 194);
        Assert.Equal(entity.KnownSkills.Count, packets.Skills.Skills.Length);

        // A level up rebuilds the book and hands the client a fresh SkillList.
        int before = entity.KnownSkills.Count;
        bench.Rec.Calls.Clear();
        Assert.True(bench.World.TrySetPlayerLevelForTests(actor.Session, 20));
        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var after));
        Assert.True(after.KnownSkills.Count > before);
        var list = Assert.IsType<SkillListMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)).Last()[1]);
        Assert.Equal(after.KnownSkills.Count, list.Skills.Length);
        Assert.Contains(list.Skills, row => row.SkillId == 194);
    }

    [Fact]
    public async Task SkillUse_power_strike_damages_gremlin_and_sends_magic_skill_use()
    {
        // A fixed clock and seed keep the gremlin's swing deterministic.
        Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var bench = new WorldBench(
            clock: () => Now,
            spawns: Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(),
            mapId: null);
        // Enter where the gremlins stand: the training hall is far from the default town spawn, and a click-to-move
        // beyond MaxStep is refused as "range".
        var gremlinHome = TalkingIsland.Gremlins[0];
        BenchPlayer actor = await bench.EnterAsync(
            "duel", "Slayer",
            classId: HumanClasses.Fighter,
            level: 5,
            x: gremlinHome.X, y: gremlinHome.Y, z: gremlinHome.Z);

        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var caster));
        int gremlinObjectId = GremlinObjectId(bench.World);
        Assert.True(gremlinObjectId > 0);

        // Mobius Power Strike: 70·(P.Atk + power)/P.Def. With the kit weapon it one-shots a 96 HP gremlin at L5,
        // so cast with the weakest sword (Squire's Sword, P.Atk 6) — Power Strike needs SWORD/BLUNT.
        caster.Inventory!.Equip(caster.Inventory.Add(SquiresSword));
        Assert.True(bench.World.TryGetEntityById(gremlinObjectId, out var gremlin));
        Assert.Null(await bench.World.TryMoveAsync(actor.Session, gremlin.X, gremlin.Y, gremlin.Z,
            CancellationToken.None));
        Assert.Null(await bench.World.TrySelectTargetAsync(actor.Session, gremlinObjectId, CancellationToken.None));

        // Drop the enter-knownlist status rows so the next assertions only see what the cast itself emits.
        bench.Rec.Calls.Clear();
        Assert.Null(await bench.World.TrySkillUseAsync(actor.Session, PowerStrike, gremlinObjectId,
            CancellationToken.None));

        // Mobius onMagicHitTimer: the cast announces itself, and movement is deferred until the hit lands.
        var use = Assert.IsType<MagicSkillUseMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)).Last()[1]);
        Assert.Equal(PowerStrike, use.SkillId);
        Assert.Equal(gremlinObjectId, use.TargetObjectId);
        int beforeMoveX = caster.X;
        Assert.Null(await bench.World.TryMoveAsync(actor.Session, caster.X + 10, caster.Y, caster.Z,
            CancellationToken.None));
        // Net clients are not refused: the intention parks on the session and runs once the cast completes.
        Assert.NotNull(actor.Session.QueuedMove);
        Assert.Equal(beforeMoveX, caster.X);

        // Nothing has been damaged on key press, only after the hit timer runs.
        Assert.DoesNotContain(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate)),
            args => args[1] is StatusUpdateMsg status && status.ObjectId == gremlinObjectId);

        Now = Now.AddSeconds(5);
        await bench.World.RunCombatTickAsync(CancellationToken.None);

        var hit = LastStatusFor(bench.Rec, gremlinObjectId);
        Assert.True(hit.Values[0] > 0);
        Assert.True(hit.Values[0] < hit.Values[1]);

        // Mobius addDamageHate -> the mob swings back at the attacker.
        bench.Rec.Calls.Clear();
        Now = Now.AddSeconds(2);
        await bench.World.RunCombatTickAsync(CancellationToken.None);

        Assert.True(bench.World.TryGetEntityById(gremlinObjectId, out var mob));
        Assert.Equal(actor.ObjectId, mob.AggroTargetId);
        Assert.NotEmpty(bench.Rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
    }

    /// <summary>#522: Human Mystic Wind Strike must reduce gremlin HP after hitTime (staff melee is separate).</summary>
    [Fact]
    public async Task SkillUse_wind_strike_damages_gremlin_for_human_mystic()
    {
        Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var bench = new WorldBench(
            clock: () => Now,
            spawns: Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(),
            mapId: null);
        var gremlinHome = TalkingIsland.Gremlins[0];
        BenchPlayer actor = await bench.EnterAsync(
            "mage", "WindKid",
            classId: HumanClasses.Mystic,
            level: 1,
            x: gremlinHome.X, y: gremlinHome.Y, z: gremlinHome.Z);

        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var caster));
        Assert.Contains(caster.KnownSkills, kv => kv.Key == WindStrike);
        int gremlinObjectId = GremlinObjectId(bench.World);
        Assert.True(gremlinObjectId > 0);
        Assert.True(bench.World.TryGetEntityById(gremlinObjectId, out var gremlin));
        int hpBefore = gremlin.CurHp;

        Assert.Null(await bench.World.TrySelectTargetAsync(actor.Session, gremlinObjectId, CancellationToken.None));

        bench.Rec.Calls.Clear();
        Assert.Null(await bench.World.TrySkillUseAsync(actor.Session, WindStrike, gremlinObjectId,
            CancellationToken.None));

        var use = Assert.IsType<MagicSkillUseMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)).Last()[1]);
        Assert.Equal(WindStrike, use.SkillId);
        Assert.Equal(gremlinObjectId, use.TargetObjectId);
        Assert.NotNull(caster.PendingHit);

        // Wind Strike hitTime is ~4s at base C.Spd; advance past it.
        Now = Now.AddSeconds(6);
        await bench.World.RunCombatTickAsync(CancellationToken.None);

        Assert.True(bench.World.TryGetEntityById(gremlinObjectId, out var after));
        Assert.True(after.CurHp < hpBefore, $"expected Wind Strike damage, hp {hpBefore} → {after.CurHp}");
        var hit = LastStatusFor(bench.Rec, gremlinObjectId);
        Assert.True(hit.Values[0] < hit.Values[1]);
    }

    /// <summary>
    /// Aura Flare / Fire Vortex are isDebuff+abnormal in skills.json but live in skill-combat —
    /// they must still roll magic damage (not the pure-CC debuff path).
    /// </summary>
    [Fact]
    public async Task SkillUse_aura_flare_damages_gremlin_despite_debuff_flag()
    {
        Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var bench = new WorldBench(
            clock: () => Now,
            spawns: Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(),
            mapId: null);
        var gremlinHome = TalkingIsland.Gremlins[0];
        BenchPlayer actor = await bench.EnterAsync(
            "flare", "FlareKid",
            classId: HumanClasses.Mystic,
            level: 40,
            x: gremlinHome.X, y: gremlinHome.Y, z: gremlinHome.Z);

        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var caster));
        caster.KnownSkills[AuraFlare] = 1;
        int gremlinObjectId = GremlinObjectId(bench.World);
        Assert.True(bench.World.TryGetEntityById(gremlinObjectId, out var gremlin));
        int hpBefore = gremlin.CurHp;

        Assert.Null(await bench.World.TrySelectTargetAsync(actor.Session, gremlinObjectId, CancellationToken.None));
        Assert.Null(await bench.World.TrySkillUseAsync(actor.Session, AuraFlare, gremlinObjectId,
            CancellationToken.None));
        Assert.NotNull(caster.PendingHit);

        Now = Now.AddSeconds(3);
        await bench.World.RunCombatTickAsync(CancellationToken.None);

        Assert.True(bench.World.TryGetEntityById(gremlinObjectId, out var after));
        Assert.True(after.CurHp < hpBefore, $"expected Aura Flare damage, hp {hpBefore} → {after.CurHp}");
    }

    [Fact]
    public async Task SkillUse_fire_vortex_damages_gremlin_despite_debuff_flag()
    {
        Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);
        var bench = new WorldBench(
            clock: () => Now,
            spawns: Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(),
            mapId: null,
            // calcEffectSuccess clamps Fire Vortex to 90%, so Random.Shared sometimes resists the abnormal.
            combatRandom: new AlwaysLandRandom());
        var gremlinHome = TalkingIsland.Gremlins[0];
        BenchPlayer actor = await bench.EnterAsync(
            "vortex", "VortexKid",
            classId: HumanClasses.Mystic,
            level: 76,
            x: gremlinHome.X, y: gremlinHome.Y, z: gremlinHome.Z);

        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var caster));
        caster.KnownSkills[FireVortex] = 1;
        int gremlinObjectId = GremlinObjectId(bench.World);
        Assert.True(bench.World.TryGetEntityById(gremlinObjectId, out var gremlin));
        int hpBefore = gremlin.CurHp;

        Assert.Null(await bench.World.TrySelectTargetAsync(actor.Session, gremlinObjectId, CancellationToken.None));
        Assert.Null(await bench.World.TrySkillUseAsync(actor.Session, FireVortex, gremlinObjectId,
            CancellationToken.None));
        Assert.NotNull(caster.PendingHit);

        // Fire Vortex hitTime is 6s at base C.Spd.
        Now = Now.AddSeconds(10);
        await bench.World.RunCombatTickAsync(CancellationToken.None);

        Assert.True(bench.World.TryGetEntityById(gremlinObjectId, out var after));
        Assert.True(after.CurHp < hpBefore, $"expected Fire Vortex damage, hp {hpBefore} → {after.CurHp}");
        Assert.Contains(after.Abnormals, a => a.Id == FireVortex);
    }

    static int GremlinObjectId(ZoneWorld world)
    {
        var info = world.NpcInfoByTemplateForTests(TalkingIsland.GremlinNpcId);
        return info.ObjectId;
    }

    static StatusUpdateMsg LastStatusFor(NetBridgeRecorder rec, int objectId) =>
        Assert.IsType<StatusUpdateMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutStatusUpdate))
            .Select(args => (StatusUpdateMsg)args[1])
            .Last(status => status.ObjectId == objectId));

    /// <summary>Every combat roll succeeds: magic hit and debuff land (<c>Rnd(100) == 0</c>).</summary>
    sealed class AlwaysLandRandom : Random
    {
        public override int Next(int maxValue) => 0;

        public override int Next(int minValue, int maxValue) => minValue;
    }
}
