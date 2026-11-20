using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Lure (51) — Treasure Hunter / Adventurer A1 utility. Mobius CT_0: targetType ONE, castRange 400,
/// effectRange 900, hitTime 1500, reuse 10 s, effect <c>AddHate</c> power 500. The skill is not a
/// damage nuke: without routing it through the hostile-utility path it used to fall into
/// <c>TrySkillUseAttack</c> and be refused as <c>skill_no_combat</c>. <c>Creature.callSkill</c> skips
/// <c>addAttackerToAttackByList</c> / <c>EVT_ATTACKED</c> for Lure, so the only hate write is the
/// AddHate payload (quietly pulls that one mob, no clan-help notify from the cast itself).
/// </summary>
public sealed class ZoneLureTests
{
    const int Lure = 51;
    const int MobId = 9_400_751;

    static (NetTestKit Kit, NetBridgeRecorder Rec) Kit(int mobXOffset = 100)
    {
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["lure"] = new("lure", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 751, "Gremlin",
                    NetTestKit.ElfX + mobXOffset, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 40, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        }), "lure", geo: GeoEngine.Empty);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static void GiveLure(ZoneEntity player)
    {
        player.KnownSkills[Lure] = 1;
        player.CurMp = player.MaxMp = 500;
    }

    static async Task<(NetTestKit Kit, NetBridgeRecorder Rec, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob)> EnterAsync(int mobXOffset = 100)
    {
        var (kit, rec) = Kit(mobXOffset);
        var (session, player) = await kit.EnterAsync(classId: 8, race: 0, level: 60); // Treasure Hunter
        GiveLure(player);
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.MaxHp = mob.CurHp = 50_000;
        return (kit, rec, session, player, mob);
    }

    [Fact]
    public async Task Lure_on_a_standing_mob_adds_hate_and_sets_the_caster_as_target()
    {
        var (kit, rec, session, player, mob) = await EnterAsync();
        Assert.Empty(mob.HateList);
        Assert.Equal(0, mob.AggroTargetId);

        Assert.Null(await kit.World.TrySkillUseAsync(session, Lure, MobId, CancellationToken.None));
        Assert.True(player.SkillReuseUntilMs[Lure] - kit.Now.ToUnixTimeMilliseconds() > 5_000);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)),
            a => ((Fineage.Protocol.MagicSkillUseMsg)a[1]!).SkillId == Lure);

        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        Assert.Equal(500, mob.HateList[player.ObjectId]);
        Assert.Equal(player.ObjectId, mob.AggroTargetId);
        // Attack intention itself switches the NPC to running (Mobius setIntention(ATTACK) → setRunning).
        Assert.True(mob.Running);

        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.True(mob.Running);
        Assert.True(mob.MoveActive, "lured mob out of melee should chase");
    }

    [Fact]
    public async Task Lure_is_not_refused_as_a_missing_combat_nuke()
    {
        var (kit, _, session, _, _) = await EnterAsync();
        var error = await kit.World.TrySkillUseAsync(session, Lure, MobId, CancellationToken.None);
        Assert.Null(error);
        Assert.NotEqual(Fineage.Protocol.SkillProtocol.NoCombat, error);
    }

    [Fact]
    public async Task Lure_from_inside_cast_range_does_not_walk_in()
    {
        var (kit, _, session, player, _) = await EnterAsync(mobXOffset: 200);
        int x = player.X, y = player.Y;
        Assert.Null(await kit.World.TrySkillUseAsync(session, Lure, MobId, CancellationToken.None));
        Assert.Equal(x, player.X);
        Assert.Equal(y, player.Y);
        Assert.False(player.MoveActive);
        Assert.Equal(0, session.PendingCastSkillId);
    }

    [Fact]
    public async Task Mortal_blow_does_not_start_from_cast_range_plus_100()
    {
        // Skill 16 (Mortal Blow) is a 40-range dagger nuke. The old gate used castRange+100, so a
        // pawn 140 UU away started the cast immediately. Mobius walks in at 40 + collisions (~80).
        var (kit, rec) = Kit(mobXOffset: 140);
        var (session, player) = await kit.EnterAsync(classId: 8, race: 0, level: 60);
        player.KnownSkills[16] = 1;
        player.CurMp = player.MaxMp = 500;
        var bag = player.Inventory!;
        if (bag.GetEquipped(Fineage.Game.PaperdollSlot.RHand)?.Template.WeaponType != WeaponType.Dagger)
            bag.Equip(bag.Add(10));

        Assert.Null(await kit.World.TrySkillUseAsync(session, 16, MobId, CancellationToken.None));
        Assert.True(player.MoveActive || session.PendingCastSkillId == 16);
        Assert.DoesNotContain(rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillUse)),
            a => ((Fineage.Protocol.MagicSkillUseMsg)a[1]!).SkillId == 16);
    }
}
