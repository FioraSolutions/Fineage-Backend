using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// "Everyone sees the same": real Net sessions on one loopback zone prove that every player in the actor's AOI gets
/// the exact same visual frames as the actor (Mobius <c>Creature.broadcastPacket</c>: Attack, MagicSkillUse,
/// MagicSkillLaunched, MagicSkillCanceled, Die, despawn, abnormal bar), while the private system lines (your own
/// damage, "hit you for", miss / crit / shield) reach only the attacker or the victim. The clock only moves through
/// <see cref="NetMoveVisibilityRig.AdvanceAsync"/>.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NetObserverParityTests
{
    const int X = 49000, Y = 50000, Z = -3000;
    const int MobId = 9_400_001;
    const int WindStrike = 1177;

    static readonly int[] PrivateCombatLines =
    [
        CombatProtocol.HitTargetForDamage, CombatProtocol.HitYouForDamage, CombatProtocol.AvoidedAttack,
        CombatProtocol.YouMissed, CombatProtocol.CriticalHit, CombatProtocol.ShieldDefenseSucceeded,
        ZoneWorld.SysMsgMagicCritical, ZoneWorld.SysMsgResisted
    ];

    static CancellationToken Ct => CancellationToken.None;

    static SpawnCatalog Mob(int x, bool aggressive) =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["parity"] = new("parity", true,
            [
                new SpawnNpcDefinition(MobId, 90_002, "Gremlin", x, Y, Z,
                    0, true, 600_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: aggressive, AggroRange: aggressive ? 400 : 0,
                    ClanHelpRange: 0, Clans: [])
            ])
        });

    /// <summary>Client order: select the target, then attack it (the auto-attack loop needs the selection).</summary>
    /// <remarks>Returns the actor's frames once the server has handled the request (drain), before the clock moves.</remarks>
    static async Task<List<PacketEnvelope>> AttackAsync(NetMoveVisibilityRig.Player who, int targetObjectId, bool forced = true)
    {
        await who.Net.SendAsync(NetOpcode.SelectTarget,
            NetCodec.EncodeSelectTarget(new SelectTargetNetMsg { TargetObjectId = targetObjectId }), Ct);
        await who.Net.SendAsync(NetOpcode.AttackRequest,
            NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = targetObjectId, Forced = forced }), Ct);
        return await who.DrainAsync();
    }

    static async Task<List<PacketEnvelope>> CastAsync(NetMoveVisibilityRig.Player who, int skillId, int targetObjectId, bool ctrl = false)
    {
        await who.Net.SendAsync(NetOpcode.UseSkill,
            NetCodec.EncodeUseSkill(new UseSkillNetMsg { SkillId = skillId, TargetObjectId = targetObjectId, CtrlPressed = ctrl }), Ct);
        return await who.DrainAsync();
    }

    static List<byte[]> Payloads(IEnumerable<PacketEnvelope> frames, ushort opcode, Func<byte[], bool> about) =>
        frames.Where(f => f.Opcode == opcode && about(f.Payload)).Select(f => f.Payload).ToList();

    static Func<byte[], bool> AttackBy(int objectId) =>
        p => NetCodec.DecodeAttackState(p).AttackerValue == (ulong)(uint)objectId;

    static Func<byte[], bool> CastBy(int objectId) => p => NetCodec.DecodeMagicSkillUse(p).CasterObjectId == objectId;

    static Func<byte[], bool> LaunchBy(int objectId) => p => NetCodec.DecodeMagicSkillLaunched(p).CasterObjectId == objectId;

    static List<SystemMessageNetMsg> Lines(IEnumerable<PacketEnvelope> frames) =>
        frames.Where(f => f.Opcode == NetOpcode.SystemMessage).Select(f => NetCodec.DecodeSystemMessage(f.Payload)).ToList();

    static void AssertSameStream(List<byte[]> expected, List<byte[]> actual, string who)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
            Assert.True(expected[i].AsSpan().SequenceEqual(actual[i]), $"{who}: frame {i} differs from the actor's");
    }

    [Fact]
    public async Task Player_melee_swings_reach_victim_and_watcher_byte_for_byte_and_damage_lines_stay_private()
    {
        var rig = await NetMoveVisibilityRig.StartAsync(Ct);
        await using var _ = rig;
        var a = await rig.JoinAsync(X, Y, Z);
        var b = await rig.JoinAsync(X + 30, Y, Z);
        var w = await rig.JoinAsync(X - 1500, Y, Z);
        var far = await rig.JoinAsync(X + 6000, Y, Z);
        foreach (var p in new[] { a, b, w, far })
            await p.DrainAsync();
        b.Entity.MaxHp = b.Entity.CurHp = 1_000_000;
        // A player's hit on a player takes CP first, then HP (Mobius PlayerStatus.reduceHp, #979): the "hit you for"
        // lines add up to the CP and HP the victim lost together.
        int cpBefore = b.Entity.CurCp;
        Assert.True(cpBefore > 0, "the victim starts with CP, so the CP-first split is exercised");

        var fa = await AttackAsync(a, b.ObjectId, forced: true);
        await rig.AdvanceAsync(8000);

        fa.AddRange(await a.DrainAsync());
        var fb = await b.DrainAsync();
        var fw = await w.DrainAsync();
        var ff = await far.DrainAsync();

        var swings = Payloads(fa, NetOpcode.AttackState, AttackBy(a.ObjectId));
        Assert.True(swings.Count >= 3, $"expected several swings, got {swings.Count}");
        AssertSameStream(swings, Payloads(fb, NetOpcode.AttackState, AttackBy(a.ObjectId)), "victim");
        AssertSameStream(swings, Payloads(fw, NetOpcode.AttackState, AttackBy(a.ObjectId)), "watcher");
        Assert.Empty(Payloads(ff, NetOpcode.AttackState, AttackBy(a.ObjectId)));

        // Every hit of every swing (fist / dual swings carry a second hit in the optional tail).
        var hits = swings.Select(p => NetCodec.DecodeAttackState(p))
            .SelectMany(s => new[] { (s.Damage, s.Flags) }.Concat(s.ExtraHits.Select(h => (h.Damage, h.Flags))))
            .ToList();
        int perSwing = hits.Count / swings.Count;
        int misses = hits.Count(h => (h.Flags & ZoneCombat.HitFlagMiss) != 0);
        int landed = hits.Count(h => (h.Flags & ZoneCombat.HitFlagMiss) == 0 && h.Damage > 0);

        // Attacker (Mobius Player.sendDamageMessage): one "missed" per miss, one "you hit" per landed hit.
        var mine = Lines(fa);
        Assert.Equal(misses, mine.Count(l => l.Id == CombatProtocol.YouMissed));
        Assert.Equal(landed, mine.Count(l => l.Id == CombatProtocol.HitTargetForDamage));
        Assert.Equal(hits.Count(h => (h.Flags & ZoneCombat.HitFlagCrit) != 0 && (h.Flags & ZoneCombat.HitFlagMiss) == 0),
            mine.Count(l => l.Id == CombatProtocol.CriticalHit));
        Assert.DoesNotContain(mine, l => l.Id is CombatProtocol.HitYouForDamage or CombatProtocol.AvoidedAttack);

        // Victim: "hit you for" / "you have avoided" at hitTime (the last swing may still be in flight).
        var victim = Lines(fb);
        int hitYou = victim.Count(l => l.Id == CombatProtocol.HitYouForDamage);
        int avoided = victim.Count(l => l.Id == CombatProtocol.AvoidedAttack);
        Assert.InRange(hitYou + avoided, hits.Count - perSwing, hits.Count);
        Assert.All(victim.Where(l => l.Id == CombatProtocol.HitYouForDamage), l => Assert.Equal(a.Entity.Name, l.Params[0]));
        Assert.DoesNotContain(victim, l => l.Id is CombatProtocol.HitTargetForDamage or CombatProtocol.YouMissed);
        int dealt = (1_000_000 - b.Entity.CurHp) + (cpBefore - b.Entity.CurCp);
        Assert.Equal(0, b.Entity.CurCp);
        Assert.Equal(dealt, victim.Where(l => l.Id == CombatProtocol.HitYouForDamage).Sum(l => int.Parse(l.Params[1])));

        // Watchers read the numbers from the Attack packet only.
        Assert.DoesNotContain(Lines(fw), l => PrivateCombatLines.Contains(l.Id));
        Assert.DoesNotContain(Lines(ff), l => PrivateCombatLines.Contains(l.Id));
    }

    [Fact]
    public async Task Mob_swings_are_identical_for_its_target_and_watchers_and_its_death_and_corpse_removal_too()
    {
        var rig = await NetMoveVisibilityRig.StartAsync(Ct, Mob(X + 40, aggressive: true), "parity");
        await using var _ = rig;
        var a = await rig.JoinAsync(X, Y, Z);
        var w = await rig.JoinAsync(X - 1500, Y, Z);
        a.Entity.MaxHp = a.Entity.CurHp = 1_000_000;
        var mob = rig.EntityOf(MobId);
        mob.GlobalAggro = 0; // skip the spawn grace (Mobius _globalAggro = -10)
        await a.DrainAsync();
        await w.DrainAsync();

        await rig.AdvanceAsync(8000);
        var fa = await a.DrainAsync();
        var fw = await w.DrainAsync();
        var mobSwings = Payloads(fa, NetOpcode.AttackState, AttackBy(MobId));
        Assert.True(mobSwings.Count >= 2, $"aggressive mob should swing, got {mobSwings.Count}");
        AssertSameStream(mobSwings, Payloads(fw, NetOpcode.AttackState, AttackBy(MobId)), "watcher");
        var mine = Lines(fa);
        Assert.InRange(mine.Count(l => l.Id is CombatProtocol.HitYouForDamage or CombatProtocol.AvoidedAttack),
            mobSwings.Count - 1, mobSwings.Count);
        Assert.Contains(mine, l => l.Id is CombatProtocol.HitYouForDamage or CombatProtocol.AvoidedAttack);
        Assert.DoesNotContain(Lines(fw), l => PrivateCombatLines.Contains(l.Id));

        // Death: the Die frame is the same for killer and watcher; the corpse leaves both screens together.
        mob.CurHp = 1;
        var killFrames = await AttackAsync(a, MobId);
        for (int i = 0; i < 40 && mob.CurHp > 0; i++)
            await rig.AdvanceAsync(400);
        Assert.Equal(0, mob.CurHp);
        await rig.AdvanceAsync(500);
        fa = killFrames.Concat(await a.DrainAsync()).ToList();
        fw = await w.DrainAsync();
        Func<byte[], bool> mobDie = p => NetCodec.DecodeEntityDie(p).EntityValue == (ulong)(uint)MobId;
        var die = Payloads(fa, NetOpcode.EntityDie, mobDie);
        Assert.Single(die);
        AssertSameStream(die, Payloads(fw, NetOpcode.EntityDie, mobDie), "watcher");

        await rig.AdvanceAsync(15_000, 500);
        Func<byte[], bool> mobGone = p => NetCodec.DecodeEntityDespawn(p).EntityValue == (ulong)(uint)MobId;
        var gone = Payloads(await a.DrainAsync(), NetOpcode.EntityDespawn, mobGone);
        Assert.Single(gone);
        AssertSameStream(gone, Payloads(await w.DrainAsync(), NetOpcode.EntityDespawn, mobGone), "watcher");
    }

    static async Task<(NetMoveVisibilityRig Rig, NetMoveVisibilityRig.Player Caster, NetMoveVisibilityRig.Player SeesCaster,
        NetMoveVisibilityRig.Player SeesOnlyTarget)> CastSceneAsync()
    {
        // Mob 500 to the east. One watcher 1600 west sees the caster but not the mob; another 1800 east of the mob
        // sees the mob but not the caster: Mobius broadcasts from the caster's knownlist only.
        var rig = await NetMoveVisibilityRig.StartAsync(Ct, Mob(X + 500, aggressive: false), "parity");
        var a = await rig.JoinAsync(X, Y, Z);
        var west = await rig.JoinAsync(X - 1600, Y, Z);
        var east = await rig.JoinAsync(X + 2300, Y, Z);
        Assert.DoesNotContain(MobId, west.Entity.Knownlist);
        Assert.DoesNotContain(a.ObjectId, east.Entity.Knownlist);
        Assert.Contains(MobId, east.Entity.Knownlist);
        a.Entity.KnownSkills[WindStrike] = 1;
        a.Entity.MaxMp = a.Entity.CurMp = 10_000;
        foreach (var p in new[] { a, west, east })
            await p.DrainAsync();
        return (rig, a, west, east);
    }

    [Fact]
    public async Task A_cast_start_and_launch_are_identical_for_everyone_who_sees_the_caster()
    {
        var (rig, a, west, east) = await CastSceneAsync();
        await using var _ = rig;

        var fa = await CastAsync(a, WindStrike, MobId);
        int hitTime = NetCodec.DecodeMagicSkillUse(Assert.Single(Payloads(fa, NetOpcode.MagicSkillUse, CastBy(a.ObjectId)))).HitTimeMs;
        await rig.AdvanceAsync(hitTime + 1000);
        fa.AddRange(await a.DrainAsync());
        var fw = await west.DrainAsync();
        var fe = await east.DrainAsync();

        var use = Payloads(fa, NetOpcode.MagicSkillUse, CastBy(a.ObjectId));
        var cast = NetCodec.DecodeMagicSkillUse(Assert.Single(use));
        Assert.Equal(WindStrike, cast.SkillId);
        Assert.Equal(MobId, cast.TargetObjectId);
        Assert.True(cast.HitTimeMs > 0);
        AssertSameStream(use, Payloads(fw, NetOpcode.MagicSkillUse, CastBy(a.ObjectId)), "caster watcher");
        Assert.Empty(Payloads(fe, NetOpcode.MagicSkillUse, CastBy(a.ObjectId)));

        var launch = Payloads(fa, NetOpcode.MagicSkillLaunched, LaunchBy(a.ObjectId));
        var launched = NetCodec.DecodeMagicSkillLaunched(Assert.Single(launch));
        Assert.Equal(WindStrike, launched.SkillId);
        Assert.Equal(1, launched.SkillLevel);
        Assert.Equal([MobId], launched.TargetObjectIds);
        AssertSameStream(launch, Payloads(fw, NetOpcode.MagicSkillLaunched, LaunchBy(a.ObjectId)), "caster watcher");
        Assert.Empty(Payloads(fe, NetOpcode.MagicSkillLaunched, LaunchBy(a.ObjectId)));

        // Damage number: the caster's own line; the watcher only sees the mob's HP bar move.
        var mob = rig.EntityOf(MobId);
        if (mob.CurHp < mob.MaxHp)
            Assert.Contains(Lines(fa), l => l.Id == CombatProtocol.HitTargetForDamage && l.Params[0] == mob.Name);
        Assert.DoesNotContain(Lines(fw), l => PrivateCombatLines.Contains(l.Id));
    }

    [Fact]
    public async Task An_aborted_cast_is_cancelled_on_every_screen_that_saw_it_start()
    {
        var (rig, a, west, east) = await CastSceneAsync();
        await using var _ = rig;

        var fa = await CastAsync(a, WindStrike, MobId);
        Assert.Single(Payloads(fa, NetOpcode.MagicSkillUse, CastBy(a.ObjectId)));
        Assert.True(rig.World.AbortCastForTest(a.Entity));
        await rig.AdvanceAsync(8000);

        fa.AddRange(await a.DrainAsync());
        var fw = await west.DrainAsync();
        var fe = await east.DrainAsync();
        Func<byte[], bool> cancelOfA = p => NetCodec.DecodeMagicSkillCanceled(p).ObjectId == a.ObjectId;
        var cancel = Payloads(fa, NetOpcode.MagicSkillCanceled, cancelOfA);
        Assert.Single(cancel);
        AssertSameStream(cancel, Payloads(fw, NetOpcode.MagicSkillCanceled, cancelOfA), "caster watcher");
        Assert.Empty(Payloads(fe, NetOpcode.MagicSkillCanceled, cancelOfA));
        Assert.Empty(Payloads(fa, NetOpcode.MagicSkillLaunched, LaunchBy(a.ObjectId)));
        Assert.Empty(Payloads(fw, NetOpcode.MagicSkillLaunched, LaunchBy(a.ObjectId)));
    }

    [Fact]
    public async Task A_mob_skill_reaches_everyone_who_sees_the_mob_identically()
    {
        var (rig, a, west, east) = await CastSceneAsync();
        await using var _ = rig;
        var mob = rig.EntityOf(MobId);
        a.Entity.MaxHp = a.Entity.CurHp = 1_000_000;
        var debuff = new NpcAiSupportSkill(4038, 2, HitTimeMs: 1000, ReuseDelayMs: 8000, Mp: 20, MpInitial: 0,
            AbnormalTimeSeconds: 30, AbnormalLevel: 2);
        mob.AiDebuffSkills = [debuff];
        mob.CurMp = mob.MaxMp;

        Assert.True(rig.World.CastNpcSupportForTest(mob, a.Entity, debuff, "debuff"));
        await rig.AdvanceAsync(1600);
        var fa = await a.DrainAsync();
        var fw = await west.DrainAsync();
        var fe = await east.DrainAsync();

        // Target and a player who sees only the mob get the same cast and launch; a player who sees only the
        // target does not (Mobius broadcasts from the caster's knownlist).
        var use = Payloads(fa, NetOpcode.MagicSkillUse, CastBy(MobId));
        Assert.Equal(4038, NetCodec.DecodeMagicSkillUse(Assert.Single(use)).SkillId);
        AssertSameStream(use, Payloads(fe, NetOpcode.MagicSkillUse, CastBy(MobId)), "mob watcher");
        Assert.Empty(Payloads(fw, NetOpcode.MagicSkillUse, CastBy(MobId)));

        var launch = Payloads(fa, NetOpcode.MagicSkillLaunched, LaunchBy(MobId));
        Assert.Equal([a.ObjectId], NetCodec.DecodeMagicSkillLaunched(Assert.Single(launch)).TargetObjectIds);
        AssertSameStream(launch, Payloads(fe, NetOpcode.MagicSkillLaunched, LaunchBy(MobId)), "mob watcher");
        Assert.Empty(Payloads(fw, NetOpcode.MagicSkillLaunched, LaunchBy(MobId)));
        Assert.Contains(a.Entity.Abnormals, ab => ab.Id == 4038);
    }

    [Fact]
    public async Task A_player_walking_into_view_gets_the_buffs_already_on_a_pawn()
    {
        var rig = await NetMoveVisibilityRig.StartAsync(Ct);
        await using var _ = rig;
        var a = await rig.JoinAsync(X, Y, Z);
        ZoneAbnormals.Apply(a.Entity.Abnormals, 1068, 1, 1200, rig.NowMs, skillLevel: 1);

        var late = await rig.JoinAsync(X + 200, Y, Z);
        var frames = late.Net.EnterFrames.Concat(await late.DrainAsync()).ToList();
        var bars = frames.Where(f => f.Opcode == NetOpcode.AbnormalStatusUpdate)
            .Select(f => NetCodec.DecodeAbnormalStatusUpdate(f.Payload))
            .Where(u => u.ObjectId == a.ObjectId)
            .ToList();
        var bar = Assert.Single(bars);
        var effect = Assert.Single(bar.Effects);
        Assert.Equal(1068, effect.Id);
        Assert.InRange(effect.DurationSeconds, 1199, 1200);

        // A pawn with an empty bar sends nothing extra.
        Assert.DoesNotContain(frames, f => f.Opcode == NetOpcode.AbnormalStatusUpdate &&
                                            NetCodec.DecodeAbnormalStatusUpdate(f.Payload).ObjectId == late.ObjectId);
    }
}
