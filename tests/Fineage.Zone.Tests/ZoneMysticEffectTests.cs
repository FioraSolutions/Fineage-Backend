using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Generic mystic-family handlers (DeathLink, ElementSeed conditions, Distrust, Confuse, CA1 channel, AURA mana burn).
/// Classification is by effect / condition name, never by skill id.
/// </summary>
public sealed class ZoneMysticEffectTests
{
    const int CurseDiscord = 1163;
    const int Madness = 1105;
    const int AuraSymphony = 1288;
    const int ManaStorm = 1399;
    const int Volcano = 1419;
    const int SpellForce = 5105;
    const int MagicSymbol = 8876;
    const int MobId = 9_401_901;
    const int MateId = 9_401_902;

    sealed class AlwaysRoll : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0d;
    }

    static (NetTestKit Kit, NetBridgeRecorder Rec) Kit(bool twoMobs = true)
    {
        var npcs = new List<SpawnNpcDefinition>
        {
            new(MobId, 90000 + 1901, "Gremlin",
                NetTestKit.ElfX + 40, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                Level: 20, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0,
                ClanHelpRange: 400, Clans: ["ogre_clan"])
        };
        if (twoMobs)
        {
            npcs.Add(new(MateId, 90000 + 1902, "Gremlin Mate",
                NetTestKit.ElfX + 60, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                Level: 20, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0,
                ClanHelpRange: 400, Clans: ["ogre_clan"]));
        }
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["mystic"] = new("mystic", true, npcs)
        }), "mystic", geo: GeoEngine.Empty, combatRandom: new AlwaysRoll());
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return (kit, rec);
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob, ZoneEntity? Mate)> EnterAsync(
        int classId = 10, int level = 80, bool twoMobs = true)
    {
        var (kit, _) = Kit(twoMobs);
        var (session, player) = await kit.EnterAsync(classId: classId, race: 0, level: level);
        player.CurMp = player.MaxMp = 4000;
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        kit.World.TryGetEntityById(MateId, out var mate);
        mob.CurMp = mob.MaxMp = 500;
        if (mate != null)
            mate.CurMp = mate.MaxMp = 500;
        return (kit, session, player, mob, mate);
    }

    [Theory]
    [InlineData(100, 100, 0d)]
    [InlineData(50, 100, 1d)]
    [InlineData(0, 100, 2d)]
    [InlineData(25, 100, 1.5d)]
    public void DeathLink_scales_power_from_caster_hp_ratio(int cur, int max, double expect) =>
        Assert.Equal(expect, ZoneWorld.DeathLinkPowerMul(cur, max), precision: 5);

    [Fact]
    public async Task Symphony_refuses_without_element_seeds()
    {
        var (kit, session, player, mob, _) = await EnterAsync();
        player.KnownSkills[AuraSymphony] = 1;
        Assert.Null(await kit.World.TrySkillUseAsync(session, AuraSymphony, MobId, CancellationToken.None));
        Assert.False(player.SkillReuseUntilMs.ContainsKey(AuraSymphony));
        Assert.Equal(0, player.CastingUntilMs);
    }

    [Fact]
    public async Task Symphony_starts_when_any_two_element_seeds_are_present()
    {
        var (kit, session, player, mob, _) = await EnterAsync();
        player.KnownSkills[AuraSymphony] = 1;
        kit.World.AddElementSeedForTest(player, "FIRE");
        kit.World.AddElementSeedForTest(player, "WATER");
        Assert.Equal(1, kit.World.ElementSeedCountForTest(player, "FIRE"));
        Assert.Null(await kit.World.TrySkillUseAsync(session, AuraSymphony, MobId, CancellationToken.None));
        Assert.True(player.CastingUntilMs > 0 || player.SkillReuseUntilMs.ContainsKey(AuraSymphony));
    }

    [Fact]
    public async Task Curse_discord_points_the_monster_at_another_monster()
    {
        var (kit, session, player, mob, mate) = await EnterAsync();
        Assert.NotNull(mate);
        player.KnownSkills[CurseDiscord] = 1;
        Assert.Null(await kit.World.TrySkillUseAsync(session, CurseDiscord, MobId, CancellationToken.None));
        kit.World.ResolvePendingHitForTest(player);
        Assert.Contains(mob.Abnormals, a => a.Slot.Equals("DISTRUST", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(mate!.ObjectId, mob.AggroTargetId);
    }

    [Fact]
    public async Task Madness_applies_confused_and_picks_a_new_target()
    {
        var (kit, session, player, mob, _) = await EnterAsync();
        player.KnownSkills[Madness] = 1;
        Assert.Null(await kit.World.TrySkillUseAsync(session, Madness, MobId, CancellationToken.None));
        kit.World.ResolvePendingHitForTest(player);
        Assert.Contains(mob.Abnormals, a => a.Slot.Equals("CONFUSED", StringComparison.OrdinalIgnoreCase));
        Assert.True(mob.AggroTargetId > 0 && mob.AggroTargetId != mob.ObjectId);
    }

    [Fact]
    public async Task Mana_storm_burns_mp_around_the_caster_without_a_primary()
    {
        var (kit, session, player, mob, mate) = await EnterAsync();
        Assert.NotNull(mate);
        player.KnownSkills[ManaStorm] = 1;
        session.SelectedTargetId = 0;
        Assert.Null(await kit.World.TrySkillUseAsync(session, ManaStorm, targetObjectId: 0, CancellationToken.None));
        kit.World.ResolvePendingHitForTest(player);
        Assert.Equal(438, mob.CurMp);  // 500 - 62
        Assert.Equal(438, mate!.CurMp);
    }

    [Fact]
    public async Task Volcano_refuses_without_spell_force_level_3()
    {
        var (kit, session, player, mob, _) = await EnterAsync();
        player.KnownSkills[Volcano] = 1;
        player.Inventory!.Add(MagicSymbol, 1);
        Assert.Null(await kit.World.TrySkillUseAsync(session, Volcano, MobId, CancellationToken.None));
        Assert.Equal(0, player.ChannelUntilMs);
        Assert.Equal(1, player.Inventory.CountOf(MagicSymbol));
    }

    [Fact]
    public async Task Volcano_channels_and_ticks_damage_at_the_target_position()
    {
        var (kit, session, player, mob, _) = await EnterAsync();
        player.KnownSkills[Volcano] = 1;
        player.Inventory!.Add(MagicSymbol, 1);
        long now = kit.Now.ToUnixTimeMilliseconds();
        player.Abnormals.Add(new ZoneAbnormalEntry
        {
            Id = SpellForce,
            Value = 3,
            SkillLevel = 3,
            Slot = "MAGICAL_STANCE",
            ExpiresAtUnixMs = now + 15_000
        });
        int before = mob.CurHp;
        Assert.Null(await kit.World.TrySkillUseAsync(session, Volcano, MobId, CancellationToken.None));
        Assert.True(player.ChannelUntilMs > 0);
        Assert.Equal(Volcano, player.ChannelSkillId);
        Assert.Equal(0, player.Inventory.CountOf(MagicSymbol));

        kit.Now = kit.Now.AddMilliseconds(player.ChannelTickMs + 50);
        await kit.World.RunCombatTickAsync(CancellationToken.None);
        Assert.True(mob.CurHp < before, "CA1 tick must deal magic damage in affectRange");
    }
}
