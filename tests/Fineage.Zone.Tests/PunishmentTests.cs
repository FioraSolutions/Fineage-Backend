using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.Punishment;
using Xunit;

namespace Fineage.Zone.Tests;

[Collection("Zone admin gate")]
public sealed class PunishmentTests : IDisposable
{
  readonly string? _prevEnv;
  readonly string _stateDir;

  public PunishmentTests()
  {
    _prevEnv = Environment.GetEnvironmentVariable(ZoneAdmin.EnvVar);
    Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, "1");
    ZoneAdmin.SetNamesForTest(["KuK"]);
    _stateDir = Path.Combine(Path.GetTempPath(), "fineage-punish-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(_stateDir);
  }

  public void Dispose()
  {
    ZoneAdmin.SetNamesForTest(null);
    Environment.SetEnvironmentVariable(ZoneAdmin.EnvVar, _prevEnv);
    try { Directory.Delete(_stateDir, recursive: true); } catch { /* best effort */ }
  }

  PunishmentManager OpenManager() =>
    new(
      new JsonPunishmentStore(Path.Combine(_stateDir, "punishments.json")),
      JailZoneCatalog.LoadDefault(),
      GmAuditLog.ForStateDirectory(_stateDir));

  [Fact]
  public async Task Admin_chat_ban_blocks_general_say()
  {
    var manager = OpenManager();
    var bench = new WorldBench(punishments: manager);
    var gm = await bench.EnterAsync(name: "KuK");
  var target = await bench.EnterAsync(name: "Victim");

    Assert.Null(await gm.BypassAsync(0, $"admin_ban_chat {target.Player.Name} 10"));
    Assert.True(target.Session.ChatBannedUntilUnixMs > bench.Now().ToUnixTimeMilliseconds());

    var refused = await bench.World.TrySayAsync(
      target.Session,
      "hello",
      ChatProtocol.All,
      bench.Now(),
      CancellationToken.None);
    Assert.Null(refused);

    Assert.Null(await gm.BypassAsync(0, $"admin_unban_chat {target.Player.Name}"));
    Assert.Equal(0, target.Session.ChatBannedUntilUnixMs);
  }

  [Fact]
  public async Task Admin_jail_teleports_and_persists()
  {
    var manager = OpenManager();
    var bench = new WorldBench(punishments: manager);
    var gm = await bench.EnterAsync(name: "KuK");
    var target = await bench.EnterAsync(name: "Jailed", x: 46045, y: 41251, z: -3440);

    Assert.Null(await gm.BypassAsync(0, $"admin_jail {target.Player.Name} 30"));
    var jail = manager.Jail.LocationIn;
    Assert.Equal(jail.X, target.Player.X);
    Assert.Equal(jail.Y, target.Player.Y);
    Assert.Equal(jail.Z, target.Player.Z);
    Assert.True(PlayerZoneChecks.IsPlayerJailed(bench.World, target.ObjectId));

    var reloaded = OpenManager();
    Assert.True(reloaded.IsJailed(target.ObjectId, bench.Now().ToUnixTimeMilliseconds()));
  }

  [Fact]
  public async Task Char_ban_refuses_enter_and_audit_appends()
  {
    var manager = OpenManager();
    var bench = new WorldBench(punishments: manager);
    var gm = await bench.EnterAsync(name: "KuK");
    var target = await bench.EnterAsync(name: "BannedOne");

    Assert.Null(await gm.BypassAsync(0, $"admin_ban_char {target.Player.Name}"));

    var record = target.SavedRecord();
    var retry = new Fineage.Zone.ZoneSession();
    retry.Bind(record.Id, target.Ticket.Account, record.Name, record.ClassId, record.Race, record.Sex);
    Assert.False(await bench.World.TryEnterAsync(retry, CancellationToken.None));

    string auditPath = Path.Combine(_stateDir, "gm-audit.jsonl");
    Assert.True(File.Exists(auditPath));
    Assert.Contains("start_Ban", await File.ReadAllTextAsync(auditPath));
  }

  [Fact]
  public void Player_zone_check_stubs_exist()
  {
    var bench = new WorldBench();
    Assert.False(PlayerZoneChecks.IsInFestivalArena(bench.World, 1));
    Assert.False(PlayerZoneChecks.IsInDimensionalRift(bench.World, 1));
    Assert.False(PlayerZoneChecks.IsOlympiadObserver(bench.World, 1));
    Assert.False(PlayerZoneChecks.IsInGrandBossZone(bench.World, 1));
  }
}