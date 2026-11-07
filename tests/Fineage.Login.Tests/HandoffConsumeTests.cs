using Fineage.Accounts;
using Fineage.Game;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>Zone-side one-time handoff consumption (in-memory MVP).</summary>
public sealed class HandoffConsumeTests
{
    static CharacterRecord Hero(string account = "hero") =>
        new(
            Id: 1,
            Name: "HeroOne",
            Account: account,
            ClassId: HumanClasses.Fighter,
            Race: HumanClasses.RaceHuman,
            Sex: HumanClasses.SexMale,
            Level: 1,
            CreatedAt: DateTimeOffset.UnixEpoch,
            LastAccess: DateTimeOffset.UnixEpoch);

    [Fact]
    public void TryConsume_accepts_a_fresh_token_once()
    {
        var store = new MemoryHandoffStore(new HandoffTokens(HandoffTokens.DefaultSecret));
        var now = DateTimeOffset.UnixEpoch.AddHours(1);
        string token = store.Issue(Hero(), now).Token;

        Assert.Equal(
            HandoffTokens.ValidateStatus.Ok,
            store.TryConsume(token, now.AddSeconds(1), out var claims));
        Assert.Equal("hero", claims.Account);
    }

    [Fact]
    public void Second_consume_of_the_same_token_is_invalid()
    {
        var store = new MemoryHandoffStore(new HandoffTokens(HandoffTokens.DefaultSecret));
        var now = DateTimeOffset.UnixEpoch.AddHours(1);
        string token = store.Issue(Hero(), now).Token;

        Assert.Equal(HandoffTokens.ValidateStatus.Ok, store.TryConsume(token, now, out _));
        Assert.Equal(
            HandoffTokens.ValidateStatus.Invalid,
            store.TryConsume(token, now, out _));
    }

    [Fact]
    public void Expired_token_is_not_consumed()
    {
        var store = new MemoryHandoffStore(new HandoffTokens(HandoffTokens.DefaultSecret, TimeSpan.FromMinutes(1)));
        var issued = DateTimeOffset.UnixEpoch.AddHours(1);
        string token = store.Issue(Hero(), issued).Token;

        Assert.Equal(
            HandoffTokens.ValidateStatus.Expired,
            store.TryConsume(token, issued.AddMinutes(2), out _));
        Assert.Equal(
            HandoffTokens.ValidateStatus.Ok,
            store.TryConsume(token, issued.AddSeconds(30), out _));
    }

    [Fact]
    public void Invalid_mac_is_rejected_without_marking_consumed()
    {
        var store = new MemoryHandoffStore(new HandoffTokens(HandoffTokens.DefaultSecret));
        var now = DateTimeOffset.UnixEpoch;

        Assert.Equal(
            HandoffTokens.ValidateStatus.Invalid,
            store.TryConsume("not-a-token", now, out _));
    }
}
