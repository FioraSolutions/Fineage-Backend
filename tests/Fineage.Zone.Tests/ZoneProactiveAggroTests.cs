using Fineage.Accounts;
using Fineage.Game.Economy;
using Fineage.Login;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Npcs;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius <c>AttackableAI.thinkActive</c>: per-template <c>aggroRange</c>, 2D + |dz| cap, geo LOS.</summary>
public sealed class ZoneProactiveAggroTests
{
    const int KabooCaptainNpcId = 20_472;
    const int KabooCaptainObjectId = 9_204_720;

    [Theory]
    [InlineData(299, true)]
    [InlineData(301, false)]
    public void Proactive_aggro_range_is_2d_with_z_cap_mobius_style(int offset, bool inside)
    {
        int range = 300;
        int ax = 100, ay = 100, az = -3400;
        Assert.Equal(inside, ZoneCombat.WithinProactiveAggroRange(ax, ay, az, ax + offset, ay, az, range));
    }

    [Fact]
    public void Vertical_gap_above_mobius_cap_is_not_proactive_aggro()
    {
        Assert.False(ZoneCombat.WithinProactiveAggroRange(0, 0, 0, 100, 100, 400, 500));
    }

    [Fact]
    public async Task Kaboo_captain_ignores_player_just_outside_template_aggro_range()
    {
        var catalog = NpcCatalog.LoadDefault();
        var template = catalog.Get(KabooCaptainNpcId);
        int aggro = template.AggroRange;
        Assert.Equal(1000, aggro);

        var (world, player) = await EnterWithMobAsync(offsetX: aggro + 50);
        await world.RunCombatTickAsync(CancellationToken.None);

        Assert.True(world.TryGetEntityById(KabooCaptainObjectId, out var mob));
        Assert.Equal(0, mob.AggroTargetId);
        Assert.False(mob.Running);
    }

    [Fact]
    public async Task Kaboo_captain_aggroes_player_inside_template_aggro_range()
    {
        var catalog = NpcCatalog.LoadDefault();
        int aggro = catalog.Get(KabooCaptainNpcId).AggroRange;

        var (world, player) = await EnterWithMobAsync(offsetX: aggro - 100);
        foreach (int id in new[] { KabooCaptainObjectId })
        {
            if (world.TryGetEntityById(id, out var mob))
            {
                mob.GlobalAggro = 0;
                mob.NextAggroScanMs = 0;
            }
        }

        await world.RunCombatTickAsync(CancellationToken.None);
        await world.RunCombatTickAsync(CancellationToken.None);
        Assert.True(world.TryGetEntityById(KabooCaptainObjectId, out var captain));
        Assert.Equal(player.ObjectId, captain.AggroTargetId);
        Assert.True(captain.Running);
    }

