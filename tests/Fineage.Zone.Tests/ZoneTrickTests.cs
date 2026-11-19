using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Trick (11) — dagger A1 utility. Mobius CT_0: <c>DeleteHateOfMe</c> (chance 50, attackable only: stop hating the
/// caster, walk, AI ACTIVE) then <c>TargetCancel</c> (chance 70, skipped on <c>isRaid()</c>: target null, abort
/// attack/cast, AI IDLE). castRange 400, hitTime 1200, reuse 20 s, needs a dagger. No movement of its own.
/// </summary>
public sealed class ZoneTrickTests
{
    const int Trick = 11;
    const int Dagger = 10;
    const int MobId = 9_400_711;
    const int RaidId = 9_400_712;

    /// <summary>Deterministic roll: every Mobius <c>chance</c> check succeeds.</summary>
    sealed class AlwaysRoll : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0d;
    }

    static (NetTestKit Kit, NetBridgeRecorder Rec) Kit()
    {
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["trick"] = new("trick", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 711, "Gremlin",
                    NetTestKit.ElfX + 100, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 40, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: []),
                new SpawnNpcDefinition(RaidId, 90000 + 712, "Raid Gremlin",
                    NetTestKit.ElfX + 120, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "RaidBoss",
                    Level: 40, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [])
            ])
        }), "trick", geo: GeoEngine.Empty, combatRandom: new AlwaysRoll());
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static void GiveTrick(ZoneEntity player, bool dagger = true)
    {
        player.KnownSkills[Trick] = 1;
        player.CurMp = player.MaxMp = 500;
        var bag = player.Inventory!;
        if (dagger && bag.GetEquipped(PaperdollSlot.RHand)?.Template.WeaponType != WeaponType.Dagger)
            bag.Equip(bag.Add(Dagger));
    }

    static async Task<(NetTestKit Kit, NetBridgeRecorder Rec, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob)> EnterAsync()
    {
        var (kit, rec) = Kit();
        var (session, player) = await kit.EnterAsync(classId: 7, race: 0, level: 60); // Rogue
        GiveTrick(player);
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.MaxHp = mob.CurHp = 50_000;
        return (kit, rec, session, player, mob);
    }

    [Fact]
    public void Trick_data_matches_mobius_ct0()
    {
        var skill = GameData.LoadDefault().Skills.Find(Trick);
        Assert.NotNull(skill);
        Assert.Equal(400, skill!.CastRange);
        Assert.Equal(1200, skill.HitTimeMs);
        Assert.Equal(20_000, skill.ReuseDelayMs);
        Assert.Contains("DAGGER", skill.UsingKinds);

        Assert.True(SkillEffectCatalog.LoadDefault().TryGet(Trick, out var rows));
        Assert.Equal(["DeleteHateOfMe", "TargetCancel"], rows.Select(r => r.Name).ToArray());
        Assert.Equal(50, rows[0].Chance);
        Assert.Equal(70, rows[1].Chance);
    }

    [Fact]
    public async Task Trick_without_a_dagger_is_refused_with_no_reuse()
    {
        var (kit, rec) = Kit();
        var (session, player) = await kit.EnterAsync(classId: 7, race: 0, level: 60);
        GiveTrick(player, dagger: false);
        var bag = player.Inventory!;
        bag.Equip(bag.Add(1)); // Short Sword

        Assert.Null(await kit.World.TrySkillUseAsync(session, Trick, MobId, CancellationToken.None));
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            m => ((Fineage.Protocol.SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgUnsuitableTerms);
        Assert.False(player.SkillReuseUntilMs.ContainsKey(Trick));
    }

    [Fact]
    public async Task Trick_on_a_mob_that_hates_only_the_caster_stops_the_chase_and_goes_idle()
    {
        var (kit, _, session, player, mob) = await EnterAsync();
        mob.HateList[player.ObjectId] = 5_000;
        mob.AggroTargetId = player.ObjectId;
        mob.Chasing = true;
        mob.Running = true;
        int px = player.X, py = player.Y;

        Assert.Null(await kit.World.TrySkillUseAsync(session, Trick, MobId, CancellationToken.None));
        // Mobius 20 s reuse, scaled by attack speed like every physical skill — well above the old 5 s row.
        Assert.True(player.SkillReuseUntilMs[Trick] - kit.Now.ToUnixTimeMilliseconds() > 10_000);
        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        Assert.DoesNotContain(player.ObjectId, mob.HateList.Keys);
        Assert.Equal(0, mob.AggroTargetId);
        Assert.False(mob.Chasing);
        Assert.False(mob.Running);
        // Trick has no movement of its own: the caster casts in place.
        Assert.Equal(px, player.X);
        Assert.Equal(py, player.Y);
    }

    [Fact]
    public async Task Trick_with_another_hater_switches_the_mob_to_it()
    {
        var (kit, _, session, player, mob) = await EnterAsync();
        var (_, other) = await kit.EnterAsync(classId: 0, race: 0, level: 60, x: NetTestKit.ElfX + 150);
        mob.HateList[player.ObjectId] = 9_000;
        mob.HateList[other.ObjectId] = 1_000;
        mob.AggroTargetId = player.ObjectId;

        Assert.Null(await kit.World.TrySkillUseAsync(session, Trick, MobId, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        Assert.DoesNotContain(player.ObjectId, mob.HateList.Keys);
        Assert.Equal(other.ObjectId, mob.AggroTargetId);
    }

    [Fact]
    public async Task Trick_on_a_player_clears_the_victim_target()
    {
        var (kit, rec, session, player, _) = await EnterAsync();
        var (victimSession, victim) = await kit.EnterAsync(classId: 0, race: 0, level: 60, x: NetTestKit.ElfX + 80);
        victimSession.SelectedTargetId = player.ObjectId;
        victimSession.AutoAttackTargetId = player.ObjectId;

        Assert.Null(await kit.World.TrySkillUseAsync(session, Trick, victim.ObjectId, CancellationToken.None, ctrlPressed: true));
        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        Assert.Equal(0, victimSession.SelectedTargetId);
        Assert.Equal(0, victimSession.AutoAttackTargetId);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutTargetUnselected)),
            a => a[0] is IReadOnlyList<int> watchers && watchers.Contains(victim.ObjectId) && (int)a[1]! == player.ObjectId);
    }

    [Fact]
    public async Task Trick_target_cancel_does_not_touch_a_raid_boss()
    {
        var (kit, _, session, player, _) = await EnterAsync();
        var (_, other) = await kit.EnterAsync(classId: 0, race: 0, level: 60, x: NetTestKit.ElfX + 150);
        Assert.True(kit.World.TryGetEntityById(RaidId, out var raid));
        raid.MaxHp = raid.CurHp = 90_000;
        // The raid fights someone else; Trick's TargetCancel must not drop that target (Mobius isRaid()).
        raid.HateList[other.ObjectId] = 5_000;
        raid.AggroTargetId = other.ObjectId;

        Assert.Null(await kit.World.TrySkillUseAsync(session, Trick, RaidId, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(2);
        kit.World.ResolvePendingHitForTest(player);

        Assert.Equal(other.ObjectId, raid.AggroTargetId);
        Assert.Contains(other.ObjectId, raid.HateList.Keys);
    }
}
