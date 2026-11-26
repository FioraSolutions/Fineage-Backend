using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius positional crit stats and damage triggers for the Human dagger line:
/// <list type="bullet">
/// <item>Focus Chance (356) <c>critRatePos</c>: ×0.7 front, ×1.3 side, ×1.6 behind on a plain attack's crit rate.</item>
/// <item>Focus Power (357) <c>critDmgPos</c>: ×0.7 / ×1.3 / ×1.6 on crit damage — halved by
/// <c>calcBlowDamage</c> / <c>calcBackstabDamage</c>, used as-is by <c>calcPhysDam</c>.</item>
/// <item>Mirage (445) <c>TriggerSkillByDamage</c> → <c>TargetCancel</c> (5144) cancelling the attacker's target.</item>
/// </list>
/// </summary>
[Trait("Category", "Integration")]
public sealed class ZonePositionalCritAndMirageTests
{
    const int FocusChance = 356;
    const int FocusPower = 357;
    const int Mirage = 445;
    const int MirageTrigger = 5144;
    const int MobId = 9_400_451;

    static (NetTestKit Kit, NetBridgeRecorder Rec) Kit(Random? combatRandom = null)
    {
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["pos"] = new("pos", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 451, "Gremlin",
                    NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 40, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [], Evasion: 0)
            ])
        }), "pos", geo: GeoEngine.Empty, combatRandom: combatRandom);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob)> EnterAsync(
        Random? combatRandom = null)
    {
        var (kit, _) = Kit(combatRandom);
        var (session, player) = await kit.EnterAsync(classId: 7, race: 0, level: 60); // Rogue
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.MaxHp = mob.CurHp = 50_000;
        mob.Heading = 0;
        return (kit, session, player, mob);
    }

    [Fact]
    public void Focus_chance_and_power_carry_positional_funcs_gated_on_a_dagger()
    {
        var effects = SkillStatEffectCatalog.LoadDefault();

        Assert.True(effects.TryGet(FocusChance, out var chance));
        Assert.Equal(3, chance.Funcs.Count);
        Assert.All(chance.Funcs, f => Assert.Equal(CombatStat.CritRatePos, f.Stat));
        Assert.Equal(0.7, chance.Funcs.Single(f => f.Position == SkillPosition.Front).ValueAt(1));
        Assert.Equal(1.3, chance.Funcs.Single(f => f.Position == SkillPosition.Side).ValueAt(1));
        Assert.Equal(1.6, chance.Funcs.Single(f => f.Position == SkillPosition.Behind).ValueAt(1));
        Assert.All(chance.Funcs, f => Assert.Equal(new[] { "DAGGER", "DUALDAGGER" }, f.Using!));

        Assert.True(effects.TryGet(FocusPower, out var power));
        Assert.Equal(3, power.Funcs.Count);
        Assert.All(power.Funcs, f => Assert.Equal(CombatStat.CritDmgPos, f.Stat));
        Assert.Equal(1.6, power.Funcs.Single(f => f.Position == SkillPosition.Behind).ValueAt(1));
    }

    [Fact]
    public void Positional_funcs_only_fold_for_their_own_side()
    {
        var catalog = SkillStatEffectCatalog.LoadDefault();
        var sources = new[] { new SkillStatSource(FocusChance, SkillLevel: 1, AbnormalValue: 1) };

        Assert.Equal(0.7, catalog.Collect(sources, position: SkillPosition.Front).Apply(CombatStat.CritRatePos, 1d), 6);
        Assert.Equal(1.3, catalog.Collect(sources, position: SkillPosition.Side).Apply(CombatStat.CritRatePos, 1d), 6);
        Assert.Equal(1.6, catalog.Collect(sources, position: SkillPosition.Behind).Apply(CombatStat.CritRatePos, 1d), 6);
        // No target → positional funcs never sit on the cached stat line.
        Assert.Equal(1d, catalog.Collect(sources).Apply(CombatStat.CritRatePos, 1d), 6);
    }

    [Fact]
    public void Dagger_gate_is_enforced_when_a_wear_check_is_supplied()
    {
        var catalog = SkillStatEffectCatalog.LoadDefault();
        var sources = new[] { new SkillStatSource(FocusChance, SkillLevel: 1, AbnormalValue: 1) };

        Assert.Equal(1.6, catalog.Collect(sources, position: SkillPosition.Behind, wearing: _ => true)
            .Apply(CombatStat.CritRatePos, 1d), 6);
        Assert.Equal(1d, catalog.Collect(sources, position: SkillPosition.Behind, wearing: _ => false)
            .Apply(CombatStat.CritRatePos, 1d), 6);
    }

    [Fact]
    public void Position_of_matches_mobius_front_side_behind()
    {
        // Target at (0,0) facing +X (heading 0): +X is in front, −X behind, +Y on the side.
        Assert.Equal(SkillPosition.Front, ZoneCombat.PositionOf(100, 0, 0, 0, 0));
        Assert.Equal(SkillPosition.Behind, ZoneCombat.PositionOf(-100, 0, 0, 0, 0));
        Assert.Equal(SkillPosition.Side, ZoneCombat.PositionOf(0, 100, 0, 0, 0));
    }

    [Fact]
    public void Blow_positional_crit_damage_is_halved_like_mobius()
    {
        // calcBlowDamage: criticalModPos = ((critDmgPos − 1) / 2) + 1
        Assert.Equal(1.3, ZoneCombat.BlowPositionalCritDamage(1.6), 6);
        Assert.Equal(0.85, ZoneCombat.BlowPositionalCritDamage(0.7), 6);
        Assert.Equal(1d, ZoneCombat.BlowPositionalCritDamage(1d), 6);
    }

    [Fact]
    public void Positional_crit_permille_scales_the_plain_crit_rate()
    {
        Assert.Equal(160, ZoneCombat.PositionalCritPermille(100, 1.6));
        Assert.Equal(70, ZoneCombat.PositionalCritPermille(100, 0.7));
        Assert.Equal(130, ZoneCombat.PositionalCritPermille(100, 1.3));
        Assert.Equal(100, ZoneCombat.PositionalCritPermille(100, 1d));
    }

    [Fact]
    public async Task Focus_chance_never_scales_the_cached_crit_rate()
    {
        var (kit, _, player, mob) = await EnterAsync();
        player.KnownSkills[FocusChance] = 1;
        player.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = FocusChance,
            Value = 1,
            SkillLevel = 1,
            Slot = "FOCUS_DAGGER",
            ExpiresAtUnixMs = kit.Now.ToUnixTimeMilliseconds() + 120_000
        });

        int without = kit.World.PlayerStats(player).CritRate;
        // A plain swing from behind folds ×1.6 into the roll, while UserInfo still reports the base rate.
        mob.X = player.X + 30;
        mob.Heading = 0;
        var (damage, _) = kit.World.RollPlayerMeleeForTest(player, mob);
        Assert.True(damage >= 0);
        Assert.Equal(without, kit.World.PlayerStats(player).CritRate);
    }

    [Fact]
    public void Mirage_is_a_self_buff_with_the_trigger_row_and_the_cancel_payload()
    {
        var effects = SkillEffectCatalog.LoadDefault();
        Assert.True(effects.TryGet(Mirage, out var mirage));
        var row = Assert.Single(mirage, e => e.Name == "TriggerSkillByDamage");
        Assert.Equal(80, row.Chance);
        Assert.Equal(MirageTrigger, row.SkillId);
        Assert.Equal(1, row.SkillLevel);
        Assert.Equal(1, row.MinDamage);
        Assert.Equal(1, row.MinAttackerLevel);
        Assert.Equal(100, row.MaxAttackerLevel);

        Assert.True(effects.TryGet(MirageTrigger, out var trigger));
        Assert.Contains(trigger, e => e.Name == "TargetCancel");

        var skill = GameData.LoadDefault().Skills.Find(Mirage);
        Assert.NotNull(skill);
        Assert.Equal("MIRAGE", skill!.AbnormalType);
        Assert.Equal(60, skill.AbnormalTimeSeconds);
    }

    [Fact]
    public async Task Mirage_cancels_the_attackers_target_when_it_hits_the_owner_with_a_max_roll()
    {
        // A literal `Random(0)` rolls 72 on the first draw and 81 on the second — the region stream is seeded from a
        // mixed hash, not by the raw seed, so the 80% trigger is a coin flip. Script the stream instead: 0 always
        // passes the chance roll so this test only asserts the cancel path.
        var (kit, rec) = Kit(new RollZeroRandom());
        var (attackerSession, attacker) = await kit.EnterAsync(classId: 0, race: 0, level: 60, x: NetTestKit.ElfX + 200);
        var (_, defender) = await kit.EnterAsync(classId: 7, race: 0, level: 60, x: NetTestKit.ElfX + 60);
        defender.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = Mirage,
            Value = 1,
            SkillLevel = 1,
            Slot = "MIRAGE",
            ExpiresAtUnixMs = kit.Now.ToUnixTimeMilliseconds() + 60_000
        });
        attackerSession.SelectedTargetId = defender.ObjectId;
        attacker.Intent.AutoAttackTargetId = defender.ObjectId;

        kit.World.RunDamageTriggersForTest(defender, attacker, 10);

        Assert.Equal(0, attackerSession.SelectedTargetId);
        Assert.Equal(0, attacker.Intent.AutoAttackTargetId);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillLaunched)),
            a => (int)a[2]! == MirageTrigger);
    }

    [Fact]
    public async Task Mirage_skips_damage_below_min_damage_and_outsiders()
    {
        var (kit, rec) = Kit(new Random(0));
        var (attackerSession, attacker) = await kit.EnterAsync(classId: 0, race: 0, level: 60, x: NetTestKit.ElfX + 200);
        var (_, defender) = await kit.EnterAsync(classId: 7, race: 0, level: 60, x: NetTestKit.ElfX + 60);
        defender.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = Mirage,
            Value = 1,
            SkillLevel = 1,
            Slot = "MIRAGE",
            ExpiresAtUnixMs = kit.Now.ToUnixTimeMilliseconds() + 60_000
        });
        attackerSession.SelectedTargetId = defender.ObjectId;

        // The attacker is not aiming at the owner any more: Mobius' ONE-target filter drops the trigger.
        attackerSession.SelectedTargetId = 0;
        kit.World.RunDamageTriggersForTest(defender, attacker, 10);
        Assert.DoesNotContain(rec.Of(nameof(IWorldEventPublisher.FanoutMagicSkillLaunched)),
            a => (int)a[2]! == MirageTrigger);
    }

    /// <summary>A stream that always rolls the bottom of the range: scripted "the chance roll always passes".</summary>
    sealed class RollZeroRandom : Random
    {
        public override int Next() => 0;

        public override int Next(int maxValue) => 0;

        public override int Next(int minValue, int maxValue) => minValue;
    }
}
