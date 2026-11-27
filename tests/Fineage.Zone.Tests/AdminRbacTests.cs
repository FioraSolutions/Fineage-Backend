using Fineage.Zone.Persistence;
using System.Text.Json;
using Fineage.Game.Punishment;
using Fineage.Zone.Punishment;
using Fineage.Zone.Security;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #991 admin RBAC and audit contract. The policy is process-wide, so this suite joins the existing
/// admin-gate collection and restores the environment on dispose.
/// </summary>
[Collection("Zone admin gate")]
[Trait("Category", "Integration")]
public sealed class AdminRbacTests : IDisposable
{
    readonly string? _prevEnv;
    readonly string _stateDir;
    readonly WorldStateStore _worldState = WorldStateStore.FromDocuments();

    public AdminRbacTests()
    {
        _prevEnv = Environment.GetEnvironmentVariable(ZoneAdmin.EnvVar);
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
        ZoneAdmin.SetNamesForTest(["KuK"]);
        _stateDir = Path.Combine(Path.GetTempPath(), "fineage-admin-rbac-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_stateDir);
    }

    public void Dispose()
    {
        ZoneAdmin.SetNamesForTest(null);
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, _prevEnv);
        try { Directory.Delete(_stateDir, recursive: true); } catch { }
    }

    PunishmentManager OpenManager() =>
        new(
            new StoredPunishmentStore(_worldState),
            JailZoneCatalog.LoadDefault(),
            GmAuditLog.ForStateDirectory(_stateDir));

    [Fact]
    public void Policy_has_an_explicit_player_helper_gm_admin_hierarchy()
    {
        var policy = AdminPolicy.Parse(
            ["LegacyAdmin"],
            new Dictionary<string, string>
            {
                ["HelperOne"] = "helper",
                ["GmOne"] = "gm",
                ["Boss"] = "admin"
            },
            DeploymentEnvironment.Development);

        Assert.Null(policy.Error);
        Assert.Equal(AdminRole.Player, policy.RoleOf("RandomPlayer"));
        Assert.Equal(AdminRole.Admin, policy.RoleOf("LegacyAdmin"));
        Assert.Equal(AdminRole.Helper, policy.RoleOf("HelperOne"));
        Assert.Equal(AdminRole.Gm, policy.RoleOf("GmOne"));
        Assert.Equal(AdminRole.Admin, policy.RoleOf("Boss"));
        Assert.True(AdminCommandCatalog.Allows(policy.RoleOf("Boss"), AdminRole.Gm));
        Assert.False(AdminCommandCatalog.Allows(policy.RoleOf("HelperOne"), AdminRole.Gm));
    }

    [Fact]
    public void Production_rejects_a_wildcard_but_development_keeps_it_for_local_play()
    {
        AdminPolicy dev = AdminPolicy.Parse([AdminPolicy.AnyCharacter], null, DeploymentEnvironment.Development);
        Assert.Null(dev.Error);
        Assert.True(dev.HasWildcard);
        Assert.Equal(AdminRole.Admin, dev.RoleOf("AnyLocalName"));

        AdminPolicy prod = AdminPolicy.Parse([AdminPolicy.AnyCharacter], null, DeploymentEnvironment.Production);
        Assert.NotNull(prod.Error);
        Assert.Contains("not allowed in production", prod.Error);
        Assert.False(prod.HasWildcard);
        Assert.Equal(AdminRole.Player, prod.RoleOf("AnyLocalName"));
    }

    [Fact]
    public void Invalid_roles_fail_closed_instead_of_leaving_a_legacy_admin_grant()
    {
        var policy = AdminPolicy.Parse(
            ["LegacyAdmin"],
            new Dictionary<string, string> { ["LegacyAdmin"] = "owner" },
            DeploymentEnvironment.Development);

        Assert.NotNull(policy.Error);
        Assert.Equal(AdminRole.Player, policy.RoleOf("LegacyAdmin"));
    }

