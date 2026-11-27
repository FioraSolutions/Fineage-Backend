using System.Text.Json;
using Fineage.Game.Punishment;
using Fineage.Login.Net;
using Fineage.Net.Contract;
using Xunit;
using Fineage.Accounts;

namespace Fineage.Login.Tests;

public sealed class LoginBanPunishmentTests
{
    readonly DateTimeOffset _now = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
    readonly MemoryAccountStore _store = MemoryAccountStore.InMemory();
    long _revision;

    // #996: login reads the punishments the zone committed to the shared store, not a punishments.json file.
    LoginService Service() => new(_store, new HandoffTokens(HandoffTokens.DefaultSecret), () => _now, accountBans: WorldStatePunishments.Gate(_store));

    void WriteBans(params PunishmentRow[] rows)
    {
        var snapshot = new PunishmentSnapshot { Rows = [.. rows] };
        _store.CommitEconomy([], [], [new WorldStateDocument(
            PunishmentStateJson.System, PunishmentStateJson.SchemaVersion, ++_revision,
            JsonSerializer.Serialize(snapshot, PunishmentStateJson.Options))]);
    }

    void WriteBan(string account, string reason) =>
        WriteBans(new PunishmentRow { Key = account, Affect = PunishmentAffect.Account, Type = PunishmentType.Ban, Reason = reason });

    [Fact]
    public void Login_ban_and_unban_cycle_via_punishments_fixture()
    {
        var service = Service();
        Assert.True(service.Register("banned", "secret1").Ok);
        Assert.True(service.Login("banned", "secret1").Ok);
        WriteBan("banned", "Policy violation");
        var refused = service.Login("banned", "secret1");
        Assert.False(refused.Ok);
        Assert.Equal("account_banned", refused.Code);
        Assert.Equal("Policy violation", refused.Message);
        WriteBans();
        Assert.True(service.Login("banned", "secret1").Ok);
    }
    [Fact]
    public async Task Net_auth_returns_account_banned_message()
    {
        var service = Service();
        Assert.True(service.Register("netban", "secret1").Ok);
        WriteBan("netban", "Login suspended by GM");
        await using var link = await NetLink.OpenAsync(new NetLoginServer(service, clock: () => _now));
        await link.SendAsync(NetOpcode.Hello, NetCodec.EncodeHello(new HelloMsg { Major = 1, Minor = 0, ClientName = "tests", FeatureFlags = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby }));
        Assert.Equal(NetOpcode.HelloAck, (await link.ExpectAsync(NetOpcode.HelloAck)).Opcode);
        await link.SendAsync(NetOpcode.Auth, NetCodec.EncodeAuth(new AuthMsg { Account = "netban", Password = "secret1" }));
        var auth = NetCodec.DecodeAuthResult(await link.ExpectPayloadAsync(NetOpcode.AuthResult));
        Assert.False(auth.Ok);
        Assert.Equal("account_banned", auth.Code);
        Assert.Equal("Login suspended by GM", auth.Message);
    }

    [Fact]
    public void Unreadable_saved_punishments_fail_the_login_instead_of_skipping_the_check()
    {
        var service = Service();
        Assert.True(service.Register("someone", "secret1").Ok);
        _store.CommitEconomy([], [], [new WorldStateDocument(PunishmentStateJson.System, 2, ++_revision, "{\"schemaVersion\":2,\"Rows\":[]}")]);

        var failed = Assert.Throws<InvalidOperationException>(() => service.Login("someone", "secret1"));
        Assert.Contains("world_state system='punishment'", failed.Message);
        Assert.Contains("schema version 2", failed.Message);
    }
}
