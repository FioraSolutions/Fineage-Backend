using System.Text.Json;
using Fineage.Game.Punishment;
using Fineage.Login.Net;
using Fineage.Net.Contract;
using Xunit;
using Fineage.Accounts;

namespace Fineage.Login.Tests;

public sealed class LoginBanPunishmentTests : IDisposable
{
    readonly string _stateDir = Path.Combine(Path.GetTempPath(), "fineage-login-ban-" + Guid.NewGuid().ToString("N"));
    readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
    public LoginBanPunishmentTests() => Directory.CreateDirectory(_stateDir);
    public void Dispose() { try { Directory.Delete(_stateDir, recursive: true); } catch { } }
    string PunishmentsPath => Path.Combine(_stateDir, "punishments.json");
    LoginService Service() => new(MemoryAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret), () => _now, accountBans: AccountBanGate.ForPath(PunishmentsPath));
    static void WriteBan(string path, string account, string reason)
    {
        var snapshot = new PunishmentSnapshot { Rows = [ new PunishmentRow { Key = account, Affect = PunishmentAffect.Account, Type = PunishmentType.Ban, Reason = reason } ] };
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonPunishmentStore.JsonOptions));
    }
    [Fact]
    public void Login_ban_and_unban_cycle_via_punishments_fixture()
    {
        var service = Service();
        Assert.True(service.Register("banned", "secret1").Ok);
        Assert.True(service.Login("banned", "secret1").Ok);
        WriteBan(PunishmentsPath, "banned", "Policy violation");
        var refused = service.Login("banned", "secret1");
        Assert.False(refused.Ok);
        Assert.Equal("account_banned", refused.Code);
        Assert.Equal("Policy violation", refused.Message);
        File.Delete(PunishmentsPath);
        Assert.True(service.Login("banned", "secret1").Ok);
    }
    [Fact]
    public async Task Net_auth_returns_account_banned_message()
    {
        var service = Service();
        Assert.True(service.Register("netban", "secret1").Ok);
        WriteBan(PunishmentsPath, "netban", "Login suspended by GM");
        await using var link = await NetLink.OpenAsync(new NetLoginServer(service, clock: () => _now));
        await link.SendAsync(NetOpcode.Hello, NetCodec.EncodeHello(new HelloMsg { Major = 1, Minor = 0, ClientName = "tests", FeatureFlags = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby }));
        Assert.Equal(NetOpcode.HelloAck, (await link.ExpectAsync(NetOpcode.HelloAck)).Opcode);
        await link.SendAsync(NetOpcode.Auth, NetCodec.EncodeAuth(new AuthMsg { Account = "netban", Password = "secret1" }));
        var auth = NetCodec.DecodeAuthResult(await link.ExpectPayloadAsync(NetOpcode.AuthResult));
        Assert.False(auth.Ok);
        Assert.Equal("account_banned", auth.Code);
        Assert.Equal("Login suspended by GM", auth.Message);
    }
}