    [Fact]
    public async Task Every_registered_bypass_command_is_refused_for_a_player_through_common_dispatch()
    {
        ZoneAdmin.SetPolicyForTest([]);
        var kit = new WorldBench(punishments: OpenManager());
        BenchPlayer player = await kit.EnterAsync(name: "NotAdmin");

        foreach (AdminCommandSpec spec in AdminCommandCatalog.AllBypassCommands)
        {
            kit.Rec.Calls.Clear();
            Assert.Equal("forbidden", await player.BypassAsync(0, spec.Name));
            Assert.DoesNotContain(
                kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
                args => args.Length > 1 && args[1] is Fineage.Protocol.SystemMessageMsg msg &&
                        msg.Text.Contains("Unknown admin command", StringComparison.Ordinal));
        }

        string audit = await File.ReadAllTextAsync(Path.Combine(_stateDir, "gm-audit.jsonl"));
        Assert.Contains("denied role=Player", audit);
    }

    [Fact]
    public async Task Every_registered_bypass_command_reaches_its_handler_for_admin()
    {
        ZoneAdmin.SetNamesForTest(["KuK"]);
        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");

        foreach (AdminCommandSpec spec in AdminCommandCatalog.AllBypassCommands)
        {
            kit.Rec.Calls.Clear();
            try
            {
                await gm.BypassAsync(0, spec.Name);
            }
            catch (InvalidOperationException ex) when (spec.Name is "admin_buff" or "admin_buffs")
            {
                // Pre-existing handler contract: a real buff needs an effect id. Reaching this throw
                // still proves dispatch did not fall through to the unknown-command path.
                Assert.Contains("buff request requires", ex.Message, StringComparison.Ordinal);
            }

            Assert.DoesNotContain(
                kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
                args => args.Length > 1 && args[1] is Fineage.Protocol.SystemMessageMsg msg &&
                        msg.Text.Contains("Unknown admin command", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("admin_heal", AdminRole.Gm)]
    [InlineData("admin_kill", AdminRole.Gm)]
    [InlineData("admin_kill_monster", AdminRole.Gm)]
    [InlineData("admin_res", AdminRole.Gm)]
    [InlineData("admin_res_monster", AdminRole.Gm)]
    [InlineData("admin_delete", AdminRole.Gm)]
    [InlineData("admin_announce", AdminRole.Gm)]
    [InlineData("admin_announce_crit", AdminRole.Gm)]
    [InlineData("admin_online", AdminRole.Helper)]
    [InlineData("admin_invul", AdminRole.Gm)]
    [InlineData("admin_setinvul", AdminRole.Gm)]
    [InlineData("admin_hide", AdminRole.Gm)]
    [InlineData("admin_invis", AdminRole.Gm)]
    [InlineData("admin_invisible", AdminRole.Gm)]
    [InlineData("admin_setinvis", AdminRole.Gm)]
    [InlineData("admin_vis", AdminRole.Gm)]
    [InlineData("admin_visible", AdminRole.Gm)]
    [InlineData("admin_view_petitions", AdminRole.Helper)]
    [InlineData("admin_view_petition", AdminRole.Helper)]
    [InlineData("admin_accept_petition", AdminRole.Gm)]
    [InlineData("admin_reject_petition", AdminRole.Gm)]
    [InlineData("admin_reset_petitions", AdminRole.Gm)]
    [InlineData("admin_force_peti", AdminRole.Gm)]
    [InlineData("admin_ride_wyvern", AdminRole.Gm)]
    [InlineData("admin_ride_strider", AdminRole.Gm)]
    [InlineData("admin_unride_wyvern", AdminRole.Gm)]
    [InlineData("admin_unride_strider", AdminRole.Gm)]
    [InlineData("admin_unride", AdminRole.Gm)]
    public void Tail_bypass_verbs_are_registered_in_the_central_catalog(string verb, AdminRole minimum)
    {
        Assert.True(
            AdminCommandCatalog.TryGetBypass(verb, out AdminCommandSpec spec),
            $"{verb} must be registered in AdminCommandCatalog; the dispatcher only routes catalog entries");
        Assert.Equal(minimum, spec.MinimumRole);
        Assert.Equal(AdminCommandSurface.Bypass, spec.Surface);
    }

    [Fact]
    public async Task Development_legacy_name_policy_reaches_tail_verbs_through_common_dispatch()
    {
        ZoneAdmin.SetPolicyForTest(["KuK"]);
        var kit = new WorldBench(punishments: OpenManager());
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");

        Assert.Null(await gm.BypassAsync(0, "admin_online"));
        Assert.Null(await gm.BypassAsync(0, "admin_heal"));

        string audit = await File.ReadAllTextAsync(Path.Combine(_stateDir, "gm-audit.jsonl"));
        Assert.Contains("\"action\":\"online\"", audit);
        Assert.Contains("\"action\":\"heal\"", audit);
    }

    [Fact]
    public async Task Helper_may_read_online_and_petitions_but_not_mutate_state()
    {
        ZoneAdmin.SetPolicyForTest(
            [],
            new Dictionary<string, string> { ["HelperOne"] = "helper" });
        var kit = new WorldBench(punishments: OpenManager());
        BenchPlayer helper = await kit.EnterAsync(name: "HelperOne");

        Assert.Null(await helper.BypassAsync(0, "admin_online"));
        Assert.Null(await helper.BypassAsync(0, "admin_view_petitions"));
        Assert.Equal("forbidden", await helper.BypassAsync(0, "admin_heal"));
        Assert.Equal("forbidden", await helper.BypassAsync(0, "admin_announce hello"));
        Assert.Equal("forbidden", await helper.BypassAsync(0, "admin_hide on"));
        Assert.Equal("forbidden", await helper.BypassAsync(0, "admin_accept_petition 1"));
    }

    [Fact]
    public async Task Helper_and_gm_cannot_reach_a_higher_role_command()
    {
        ZoneAdmin.SetPolicyForTest(
            [],
            new Dictionary<string, string>
            {
                ["HelperOne"] = "helper",
                ["GmOne"] = "gm"
            });

        var kit = new WorldBench(includeEnabledMaps: true);
        BenchPlayer helper = await kit.EnterAsync(name: "HelperOne");
        BenchPlayer gm = await kit.EnterAsync(name: "GmOne");

        Assert.Equal("forbidden", await helper.BypassAsync(0, "admin_move_to 1 2 3"));
        Assert.Equal("forbidden", await gm.BypassAsync(0, "admin_give 57 1"));
        Assert.Null(await gm.BypassAsync(0, "admin_move_to 46045 41251 -3440"));
    }

    [Fact]
    public async Task Bypass_audit_is_versioned_and_records_who_what_target_before_after_source()
    {
        var manager = OpenManager();
        var kit = new WorldBench(punishments: manager);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");

        Assert.Null(await gm.BypassAsync(0, "admin_setlevel 5"));

        string path = Path.Combine(_stateDir, "gm-audit.jsonl");
        Assert.True(File.Exists(path));
        string line = (await File.ReadAllLinesAsync(path)).Last();
        using JsonDocument row = JsonDocument.Parse(line);
        JsonElement root = row.RootElement;
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal("KuK", root.GetProperty("gm").GetString());
        Assert.Equal("admin_setlevel", root.GetProperty("action").GetString());
        Assert.Equal("5", root.GetProperty("target").GetString());
        Assert.Equal("client-bypass", root.GetProperty("source").GetString());
        Assert.Equal("ok", root.GetProperty("detail").GetString());
        Assert.True(root.GetProperty("atUnixMs").GetInt64() > 0);

        using JsonDocument before = JsonDocument.Parse(root.GetProperty("before").GetString()!);
        using JsonDocument after = JsonDocument.Parse(root.GetProperty("after").GetString()!);
        Assert.Equal(1, before.RootElement.GetProperty("Level").GetInt32());
        Assert.Equal(5, after.RootElement.GetProperty("Level").GetInt32());
    }

    [Fact]
    public void Gm_audit_rotation_is_bounded_and_keeps_every_line_parseable()
    {
        string path = Path.Combine(_stateDir, "gm-audit.jsonl");
        var log = new GmAuditLog(path, maxBytes: 300, maxArchives: 2);
        string detail = new('x', 200);

        for (int i = 0; i < 20; i++)
        {
            log.Append(new GmAuditRecord(
                GmAuditRecord.CurrentVersion,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                "KuK",
                1,
                "rotate",
                i.ToString(),
                new string('a', 20),
                new string('b', 20),
                "unit-test",
                detail));
        }

        string[] files = Directory.GetFiles(_stateDir, "gm-audit.jsonl*");
        Assert.True(files.Length <= 3, "audit rotation must keep one live file plus bounded archives");
        int rows = 0;
        foreach (string file in files)
        {
            foreach (string line in File.ReadLines(file))
            {
                using JsonDocument row = JsonDocument.Parse(line);
                Assert.Equal(1, row.RootElement.GetProperty("version").GetInt32());
                rows++;
            }
        }

        Assert.True(rows > 0);
    }
}
