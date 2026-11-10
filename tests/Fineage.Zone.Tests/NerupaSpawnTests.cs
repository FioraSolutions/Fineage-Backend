using System.Text.RegularExpressions;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Nerupa 30370 and the Newbie Helper 30400 are the elven newbie guides. Mobius CT 0 Interlude spawns
/// both from the global <c>spawns/InterludeSpawns.xml</c> (this datapack has no ElvenTerritory NPC file),
/// so the elven map the local Net set loads has to carry the rows itself:
/// <c>scripts/gen_elven_newbie_guides.py</c> pins them to the Mobius coordinates.
/// </summary>
public sealed class NerupaSpawnTests
{
    const int NerupaNpcId = 30370;
    const int NewbieHelperNpcId = 30400;

    // Mobius dist/game/data/spawns/InterludeSpawns.xml rows.
    const int NerupaX = 46048, NerupaY = 41200, NerupaZ = -3504, NerupaHeading = 17000;
    const int HelperX = 46112, HelperY = 41200, HelperZ = -3504;

    [Fact]
    public void Nerupa_row_keeps_the_mobius_coordinates()
    {
        var map = SpawnCatalog.LoadDefault().GetMap("elven_village");
        Assert.True(map.Enabled);

        var nerupa = Assert.Single(map.Npcs, row => row.NpcId == NerupaNpcId);
        Assert.Equal((NerupaX, NerupaY, NerupaZ), (nerupa.X, nerupa.Y, nerupa.Z));
        Assert.Equal(NerupaHeading, nerupa.Heading);
        Assert.Equal(60, nerupa.RespawnDelaySeconds);
        Assert.Equal(0, nerupa.RespawnRandomSeconds);

        // Mobius <status attackable="false"> + type Folk: a talk target, never a combat target.
        Assert.Equal("Nerupa", nerupa.Name);
        Assert.Equal("Folk", nerupa.NpcType);
        Assert.False(nerupa.Attackable);
        Assert.Equal(70, nerupa.Level);
        Assert.Equal(2444, nerupa.MaxHp);

        // Exactly one lord of this template across every map, in the town object-id band.
        Assert.Single(SpawnCatalog.LoadDefault().Maps.Values.SelectMany(m => m.Npcs).Where(row => row.NpcId == NerupaNpcId));
        Assert.InRange(nerupa.ObjectId, Fineage.Game.NpcObjectIdAllocator.First, Fineage.Game.NpcObjectIdAllocator.ExclusiveEnd - 1);
    }

    [Fact]
    public void Nerupa_and_the_newbie_helper_share_the_elven_newbie_spawn()
    {
        var map = SpawnCatalog.LoadDefault().GetMap("elven_village");
        var nerupa = Assert.Single(map.Npcs, row => row.NpcId == NerupaNpcId);
        var helper = Assert.Single(map.Npcs, row => row.NpcId == NewbieHelperNpcId);
        Assert.Equal((HelperX, HelperY, HelperZ), (helper.X, helper.Y, helper.Z));

        // Mobius puts them 64 apart on the same z/heading: both fit in one client view.
        double distance = Math.Sqrt(
            Math.Pow(nerupa.X - helper.X, 2) + Math.Pow(nerupa.Y - helper.Y, 2) + Math.Pow(nerupa.Z - helper.Z, 2));
        Assert.InRange(distance, 1, 900);
        Assert.Equal(nerupa.Z, helper.Z);
        Assert.Equal(nerupa.Heading, helper.Heading);
    }

    [Fact]
    public void She_is_in_view_from_every_elven_creation_point()
    {
        // The player hangs around the village and expects her "on the newbie spawn". The newbie spawn is the
        // classes.json creation set for races[1] (Elven Fighter 18 / Elven Mystic 25), so check the real points
        // rather than trusting the file row alone.
        var map = SpawnCatalog.LoadDefault().GetMap("elven_village");
        var nerupa = Assert.Single(map.Npcs, row => row.NpcId == NerupaNpcId);
        double nerupaZ = nerupa.Z;

        var points = Fineage.Accounts.ClassTemplates.Points(18)
            .Concat(Fineage.Accounts.ClassTemplates.Points(25))
            .Distinct()
            .ToArray();
        Assert.NotEmpty(points);

        foreach (var point in points)
        {
            double dx = nerupa.X - point.X, dy = nerupa.Y - point.Y;
            double flat = Math.Sqrt(dx * dx + dy * dy);
            // Comfortably inside the client's view: you see her without walking, and she is not on top of you.
            Assert.InRange(flat, 20, 400);
            Assert.InRange(Math.Abs(nerupaZ - point.Z), 0, 200);
        }

        // Same for the helper: the two guides have to be co-visible from the spawn, not split across the map.
        var helper = Assert.Single(map.Npcs, row => row.NpcId == NewbieHelperNpcId);
        foreach (var point in points)
        {
            double dx = helper.X - point.X, dy = helper.Y - point.Y;
            Assert.InRange(Math.Sqrt(dx * dx + dy * dy), 20, 400);
        }
    }

