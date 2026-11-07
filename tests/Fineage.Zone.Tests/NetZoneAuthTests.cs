using Fineage.Accounts;
using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Zone handoff auth on Net: one auth per session, a blank account is a bad request, and an expired or
/// forged handoff token never binds a character. These used to be asserted through the V2 listener.
/// </summary>
public sealed class NetZoneAuthTests
{
    [Fact]
    public async Task Second_auth_on_the_same_session_is_refused_as_already_authenticated()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await NetZoneHost.WithSessionAsync(async session =>
        {
            await session.HelloAsync(timeout.Token);
            await session.AuthAsync("hero", "HeroOne", timeout.Token);

            string code = await session.RefusalCodeAsync(
                NetOpcode.ZoneAuth,
                NetCodec.EncodeZoneAuth(new ZoneAuthMsg
                {
                    Account = "hero",
                    Token = session.Ticket("hero", "HeroTwo").Token
                }),
                timeout.Token);
            Assert.Equal("already_authenticated", code);

            // The first character is still the bound one: the refused second auth did not swap it.
            await session.EnterWorldAsync(timeout.Token);
            Assert.Equal("HeroOne", session.EnteredName);
        });
    }

    [Fact]
    public async Task Auth_with_a_blank_account_is_a_bad_request()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await NetZoneHost.WithSessionAsync(async session =>
        {
            await session.HelloAsync(timeout.Token);

            string code = await session.RefusalCodeAsync(
                NetOpcode.ZoneAuth,
                NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = "   ", Token = session.Ticket("hero", "HeroOne").Token }),
                timeout.Token);

            Assert.Equal("bad_request", code);
        });
    }

    [Fact]
    public async Task Expired_handoff_is_refused_and_binds_nothing()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var issuedAt = DateTimeOffset.UnixEpoch.AddDays(1);
        await using var host = await NetZoneHost.StartAsync(clock: () => issuedAt.AddMinutes(11));
        await using var session = await host.ConnectAsync();

        await session.HelloAsync(timeout.Token);

        string code = await session.RefusalCodeAsync(
            NetOpcode.ZoneAuth,
            NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = "hero", Token = session.Ticket("hero", "HeroOne", () => issuedAt).Token }),
            timeout.Token);
        Assert.Equal("handoff_expired", code);

        // Nothing was bound, so EnterWorld still stops at the auth gate.
        string enterCode = await session.RefusalCodeAsync(
            NetOpcode.EnterWorld,
            NetCodec.EncodeEnterWorld(new EnterWorldMsg()),
            timeout.Token);
        Assert.Equal("auth_required", enterCode);
    }

    [Fact]
    public async Task A_custom_handoff_ttl_expires_on_the_zone_clock()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var issuedAt = DateTimeOffset.UnixEpoch.AddHours(3);
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret, TimeSpan.FromSeconds(45));
        await using var host = await NetZoneHost.StartAsync(tokens: tokens, clock: () => issuedAt.AddSeconds(45));
        await using var session = await host.ConnectAsync();

        await session.HelloAsync(timeout.Token);

        Assert.Equal("handoff_expired", await session.RefusalCodeAsync(
            NetOpcode.ZoneAuth,
            NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = "hero", Token = session.Ticket("hero", "HeroOne", () => issuedAt).Token }),
            timeout.Token));
    }

    [Fact]
    public async Task A_custom_handoff_ttl_is_accepted_one_second_before_it_lapses()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var issuedAt = DateTimeOffset.UnixEpoch.AddHours(3);
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret, TimeSpan.FromSeconds(45));
        await using var host = await NetZoneHost.StartAsync(tokens: tokens, clock: () => issuedAt.AddSeconds(44));
        await using var session = await host.ConnectAsync();

        await session.HelloAsync(timeout.Token);
        await session.AuthAsync("hero", "HeroOne", timeout.Token);
    }

    [Fact]
    public async Task A_forged_token_is_refused_as_invalid_credentials()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await NetZoneHost.WithSessionAsync(async session =>
        {
            await session.HelloAsync(timeout.Token);

            string code = await session.RefusalCodeAsync(
                NetOpcode.ZoneAuth,
                NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = "hero", Token = "not-a-token" }),
                timeout.Token);

            Assert.Equal("invalid_credentials", code);
        });
    }
}
