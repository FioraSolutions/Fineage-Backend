using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Generic Mobius-named instant effect handlers (GetAgro / TargetMe, HpDrain, Life Scavenge, CpDamPercent,
/// Lethal, RunAway, PolearmSingleTarget). Classification is by effect name, never by skill id.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ZoneEffectHandlerTests
{
    const int Aggression = 28;
    const int AuraOfHate = 18;
    const int LifeScavenge = 46;
    const int DrainHealth = 70;
    const int FocusAttack = 317;
    const int FellSwoop = 421;
    const int Wrath = 320;
    const int BanishUndead = 405;
    const int BanishSeraph = 450;
    const int ShortSpear = 15;
    const int SoulOre = 1785;
    const int MobId = 9_401_801;
    const int MateId = 9_401_802;

    /// <summary>Every Mobius chance / lethal roll succeeds (Next(100)=0, Next(1e6)=0 → full lethal).</summary>
    sealed class AlwaysRoll : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0d;
    }

    static (NetTestKit Kit, NetBridgeRecorder Rec) Kit(bool twoMobs = true, int clanHelp = 400, string[]? clans = null)
    {
        clans ??= ["ogre_clan"];
        var npcs = new List<SpawnNpcDefinition>
        {
            new(MobId, 90000 + 1801, "Gremlin",
                NetTestKit.ElfX + 40, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                Level: 20, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0,
                ClanHelpRange: clanHelp, Clans: clans)
        };
        if (twoMobs)
        {
            npcs.Add(new(MateId, 90000 + 1802, "Gremlin Mate",
                NetTestKit.ElfX + 60, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                Level: 20, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0,
                ClanHelpRange: clanHelp, Clans: clans));
        }
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["effects"] = new("effects", true, npcs)
        }), "effects", geo: GeoEngine.Empty, combatRandom: new AlwaysRoll());
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob, ZoneEntity? Mate)> EnterAsync(
        int classId = 3, int level = 70, bool twoMobs = true)
    {
        var (kit, _) = Kit(twoMobs);
        var (session, player) = await kit.EnterAsync(classId: classId, race: 0, level: level);
        player.CurMp = player.MaxMp = 2000;
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.MaxHp = mob.CurHp = 50_000;
        ZoneEntity? mate = null;
        if (twoMobs)
        {
            Assert.True(kit.World.TryGetEntityById(MateId, out mate));
            mate.MaxHp = mate.CurHp = 50_000;
        }
        return (kit, session, player, mob, mate);
    }

    [Fact]
    public void Catalog_rows_use_Mobius_effect_names_and_tables()
    {
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(Aggression, out var aggression));
        Assert.Contains(aggression, e => e.Name == "GetAgro" && e.Chance == 60);
        Assert.Contains(aggression, e => e.Name == "TargetMe" && e.Chance == 60);

        Assert.True(effects.TryGet(LifeScavenge, out var scavenge));
        var drain = Assert.Single(scavenge, e => e.Name == "HpByLevel");
        Assert.Equal(105, drain.PowerAt(1));
        Assert.Equal(243, drain.PowerAt(15));
        Assert.Contains(scavenge, e => e.Name == "ConsumeBody");

        Assert.True(effects.TryGet(DrainHealth, out var drainHp));
        Assert.Equal(0.2, Assert.Single(drainHp, e => e.Name == "HpDrain").Power);

        Assert.True(effects.TryGet(FocusAttack, out var focus));
        Assert.Contains(focus, e => e.Name == "PolearmSingleTarget");

        Assert.True(effects.TryGet(Wrath, out var wrath));
        var cp = Assert.Single(wrath, e => e.Name == "CpDamPercent");
        Assert.Equal(7, cp.PowerAt(1));
        Assert.Equal(30, cp.PowerAt(10));

        Assert.True(effects.TryGet(BanishUndead, out var undead));
        Assert.Contains(undead, e => e.Name == "RunAway" && (int)e.Power == 30 && e.ReadExtraInt("time") == 20);
        Assert.Contains(undead, e => e.Name == "Lethal" && e.ReadExtraInt("fullLethal") == 25);

        Assert.True(effects.TryGet(BanishSeraph, out var seraph));
        Assert.Contains(seraph, e => e.Name == "Lethal" && e.ReadExtraInt("halfLethal") == 50 && e.ReadExtraInt("fullLethal") == 0);
    }

    [Fact]
    public async Task Aggression_sets_hate_and_clan_assist_of_200()
    {
        var (kit, session, player, mob, mate) = await EnterAsync();
        player.KnownSkills[Aggression] = 1;

        Assert.Null(await kit.World.TrySkillUseAsync(session, Aggression, MobId, CancellationToken.None));
        kit.World.ResolvePendingHitForTest(player);

        Assert.True(mob.HateList[player.ObjectId] >= 1);
        Assert.Equal(player.ObjectId, mob.AggroTargetId);
        Assert.NotNull(mate);
        Assert.Equal(200, mate!.HateList[player.ObjectId]);
        Assert.True(mate.Running);
    }

    [Fact]
    public async Task Aura_of_hate_does_not_require_a_primary_target()
    {
        var (kit, session, player, mob, mate) = await EnterAsync();
        player.KnownSkills[AuraOfHate] = 1;
        session.SelectedTargetId = 0;

        Assert.Null(await kit.World.TrySkillUseAsync(session, AuraOfHate, targetObjectId: 0, CancellationToken.None));
        kit.World.ResolvePendingHitForTest(player);

        Assert.True(mob.HateList.ContainsKey(player.ObjectId), "aura must tag nearby attackables around the caster");
        Assert.NotNull(mate);
        Assert.True(mate!.HateList.ContainsKey(player.ObjectId));
    }

    [Fact]
    public async Task Life_scavenge_heals_the_caster_from_the_drain_table_and_consumes_the_corpse()
    {
        var (kit, session, player, mob, _) = await EnterAsync(classId: 8, level: 40, twoMobs: false);
        player.KnownSkills[LifeScavenge] = 1;
        kit.World.RefreshPlayerMaxima(player, fanout: false);
        player.CurHp = 1;
        int max = player.MaxHp;
        mob.CurHp = 0;
        mob.Dead = true;

        Assert.Null(await kit.World.TrySkillUseAsync(session, LifeScavenge, MobId, CancellationToken.None));
        kit.World.ResolvePendingHitForTest(player);

        Assert.Equal(Math.Min(max, 1 + 105), player.CurHp);
        Assert.False(kit.World.TryGetEntityById(MobId, out _));
    }

    [Fact]
    public async Task Drain_health_is_a_magic_attack_and_absorbs_20_percent_of_the_hp_damage()
    {
        var (kit, session, player, mob, _) = await EnterAsync(classId: 13, level: 40, twoMobs: false);
        player.KnownSkills[DrainHealth] = 1;
        kit.World.RefreshPlayerMaxima(player, fanout: false);
        player.CurHp = 1;
        int beforeMob = mob.CurHp;

        Assert.Null(await kit.World.TrySkillUseAsync(session, DrainHealth, MobId, CancellationToken.None));
        kit.World.ResolvePendingHitForTest(player);

        int dealt = beforeMob - mob.CurHp;
        Assert.True(dealt > 0, "Drain Health must land as a skill-combat magic hit");
        int expect = Math.Min(player.MaxHp - 1, (int)(0.2 * dealt));
        Assert.Equal(1 + expect, player.CurHp);
    }

    [Fact]
    public async Task Cp_dam_percent_removes_the_catalog_share_of_current_cp()
    {
        var (kit, _, player, _, _) = await EnterAsync(twoMobs: false);
        player.MaxCp = player.CurCp = 1000;
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(Wrath, out var rows));
        var cp = Assert.Single(rows, e => e.Name == "CpDamPercent");
        kit.World.ApplyCpDamPercent(player, player, cp, level: 1);
        Assert.Equal(930, player.CurCp); // 7% of 1000
    }

    [Fact]
    public async Task Lethal_full_leaves_the_monster_at_1_hp()
    {
        var (kit, _, player, mob, _) = await EnterAsync(twoMobs: false);
        var template = kit.Game.Skills.Find(BanishUndead);
        Assert.NotNull(template);
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(BanishUndead, out var rows));
        var lethal = Assert.Single(rows, e => e.Name == "Lethal");
        kit.World.ApplyLethalEffect(player, mob, lethal, template, level: 1);
        Assert.Equal(1, mob.CurHp);
    }

    [Fact]
    public async Task Run_away_fears_an_attackable_and_starts_the_flee_step()
    {
        var (kit, _, player, mob, _) = await EnterAsync(twoMobs: false);
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(BanishUndead, out var rows));
        var run = Assert.Single(rows, e => e.Name == "RunAway");
        kit.World.ApplyRunAway(player, mob, run, BanishUndead, kit.Now.ToUnixTimeMilliseconds());
        Assert.Contains(mob.Abnormals, a => a.Slot == "TURN_FLEE");
        Assert.True(mob.Running);
        Assert.True(mob.FearFleeAtUnixMs > kit.Now.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Atk_count_max_starts_at_one_and_fell_swoop_adds_extras()
    {
        var (kit, _, player, _, _) = await EnterAsync(twoMobs: false);
        Assert.Equal(1, kit.World.PlayerStats(player).AtkCountMax);

        var template = kit.Game.Skills.Find(FellSwoop);
        Assert.NotNull(template);
        Assert.True(template!.TryLevelData(1, out var lv));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, lv, player, force: true));
        // Mobius add 3 on a base of 1 → 4.
        Assert.Equal(4, kit.World.PlayerStats(player).AtkCountMax);
    }

    [Fact]
    public async Task Focus_attack_sets_the_polearm_single_target_flag()
    {
        var (kit, session, player, mob, mate) = await EnterAsync();
        Assert.False(kit.World.HasPolearmSingleTarget(player));

        var template = kit.Game.Skills.Find(FocusAttack);
        Assert.NotNull(template);
        Assert.True(template!.TryLevelData(1, out var lv));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(template, lv, player, force: true));
        Assert.True(kit.World.HasPolearmSingleTarget(player));

        var bag = player.Inventory!;
        if (bag.GetEquipped(PaperdollSlot.RHand)?.Template.WeaponType != WeaponType.Pole)
            bag.Equip(bag.Add(ShortSpear));
        // Even with Fell Swoop's extra hits, Focus Attack must not splash onto the mate.
        var fell = kit.Game.Skills.Find(FellSwoop);
        Assert.NotNull(fell);
        Assert.True(fell!.TryLevelData(1, out var fellLv));
        Assert.True(kit.World.ApplyAbnormalFromSkillForTest(fell, fellLv, player, force: true));
        Assert.Equal(4, kit.World.PlayerStats(player).AtkCountMax);

        int mateHp = mate!.CurHp;
        session.SelectedTargetId = MobId;
        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));
        kit.Now = kit.Now.AddSeconds(2);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.Equal(mateHp, mate.CurHp);
    }
}
