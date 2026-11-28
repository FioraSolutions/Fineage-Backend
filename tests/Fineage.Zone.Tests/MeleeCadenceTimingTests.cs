using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Melee cadence against Mobius CT0 Interlude. Mobius schedules the next swing at exactly <c>timeAtk</c>
/// (<see cref="ZoneCombat.AttackTimeMs"/> = <c>500000 / pAtkSpd</c>) and lands the damage at exactly
/// <c>timeAtk / 2</c> (<c>Creature.doAttack</c> → <c>onHitTimer</c>), both as plain millisecond deadlines.
/// Fineage honours deadlines on the fixed 200 ms simulation tick
/// (<see cref="ZoneSimulationSettings.StepMilliseconds"/> = <see cref="ZoneCombat.AutoAttackTickMs"/>), so the observed
/// swing interval is <c>timeAtk</c> rounded up to that step and the damage lands on the first tick at or after
/// <c>timeAtk / 2</c> — up to 199 ms late.
/// <para>
/// These tests pin the current behavior for the player swing (pAtkSpd 300 → 1666 ms) and for the Gremlin's swing
/// (npc 20001, attackSpeed 253 → 1976 ms). The skipped test records the exact-Mobius target that a sub-tick deadline
/// scheduler would have to satisfy.
/// </para>
/// </summary>
public sealed class MeleeCadenceTimingTests
{
    /// <summary>Gremlin row of <c>data/npcs.json</c>: attackSpeed 253.</summary>
    const int GremlinNpcId = 20_001;
    const int GremlinObjectId = 9_500_001;
    const int GremlinAttackSpeed = 253;

    /// <summary>Human Fighter physical attack speed under test: 500000 / 300 = 1666 ms.</summary>
    const int PlayerAttackSpeed = 300;

    const int StepMs = ZoneCombat.AutoAttackTickMs;

    /// <summary>1666 ms: Mobius <c>calculateTimeBetweenAttacks</c> at pAtkSpd 300.</summary>
    static readonly int PlayerAtkMs = ZoneCombat.AttackTimeMs(PlayerAttackSpeed);

    /// <summary>1976 ms: Mobius <c>calculateTimeBetweenAttacks</c> at the Gremlin's 253.</summary>
    static readonly int GremlinAtkMs = ZoneCombat.AttackTimeMs(GremlinAttackSpeed);

    /// <summary>atkMs deadlines rounded up to the tick they are honoured on (1800 / 2000).</summary>
    static readonly int PlayerSwingStep = CeilToTick(PlayerAtkMs);
    static readonly int GremlinSwingStep = CeilToTick(GremlinAtkMs);

    /// <summary>atkMs / 2 deadlines rounded up to the tick the damage is applied on (1000 / 1000).</summary>
    static readonly int PlayerHitStep = CeilToTick(PlayerAtkMs / 2);
    static readonly int GremlinHitStep = CeilToTick(GremlinAtkMs / 2);

