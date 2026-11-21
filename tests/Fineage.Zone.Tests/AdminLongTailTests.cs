using Fineage.Game.Punishment;
using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.Punishment;
using Fineage.Game.Punishment;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

[Collection("Zone admin gate")]
public sealed class AdminLongTailTests : IDisposable
{
    readonly string? _prevEnv;
    readonly string _stateDir;

    public AdminLongTailTests()
    {
        _prevEnv = Environment.GetEnvironmentVariable(ZoneAdmin.EnvVar);
        Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
        ZoneAdmin.SetNamesForTest(["KuK"]);
        _stateDir = Path.Combine(Path.GetTempPath(), "fineage-admin-tail-" + Guid.NewGuid().ToString("N"));
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
        new JsonPunishmentStore(Path.Combine(_stateDir, "punishments.json")),
        JailZoneCatalog.LoadDefault(),
        GmAuditLog.ForStateDirectory(_stateDir));

    [Fact]
    public async Task Unknown_admin_bypass_rejects_without_gm_shop_html()
    {
        var kit = new WorldBench(html: Fineage.Zone.NpcHtml.NpcHtmlCatalog.FromDocuments(new Dictionary<string, string>
        {
            ["admin/gmshops.htm"] = "<html><body>GM SHOP FALLBACK</body></html>"
        }));
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");

        Assert.Equal("bad_request", await gm.BypassAsync(0, "admin_not_wired_yet"));
        Assert.Empty(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutNpcHtml)));
        var sys = Assert.IsType<SystemMessageMsg>(kit.Rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);
        Assert.Contains("Unknown admin command", sys.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_kick_disconnects_target_and_writes_audit()
    {
        var manager = OpenManager();
        var kit = new WorldBench(punishments: manager);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");
        BenchPlayer victim = await kit.EnterAsync(name: "KickedOne");

        Assert.Null(await gm.BypassAsync(0, $"admin_kick {victim.Player.Name}"));
        Assert.False(victim.Session.InWorld);

        string auditPath = Path.Combine(_stateDir, "gm-audit.jsonl");
        Assert.True(File.Exists(auditPath));
        string audit = await File.ReadAllTextAsync(auditPath);
        Assert.Contains("\"action\":\"kick\"", audit);
        Assert.Contains("KickedOne", audit);
    }

    [Fact]
    public async Task Gm_chat_kick_disconnects_target()
    {
        var manager = OpenManager();
        var kit = new WorldBench(punishments: manager);
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");
        BenchPlayer victim = await kit.EnterAsync(name: "VictimKick");

        Assert.Null(await kit.World.TrySayAsync(
          gm.Session,
          "//kick VictimKick",
          ChatProtocol.All,
          kit.Now(),
          CancellationToken.None));
        Assert.False(victim.Session.InWorld);
    }

    [Fact]
    public async Task Admin_kick_offline_name_is_bad_request()
    {
        var kit = new WorldBench();
        BenchPlayer gm = await kit.EnterAsync(name: "KuK");
        Assert.Equal("bad_request", await gm.BypassAsync(0, "admin_kick NobodyHere"));
    }
}
