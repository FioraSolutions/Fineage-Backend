using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

[CollectionDefinition("Zone load-test gate", DisableParallelization = true)]
public sealed class ZoneLoadTestGateCollection;

/// <summary>
/// Perf #965: the load-test combat profile must build a Warlord that knows the scenario's pole AoE rotation, and the
/// load-test TeleportToLocation path must stay closed to every account outside the load-test prefix.
/// </summary>
[Collection("Zone load-test gate")]
public sealed class LoadTestCombatProfileTests
{
    const int ArenaX = -71100, ArenaY = 258400, ArenaZ = -3148;

    /// <summary>The arena map is disabled in the datapack (hosted worlds never spawn it); tests enable their copy.</summary>
    static NetTestKit Arena()
    {
        var arena = Fineage.Zone.Spawns.SpawnCatalog.LoadDefault().Maps["loadtest_aoe_arena"];
        Assert.False(arena.Enabled);
        var catalog = Fineage.Zone.Spawns.SpawnCatalog.From(new Dictionary<string, Fineage.Zone.Spawns.SpawnMapDefinition>
        {
            [arena.MapId] = new(arena.MapId, true, arena.Npcs)
        });
        return new NetTestKit(catalog, arena.MapId);
    }

    [Fact]
    public async Task Profile_makes_a_warlord_that_casts_whirlwind_and_thunder_storm()
    {
        var kit = Arena();
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0, level: 77, x: ArenaX, y: ArenaY, z: ArenaZ);
        kit.World.RunStepForTest(() => { kit.World.ApplyLoadTestCombatProfile(player); return 0; });

        Assert.Equal(LoadTestCombatSettings.WarlordClassId, player.ClassId);
        Assert.Equal(3, player.ClassId);
        Assert.Equal(LoadTestCombatSettings.CombatLevel, player.Level);
        Assert.True(player.Exp >= Fineage.Accounts.ClassTemplates.Catalog.Experience.ExpToBeLevel(LoadTestCombatSettings.CombatLevel));
        foreach (int skill in LoadTestCombatSettings.RequiredSkillIds)
            Assert.True(player.KnownSkills.ContainsKey(skill), $"warlord does not know skill {skill}");
        var spear = player.Inventory!.FindByItemId(LoadTestCombatSettings.PolearmItemId);
        Assert.NotNull(spear);
        Assert.True(spear!.IsEquipped);

        var mob = kit.World.AttackableForTest()!;
        foreach (int skill in LoadTestCombatSettings.RequiredSkillIds)
        {
            session.SelectedTargetId = mob.ObjectId;
            Assert.Null(await kit.World.TrySkillUseAsync(session, skill, mob.ObjectId, CancellationToken.None, ctrlPressed: false));
            kit.Now = kit.Now.AddSeconds(30);
        }
    }

    [Fact]
    public async Task Profile_reapplied_on_relog_keeps_one_spear()
    {
        var kit = Arena();
        var (_, player) = await kit.EnterAsync(classId: 0, race: 0, level: 77, x: ArenaX, y: ArenaY, z: ArenaZ);
        kit.World.RunStepForTest(() => { kit.World.ApplyLoadTestCombatProfile(player); return 0; });
        kit.World.RunStepForTest(() => { kit.World.ApplyLoadTestCombatProfile(player); return 0; });

        Assert.Single(player.Inventory!.Items, i => i.ItemId == LoadTestCombatSettings.PolearmItemId);
    }

    [Fact]
    public async Task Teleport_is_unknown_opcode_unless_enabled_for_a_load_test_account()
    {
        var kit = Arena();
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0, level: 77, x: ArenaX, y: ArenaY, z: ArenaZ);
        Assert.Equal("unknown_opcode", kit.World.RunStepForTest(() => kit.World.TryLoadTestTeleport(session, ArenaX + 300, ArenaY, ArenaZ)));

        Environment.SetEnvironmentVariable(LoadTestCombatSettings.TeleportEnvVar, "1");
        Environment.SetEnvironmentVariable(LoadTestCombatSettings.AccountPrefixVar, "nobody-");
        try
        {
            Assert.Equal("unknown_opcode", kit.World.RunStepForTest(() => kit.World.TryLoadTestTeleport(session, ArenaX + 300, ArenaY, ArenaZ)));

            Environment.SetEnvironmentVariable(LoadTestCombatSettings.AccountPrefixVar, "net");
            Assert.Null(kit.World.RunStepForTest(() => kit.World.TryLoadTestTeleport(session, ArenaX + 300, ArenaY, ArenaZ)));
            Assert.Equal(ArenaX + 300, player.X);
            Assert.Equal(ArenaY, player.Y);
        }
        finally
        {
            Environment.SetEnvironmentVariable(LoadTestCombatSettings.TeleportEnvVar, null);
            Environment.SetEnvironmentVariable(LoadTestCombatSettings.AccountPrefixVar, null);
        }
    }
}
