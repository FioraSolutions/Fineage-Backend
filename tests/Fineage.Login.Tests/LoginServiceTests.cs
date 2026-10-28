using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Login.Tests;

public class LoginServiceTests
{
    static LoginService NewService(IAccountStore? store = null, DateTimeOffset? now = null) =>
        new(store ?? JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret), () => now ?? DateTimeOffset.UnixEpoch);

    [Fact]
    public void Play_RegistersHumanFighter_AndIssuesZoneToken()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(1);
        var service = NewService(now: now);
        var result = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter);

        Assert.True(result.Ok, result.Message);
        var ticket = result.Value!;
        Assert.Equal("alice", ticket.Account);
        Assert.Equal("Aria", ticket.CharacterName);
        Assert.Equal(HumanClasses.Fighter, ticket.ClassId);
        Assert.Equal(HumanClasses.RaceHuman, ticket.Race);
        Assert.Equal(HumanClasses.SexMale, ticket.Sex);
        Assert.Equal(ZoneHandoff.Host, ticket.ZoneHost);
        Assert.Equal(ZoneHandoff.Port, ticket.ZonePort);
        Assert.StartsWith("fa1.", ticket.Token);

        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        Assert.True(tokens.TryValidate(ticket.Token, now, out var claims));
        Assert.Equal("Aria", claims.CharacterName);
        Assert.Equal(HumanClasses.Fighter, claims.ClassId);

        var auth = ticket.ToZoneAuth();
        var decoded = MessagePackSerializer.Deserialize<AuthMsg>(MessagePackSerializer.Serialize(auth));
        Assert.Equal("alice", decoded.Account);
        Assert.Equal(ticket.Token, decoded.Password);
        Assert.Equal(0x92, MessagePackSerializer.Serialize(auth)[0]);
    }

    [Fact]
    public void Play_CreatesHumanMystic()
    {
        var service = NewService();
        var result = service.Play("bob", "secret1", "MageOne", HumanClasses.Mystic);
        Assert.True(result.Ok, result.Message);
        Assert.Equal(HumanClasses.Mystic, result.Value!.ClassId);
    }

    [Fact]
    public void Register_RejectsDuplicate_AndBadPassword()
    {
        var service = NewService();
        Assert.True(service.Register("alice", "secret1").Ok);
        var again = service.Register("Alice", "secret1");
        Assert.False(again.Ok);
        Assert.Equal("account_exists", again.Code);

        var login = service.Login("alice", "nope");
        Assert.False(login.Ok);
        Assert.Equal("invalid_credentials", login.Code);

        var shortPw = service.Register("carol", "x");
        Assert.Equal("invalid_password", shortPw.Code);
    }

    [Fact]
    public void CreateHuman_RejectsNonHumanNonMaleAndOtherClasses()
    {
        var service = NewService();
        Assert.True(service.Register("alice", "secret1").Ok);

        Assert.Equal("race_not_supported", service.CreateHuman("alice", "Elf", HumanClasses.Fighter, race: 1).Code);
        Assert.Equal("sex_not_supported", service.CreateHuman("alice", "Fighter", HumanClasses.Fighter, sex: 1).Code);
        Assert.Equal("class_not_supported", service.CreateHuman("alice", "ElfF", classId: 18).Code);
        Assert.Equal("invalid_name", service.CreateHuman("alice", "Bad Name", HumanClasses.Fighter).Code);
    }

    [Fact]
    public void CreateHuman_NamesAreGloballyUnique_AndCappedAtSeven()
    {
        var service = NewService();
        Assert.True(service.Register("alice", "secret1").Ok);
        Assert.True(service.Register("bob", "secret1").Ok);
        Assert.True(service.CreateHuman("alice", "Aria", HumanClasses.Fighter).Ok);
        Assert.Equal("name_taken", service.CreateHuman("bob", "aria", HumanClasses.Mystic).Code);

        for (int i = 0; i < 6; i++)
            Assert.True(service.CreateHuman("alice", "Alt" + i, HumanClasses.Fighter).Ok);
        Assert.Equal("character_limit", service.CreateHuman("alice", "Alt9", HumanClasses.Mystic).Code);
    }

    [Fact]
    public void Select_RequiresOwnership()
    {
        var service = NewService();
        var alice = service.Register("alice", "secret1").Value!;
        var bob = service.Register("bob", "secret1").Value!;
        var created = service.CreateHuman(alice, "Aria", HumanClasses.Fighter).Value!;
        Assert.Equal(LobbyProtocol.CharacterNotFound, service.Select(bob, created.Id).Code);
        Assert.True(service.Select(alice, 0, "aria").Ok);
    }

    [Fact]
    public void Handoff_honors_configured_ttl()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(5);
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret, TimeSpan.FromSeconds(90));
        var service = new LoginService(JsonAccountStore.InMemory(), tokens, () => now);
        var ticket = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter).Value!;

        Assert.Equal(now.ToUnixTimeSeconds() + 90, ticket.ExpiresUnixSeconds);
        Assert.True(tokens.TryValidate(ticket.Token, now.AddSeconds(89), out _));
        Assert.False(tokens.TryValidate(ticket.Token, now.AddSeconds(90), out _));
    }

    [Fact]
    public void Store_ReloadsAccounts_WithoutPlaintextPassword()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fineage-login-" + Guid.NewGuid().ToString("n"));
        string path = Path.Combine(dir, "accounts.json");
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var service = new LoginService(JsonAccountStore.Open(path), tokens);
        Assert.True(service.Play("alice", "secret1", "Aria", HumanClasses.Fighter).Ok);

        string json = File.ReadAllText(path);
        Assert.DoesNotContain("secret1", json);

        var reloaded = new LoginService(JsonAccountStore.Open(path), tokens);
        Assert.True(reloaded.Login("alice", "secret1").Ok);
        Assert.False(reloaded.Login("alice", "secret2").Ok);
        var listed = reloaded.List("alice");
        Assert.True(listed.Ok);
        Assert.Equal("Aria", listed.Value![0].Name);
    }

    [Fact]
    public async Task CliPlay_PrintsHandoff()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fineage-login-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "accounts.json");
        var output = new StringWriter();
        var error = new StringWriter();
        int code = await Cli.RunAsync(["--store", path, "play", "alice", "secret1", "Aria", "mystic"], output, error);
        Assert.Equal(0, code);
        string text = output.ToString();
        Assert.Contains("HANDOFF", text);
        Assert.Contains("character=Aria", text);
        Assert.Contains("classId=10", text);
        Assert.Contains("zone=127.0.0.1:9777", text);
        Assert.Contains("ZONE_AUTH account=alice password=fa1.", text);
        Assert.DoesNotContain("error", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Acceptance_RegistersHumanAndReturnsZoneToken()
    {
        var result = await LoginAcceptance.RunAsync();
        Assert.Equal("HumanOne", result.Fighter.CharacterName);
        Assert.Equal(HumanClasses.Fighter, result.Fighter.ClassId);
        Assert.Equal("selftest", result.Fighter.Account);
        Assert.Equal(HumanClasses.Mystic, result.Mystic.ClassId);
        Assert.Equal("HumanTwo", result.Mystic.CharacterName);
        Assert.Equal(result.Mystic.Token, result.Mystic.ToZoneAuth().Password);
    }
}