    [Fact]
    public void Local_net_set_loads_the_whole_world_so_she_is_spawned()
    {
        // The local playtest set is scripts/run-local-net.sh; read its defaults instead of mutating process env
        // (the zone filter is process-global and other tests run in parallel with this one).
        string script = File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "run-local-net.sh"));

        // Hosted Zone defaults to every enabled map; the script must not pin a region whitelist.
        Assert.DoesNotContain("FINEAGE_ZONE_INCLUDE_ENABLED_MAPS", script);
        Assert.Equal(string.Empty, DefaultExport(script, "FINEAGE_ZONE_MAPS"));
        Assert.Equal(string.Empty, DefaultExport(script, "FINEAGE_ZONE_BOUNDS"));
        Assert.Equal("3000", DefaultExport(script, "FINEAGE_ZONE_AOI_CELL"));

        // Empty filters keep everything, so her map and row survive the filtering the zone applies.
        var boxes = ZoneMapFilter.ParseBounds(DefaultExport(script, "FINEAGE_ZONE_BOUNDS"));
        var map = SpawnCatalog.LoadDefault().GetMap("elven_village");
        Assert.True(map.Enabled);
        foreach (var row in map.Npcs.Where(row => row.NpcId is NerupaNpcId or NewbieHelperNpcId))
            Assert.True(ZoneMapFilter.Contains(boxes, row.X, row.Y), $"{row.NpcId} at {row.X},{row.Y} would be filtered out");
    }

    [Fact]
    public async Task Nerupa_enters_the_world_as_a_visible_npc_entity_for_a_new_elf()
    {
        var kit = new NetTestKit(ElvenVillageOnly(), "elven_village");
        var (_, player) = await kit.EnterAsync(classId: 25, race: 1);

        var row = ElvenVillageOnly().GetMap("elven_village").Npcs.Single(r => r.NpcId == NerupaNpcId);
        Assert.True(kit.World.TryGetEntityById(row.ObjectId, out var entity), "Nerupa is not materialised in the elven world.");

        // Live entity: an Npc standing at the Mobius spot, in the entrant's knownlist (so it is sent).
        Assert.Equal(ZoneEntityKind.Npc, entity.Kind);
        Assert.Equal(NerupaNpcId, entity.NpcId);
        Assert.Equal("Nerupa", entity.Name);
        Assert.Equal((NerupaX, NerupaY, NerupaZ), (entity.X, entity.Y, entity.Z));
        Assert.Equal(NerupaHeading, entity.Heading);
        Assert.True(entity.CurHp > 0, "Nerupa spawned dead.");
        Assert.Contains(row.ObjectId, player.Knownlist);
        Assert.Contains(kit.World.NpcObjectIds, id => id == row.ObjectId);

        // Net wire shape the client builds the model from.
        var spawn = NetEnterWorldProjector.BuildPresenceSpawn(entity, EntityId.FromObjectId(row.ObjectId, 1));
        Assert.Equal(EntityKindNet.Npc, spawn.Kind);
        Assert.Equal(NerupaNpcId, spawn.TemplateId);
        Assert.Equal("Nerupa", spawn.Name);
        Assert.Equal((NerupaX, NerupaY, NerupaZ), (spawn.X, spawn.Y, spawn.Z));
        Assert.Equal(NerupaHeading, spawn.Heading);
        Assert.False(spawn.Attackable);
        Assert.False(spawn.Dead);
        Assert.False(spawn.IsSelf);

        var wire = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(spawn));
        Assert.Equal(EntityKindNet.Npc, wire.Kind);
        Assert.Equal(NerupaNpcId, wire.TemplateId);
        Assert.Equal((NerupaX, NerupaY, NerupaZ), (wire.X, wire.Y, wire.Z));
    }

    [Fact]
    public async Task Nerupa_is_selectable_and_talkable_so_q160_still_starts()
    {
        var kit = new NetTestKit(ElvenVillageOnly(), "elven_village");
        var (session, _) = await kit.EnterAsync(classId: 25, race: 1);
        var row = ElvenVillageOnly().GetMap("elven_village").Npcs.Single(r => r.NpcId == NerupaNpcId);

        Assert.Null(await kit.World.TrySelectTargetAsync(session, row.ObjectId, CancellationToken.None));
        Assert.Equal(row.ObjectId, session.SelectedTargetId);
        Assert.Null(await kit.World.TryNpcTalkAsync(session, row.ObjectId, CancellationToken.None));
    }

    /// <summary>Just the village map, so no process-global FINEAGE_ZONE_MAPS / bounds are involved.</summary>
    static SpawnCatalog ElvenVillageOnly()
    {
        var village = SpawnCatalog.LoadDefault().GetMap("elven_village");
        return SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition> { ["elven_village"] = village });
    }

    /// <summary>The value of an <c>export NAME="${NAME-default}"</c> / <c>${NAME:-default}</c> in the run script.</summary>
    static string DefaultExport(string script, string name)
    {
        var match = Regex.Match(script, $@"export\s+{Regex.Escape(name)}=""\$\{{{Regex.Escape(name)}:?-([^}}]*)\}}""");
        Assert.True(match.Success, $"{name} default not found in scripts/run-local-net.sh");
        return match.Groups[1].Value;
    }

    static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "scripts", "run-local-net.sh")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