    static SpawnCatalog AdjacentMap() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["melee-cadence"] = new("melee-cadence", true,
            [
                new SpawnNpcDefinition(GremlinObjectId, GremlinNpcId, "Gremlin",
                    NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: GremlinAttackSpeed,
                    Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        });

    /// <summary>
    /// Elf-area world with a Human Fighter in melee reach of one Gremlin. Both HP pools are lifted far above what ten
    /// swings can do so neither side dies and the cadence runs to the tenth swing; the seeded combat stream keeps the
    /// rolls (and therefore hit ticks) reproducible.
    /// </summary>
    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob, NetBridgeRecorder Rec)>
        RigAsync(int seed)
    {
        var kit = new NetTestKit(AdjacentMap(), "melee-cadence", combatRandom: new Random(seed));
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        Assert.True(kit.World.TryGetEntityById(GremlinObjectId, out var mob),
            $"spawn map 'melee-cadence' has no object {GremlinObjectId}");

        player.MaxHp = 50_000;
        player.CurHp = 50_000;
        mob.MaxHp = 50_000;
        mob.CurHp = 50_000;

        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, session, player, mob, rec);
    }

    /// <summary>Attack packets the bridge sent, in order, for one attacker.</summary>
    static List<AttackMsg> AttacksBy(NetBridgeRecorder rec, int attackerObjectId) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutAttackState))
            .Select(args => Assert.IsType<AttackMsg>(args[1]))
            .Where(attack => attack.AttackerObjectId == attackerObjectId)
            .ToList();

    /// <summary><paramref name="ms"/> rounded up to the next simulation tick boundary.</summary>
    static int CeilToTick(int ms) => ms % StepMs == 0 ? ms : (ms / StepMs + 1) * StepMs;

    /// <summary>
    /// One swing and the tick its damage was applied on (null when the roll missed: Mobius applies no damage then,
    /// and Fineage's ResolveMeleeHit returns before any HP loss).
    /// </summary>
    readonly record struct Swing(long SwingTick, long? HitTick);

    /// <summary>
    /// Steps the simulation 200 ms at a time and records the tick time of every swing by <paramref name="attacker"/>
    /// and of every damage application on <paramref name="victim"/> (its <c>CurHp</c> dropping), until
    /// <paramref name="wanted"/> swings have been seen and their hits have had time to land.
    /// <paramref name="swingTicks"/> is seeded with swings already sent before the loop (RequestAttack swings on the
    /// command, not on a tick).
    /// </summary>
    static async Task<List<Swing>> CollectSwingsAsync(NetTestKit kit, NetBridgeRecorder rec, int attacker,
        ZoneEntity victim, List<long> swingTicks, int wanted, int maxSteps = 400)
    {
        Assert.Equal(swingTicks.Count, AttacksBy(rec, attacker).Count);
        int swings = swingTicks.Count;
        int hp = victim.CurHp;
        var hitTicks = new List<long>(wanted);

        // A hit is due atkMs/2 after its swing; the longest wait is one full tick window of lateness plus a step.
        int tailSteps = CeilToTick(ZoneCombat.AutoAttackIntervalMs / 2) / StepMs + 2;
        int quiet = 0;
        for (int step = 0; step < maxSteps; step++)
        {
            kit.AdvanceToTickCovering(kit.Now.ToUnixTimeMilliseconds() + StepMs);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
            long now = kit.Now.ToUnixTimeMilliseconds();

            int seen = AttacksBy(rec, attacker).Count;
            if (seen > swings)
            {
                Assert.Equal(swings + 1, seen); // one swing per attacker per tick
                swingTicks.Add(now);
                swings = seen;
            }
            if (victim.CurHp < hp)
            {
                hitTicks.Add(now);
                hp = victim.CurHp;
            }

            if (swings >= wanted && hitTicks.Count >= wanted)
                break;
            if (swings >= wanted && ++quiet >= tailSteps)
                break;
        }

        Assert.True(swings >= wanted,
            $"saw {swings} swing(s) from object {attacker} in {maxSteps} steps, expected {wanted}");

        var flags = AttacksBy(rec, attacker).Take(wanted).Select(a => a.Flags).ToList();
        Assert.Equal(wanted, swingTicks.Count);
        Assert.Equal(wanted, flags.Count);

        // Match each swing with the hit it queued: a miss queues no HP change, so hit ticks are consumed in order by
        // the swings that did not miss.
        var result = new List<Swing>(wanted);
        int hitIndex = 0;
        for (int i = 0; i < wanted; i++)
        {
            bool miss = (flags[i] & ZoneCombat.HitFlagMiss) != 0;
            long? hitTick = miss || hitIndex >= hitTicks.Count ? null : hitTicks[hitIndex++];
            result.Add(new Swing(swingTicks[i], hitTick));
        }

        return result;
    }

    /// <summary>
    /// Cadence of one attacker: every interval between swing ticks is exactly <c>atkMs</c> rounded up to the tick, and
    /// every swing that did not miss applies its damage on the first tick at or after <c>atkMs / 2</c> — i.e. 0..199 ms
    /// after the Mobius hit time, inside the same tick window the swing itself was delayed into.
    /// </summary>
    static void AssertCadence(IReadOnlyList<Swing> swings, int atkMs, int swingStep, int hitStep, string who)
    {
        Assert.True(swings.Count > 1, $"{who}: need at least two swings, got {swings.Count}");
        for (int i = 1; i < swings.Count; i++)
        {
            long interval = swings[i].SwingTick - swings[i - 1].SwingTick;
            Assert.True(interval == swingStep,
                $"{who}: swing {i} came {interval} ms after swing {i - 1}; Mobius atkMs {atkMs} honoured on the " +
                $"{StepMs} ms tick is {swingStep} ms");
        }

        int hits = 0;
        for (int i = 0; i < swings.Count; i++)
        {
            if (swings[i].HitTick is not long hitTick)
                continue; // the roll missed: Mobius sends no damage at hitTime either
            hits++;
            long latency = hitTick - swings[i].SwingTick;
            Assert.True(latency == hitStep,
                $"{who}: hit {i} landed {latency} ms after its swing; Mobius hitTime atkMs/2 = {atkMs / 2} ms is " +
                $"honoured on the {StepMs} ms tick at {hitStep} ms");
            long late = latency - atkMs / 2;
            Assert.True(late is >= 0 and < StepMs,
                $"{who}: hit {i} is {late} ms late against atkMs/2 = {atkMs / 2}; the tick window allows 0..{StepMs - 1}");
        }
        Assert.True(hits > 0, $"{who}: every one of {swings.Count} swings missed, the cadence is not exercised");
    }

    [Fact]
    public async Task Player_swings_on_the_tick_after_atkMs_and_lands_each_hit_half_a_swing_later()
    {
        var (kit, session, player, mob, rec) = await RigAsync(seed: 1);
        // Fix the speed this test is about: the Human Fighter line is 300 before the DEX bonus and gear, and a
        // weapon would replace it again, so the test pins the stat instead of depending on the starter kit.
        player.TestCombatFuncs = [new SkillStatFunc(CombatStat.PAtkSpd, '=', [PlayerAttackSpeed])];
        Assert.Equal(PlayerAttackSpeed, kit.World.PlayerStats(player).PAtkSpd);
        Assert.Equal(1_666, PlayerAtkMs);
        Assert.Equal(1_800, PlayerSwingStep);
        Assert.Equal(1_000, PlayerHitStep);

        Assert.Null(await kit.World.TrySelectTargetAsync(session, GremlinObjectId, CancellationToken.None));
        // RequestAttack swings on the command itself: that is swing zero, at the current tick boundary.
        Assert.Null(await kit.World.TryAttackAsync(session, GremlinObjectId, CancellationToken.None));
        Assert.Single(AttacksBy(rec, player.ObjectId));

        long firstSwingTick = kit.Now.ToUnixTimeMilliseconds();
        Assert.Equal(0, firstSwingTick % StepMs);

        var swings = await CollectSwingsAsync(kit, rec, player.ObjectId, mob, [firstSwingTick], wanted: 10);
        Assert.Equal(10, swings.Count);
        AssertCadence(swings, PlayerAtkMs, PlayerSwingStep, PlayerHitStep, "player pAtkSpd 300");
    }

    [Fact]
    public async Task Gremlin_swings_on_the_tick_after_its_atkMs_and_lands_each_hit_half_a_swing_later()
    {
        var (kit, session, player, mob, rec) = await RigAsync(seed: 1);
        Assert.Equal(1_976, GremlinAtkMs);
        Assert.Equal(2_000, GremlinSwingStep);
        Assert.Equal(1_000, GremlinHitStep);

        // The player's swing raises the Gremlin's hate (Mobius onEvtAttacked); the mob's first swing is scheduled at
        // hitTime after that hate, so the mob never swings on the tick the hate arrives.
        Assert.Null(await kit.World.TrySelectTargetAsync(session, GremlinObjectId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(session, GremlinObjectId, CancellationToken.None));

        // Only the mob's swings matter from here, so drop the player's auto-attack intention; the hate the opening
        // swing wrote stays and keeps the Gremlin on the player.
        player.Intent.AutoAttackTargetId = 0;

        var swings = await CollectSwingsAsync(kit, rec, mob.ObjectId, player, [], wanted: 10);
        Assert.Equal(player.ObjectId, mob.AggroTargetId);
        Assert.Equal(10, swings.Count);
        AssertCadence(swings, GremlinAtkMs, GremlinSwingStep, GremlinHitStep,
            $"Gremlin npc {GremlinNpcId} attackSpeed {GremlinAttackSpeed}");
    }

    /// <summary>
    /// The exact Mobius CT0 cadence: <c>Creature.calculateTimeBetweenAttacks</c> schedules the next swing exactly
    /// <c>timeAtk</c> ms later and <c>doAttack</c> lands the damage exactly <c>timeAtk / 2</c> ms later with no tick
    /// quantization — Mobius runs its AI on a 100 ms task manager and still keeps the millisecond deadline.
    /// </summary>
    [Fact(Skip = """
        Known divergence, not a defect these tests should hide: Fineage honours combat deadlines on its fixed 200 ms
        simulation tick, so the measured cadence is ceil(atkMs / 200) * 200 instead of atkMs, and the damage lands on
        the first tick at or after atkMs / 2.
          - player pAtkSpd 300: Mobius swing 1666 ms / hit 833 ms; Fineage swing 1800 ms / hit 1000 ms (+167 ms).
          - Gremlin npc 20001 attackSpeed 253: Mobius swing 1976 ms / hit 988 ms; Fineage swing 2000 ms / hit 1000 ms
            (+12 ms).
        That slows both sides versus Mobius and drifts the swing phase by up to a full step. Closing it needs sub-tick
        deadlines in the shared simulation clock (a deadline scheduler inside a step) — a change to the tick pipeline,
        not to a test. Enable this test when that lands.
        """)]
    public async Task Cadence_matches_mobius_millisecond_deadlines_exactly()
    {
        var (kit, session, player, mob, rec) = await RigAsync(seed: 1);
        player.TestCombatFuncs = [new SkillStatFunc(CombatStat.PAtkSpd, '=', [PlayerAttackSpeed])];

        Assert.Null(await kit.World.TrySelectTargetAsync(session, GremlinObjectId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(session, GremlinObjectId, CancellationToken.None));
        long firstPlayerSwing = kit.Now.ToUnixTimeMilliseconds();

        var playerSwings = await CollectSwingsAsync(kit, rec, player.ObjectId, mob, [firstPlayerSwing], wanted: 10);
        for (int i = 1; i < playerSwings.Count; i++)
            Assert.Equal(PlayerAtkMs, playerSwings[i].SwingTick - playerSwings[i - 1].SwingTick);
        foreach (var swing in playerSwings)
        {
            if (swing.HitTick is long hit)
                Assert.Equal(PlayerAtkMs / 2, hit - swing.SwingTick);
        }

        player.Intent.AutoAttackTargetId = 0;
        var mobSwings = await CollectSwingsAsync(kit, rec, mob.ObjectId, player, [], wanted: 10);
        for (int i = 1; i < mobSwings.Count; i++)
            Assert.Equal(GremlinAtkMs, mobSwings[i].SwingTick - mobSwings[i - 1].SwingTick);
        foreach (var swing in mobSwings)
        {
            if (swing.HitTick is long hit)
                Assert.Equal(GremlinAtkMs / 2, hit - swing.SwingTick);
        }
    }
}