    [Fact]
    public async Task Aggressive_mob_ignores_player_when_geo_los_is_blocked()
    {
        var region = GeoEngine.Region.Flat1((short)NetTestKit.ElfZ);
        region.SetComplexBlock(103, 66, (cx, cy) =>
            cx >= 6 ? ((short)(NetTestKit.ElfZ + 600), (byte)0) : ((short)NetTestKit.ElfZ, GeoEngine.All));
        var geo = new GeoEngine();
        geo.SetRegion(21, 19, region);

        var template = NpcCatalog.LoadDefault().Get(KabooCaptainNpcId);
        var mob = new SpawnNpcDefinition(
            KabooCaptainObjectId, KabooCaptainNpcId, template.Name,
            NetTestKit.ElfX + 60, NetTestKit.ElfY, NetTestKit.ElfZ, 0,
            template.Attackable, template.Hp,
            template.RunSpeed, template.WalkSpeed, 60, 0,
            template.Title, template.Type, template.Level,
            AttackRange: template.AttackRange, AttackSpeed: template.AttackSpeed,
            Aggressive: template.Aggressive, AggroRange: template.AggroRange,
            ClanHelpRange: template.ClanHelpRange, Clans: template.Clans);
        var map = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["los"] = new("los", true, [mob])
        });
        var kit = new NetTestKit(map, "los", geo);
        var (_, _) = await kit.EnterAsync();
        foreach (int id in new[] { KabooCaptainObjectId })
        {
            if (kit.World.TryGetEntityById(id, out var npc))
            {
                npc.GlobalAggro = 0;
                npc.NextAggroScanMs = 0;
            }
        }

        kit.World.StepSimulation();
        Assert.True(kit.World.TryGetEntityById(KabooCaptainObjectId, out var captain));
        Assert.Equal(0, captain.AggroTargetId);
    }

    [Fact]
    public void Audit_aggressive_monsters_for_missing_or_unusually_large_aggro_range()
    {
        var catalog = NpcCatalog.LoadDefault();
        var missing = new List<string>();
        var large = new List<string>();
        foreach (var template in catalog.ById.Values)
        {
            if (!template.Aggressive || !template.Attackable || template.Type != "Monster")
                continue;
            if (template.AggroRange <= 0)
                missing.Add($"{template.NpcId} {template.Name}");
            else if (template.AggroRange >= 1000)
                large.Add($"{template.NpcId} {template.Name} aggroRange={template.AggroRange}");
        }

        Assert.Contains(large, s => s.StartsWith("20472 ", StringComparison.Ordinal));
        Assert.Equal(9, missing.Count);
        Assert.All(missing, name => Assert.DoesNotContain("Kaboo Orc Fighter Leader", name));
    }

    static async Task<(ZoneWorld World, ZoneEntity Player)> EnterWithMobAsync(int offsetX, GeoEngine? geo = null)
    {
        // No geo means "see-through" (GeoEngine.Empty), not "whatever data/geodata this machine has": with the retail
        // geodata present the elf spawn sits behind a hill, the LOS check rejects the target and the test flips.
        geo ??= GeoEngine.Empty;
        var store = MemoryAccountStore.InMemory();
        var login = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(login.Register("kaboo", "pass1234").Ok);
        var created = login.CreateHuman("kaboo", "Elf", HumanClasses.Fighter);
        Assert.True(created.Ok);

        static SpawnCatalog EmptyMap() =>
            SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { ["kaboo"] = new("kaboo", true, []) });

        async Task<ZoneEntity> ProbePositionAsync()
        {
            var ticket = login.Play("kaboo", "pass1234", created.Value!.Name, HumanClasses.Fighter);
            var world = new ZoneWorld(accounts: store, spawnCatalog: EmptyMap(), activeMapId: "kaboo",
                ledgerJournal: new MemoryLedgerJournal(), geo: geo);
            var session = new ZoneSession();
            session.Bind(ticket.Value!.CharacterId, ticket.Value.Account, ticket.Value.CharacterName, ticket.Value.ClassId);
            Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
            Assert.True(world.TryGetLivePlayer(session, out var player));
            return player;
        }

        var anchor = await ProbePositionAsync();
        var template = NpcCatalog.LoadDefault().Get(KabooCaptainNpcId);
        var mob = new SpawnNpcDefinition(
            KabooCaptainObjectId, KabooCaptainNpcId, template.Name,
            anchor.X + offsetX, anchor.Y, anchor.Z, 0,
            template.Attackable, template.Hp,
            template.RunSpeed, template.WalkSpeed, 60, 0,
            template.Title, template.Type, template.Level,
            AttackRange: template.AttackRange, AttackSpeed: template.AttackSpeed,
            Aggressive: template.Aggressive, AggroRange: template.AggroRange,
            ClanHelpRange: template.ClanHelpRange, Clans: template.Clans);

        var catalog = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["kaboo"] = new("kaboo", true, [mob])
        });

        var play = login.Play("kaboo", "pass1234", created.Value.Name, HumanClasses.Fighter);
        var fight = new ZoneWorld(accounts: store, spawnCatalog: catalog, activeMapId: "kaboo",
            ledgerJournal: new MemoryLedgerJournal(), geo: geo);
        var fightSession = new ZoneSession();
        fightSession.Bind(play.Value!.CharacterId, play.Value.Account, play.Value.CharacterName, play.Value.ClassId);
        Assert.True(await fight.TryEnterAsync(fightSession, CancellationToken.None));
        Assert.True(fight.TryGetLivePlayer(fightSession, out var player));
        return (fight, player);
    }
}