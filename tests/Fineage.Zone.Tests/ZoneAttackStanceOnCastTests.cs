using System;
using System.Threading;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius <c>Creature.onMagicFinalizer</c> raises the attack stance only for <c>skill.isBad()</c>:
/// <c>effectPoint &lt; 0 &amp;&amp; targetType != SELF</c>. <c>AttackStanceTaskManager</c> then holds the 15 s window
/// that makes the client draw <c>atkwait</c> — and that same window blocks summon / private store / clan actions.
/// <para>
/// A self buff such as Dash (4, effectPoint +204/+438, targetType SELF) must not open it: retail leaves the character
/// in the peace pose after the gesture. Power Strike (3, effectPoint −52, targetType ONE) still must.
/// </para>
/// </summary>
public sealed class ZoneAttackStanceOnCastTests
{
    const int Dash = 4;
    const int PowerStrike = 3;
    const int SquiresSword = 2369;

    static WorldBench Bench() => new(
        clock: () => DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000),
        spawns: Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(),
        mapId: null);

    /// <summary>Both skills are learned from a village master, so the cast precheck needs the known row.</summary>
    static void Learn(ZoneEntity player, int skillId) => player.KnownSkills[skillId] = 1;

    [Fact]
    public async Task Self_buff_dash_leaves_the_caster_out_of_the_attack_stance()
    {
        var bench = Bench();
        var home = TalkingIsland.Gremlins[0];
        BenchPlayer actor = await bench.EnterAsync(
            "stancebuff", "Slayer",
            classId: HumanClasses.Fighter,
            level: 20,
            x: home.X, y: home.Y, z: home.Z);

        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var caster));
        Learn(caster, Dash);
        caster.CurMp = caster.MaxMp;
        long now = bench.Now().ToUnixTimeMilliseconds();
        Assert.Equal(0, caster.CombatUntilUnixMs);

        bench.Rec.Calls.Clear();
        Assert.Null(await bench.World.TrySkillUseAsync(actor.Session, Dash, caster.ObjectId, CancellationToken.None));

        // The cast still goes out: the client needs MagicSkillUse for the gesture and the effect.
        var use = Assert.IsType<MagicSkillUseMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)).Last()[1]);
        Assert.Equal(Dash, use.SkillId);
        // Mobius onMagicFinalizer: not isBad() → no clientStartAutoAttack / AutoAttackStart and no stance window.
        Assert.DoesNotContain(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutAttackStance)),
            args => args[1] is int id && id == caster.ObjectId && args[2] is true);
        Assert.True(caster.CombatUntilUnixMs <= now);
    }

    [Fact]
    public async Task Offensive_skill_still_opens_the_attack_stance()
    {
        var bench = Bench();
        var home = TalkingIsland.Gremlins[0];
        BenchPlayer actor = await bench.EnterAsync(
            "stanceattack", "Slayer",
            classId: HumanClasses.Fighter,
            level: 5,
            x: home.X, y: home.Y, z: home.Z);

        Assert.True(bench.World.TryGetLivePlayer(actor.Session, out var caster));
        Learn(caster, PowerStrike);
        int gremlinObjectId = GremlinObjectId(bench.World);
        Assert.True(gremlinObjectId > 0);
        // Power Strike carries <using kind="SWORD,BLUNT">. Official Interlude already equips Squire's Sword;
        // keep the explicit equip so the test still holds if the create table changes.
        caster.Inventory!.Equip(caster.Inventory.Add(SquiresSword));
        Assert.True(bench.World.TryGetEntityById(gremlinObjectId, out var gremlin));
        Assert.Null(await bench.World.TryMoveAsync(actor.Session, gremlin.X, gremlin.Y, gremlin.Z,
            CancellationToken.None));
        Assert.Null(await bench.World.TrySelectTargetAsync(actor.Session, gremlinObjectId, CancellationToken.None));
        caster.CurMp = caster.MaxMp;
        Assert.True(await bench.World.TrySkillUseAsync(actor.Session, PowerStrike, gremlinObjectId,
            CancellationToken.None) is null);

        // The cast must actually have gone out — a deferred cast (out of range / sight) also returns null.
        var use = Assert.IsType<MagicSkillUseMsg>(
            bench.Rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)).Last()[1]);
        Assert.Equal(PowerStrike, use.SkillId);

        // Power Strike (3): effectPoint −52 with targetType ONE → isBad() → the 15 s stance opens. The
        // AutoAttackStart fanout itself comes from TickAttackStance on the world tick, so assert the window.
        long now = bench.Now().ToUnixTimeMilliseconds();
        Assert.Equal(now + ClanProtocol.CombatStanceMillis, caster.CombatUntilUnixMs);
    }

    static int GremlinObjectId(ZoneWorld world) =>
        world.NpcInfoByTemplateForTests(TalkingIsland.GremlinNpcId).ObjectId;
}
