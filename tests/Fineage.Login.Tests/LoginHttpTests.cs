using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Fineage.Login.Tests;

public class LoginHttpTests
{
    [Fact]
    public void DefaultBind_ListensOnAnyInterface()
    {
        Assert.Equal("0.0.0.0", BindAddress.Default);
        Assert.True(BindAddress.TryResolve(null, out var any, out string display));
        Assert.Equal(IPAddress.Any, any);
        Assert.Equal("0.0.0.0", display);
        Assert.True(BindAddress.TryResolve("127.0.0.1", out var loop, out _));
        Assert.True(IPAddress.IsLoopback(loop));
        Assert.False(BindAddress.TryResolve("not-an-address", out _, out _));

        var listener = new TcpListener(any, 0);
        listener.Start();
        try
        {
            var ep = (IPEndPoint)listener.LocalEndpoint;
            Assert.Equal(IPAddress.Any, ep.Address);
            Assert.NotEqual(0, ep.Port);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Register_ThenLogin_ByUsernameAndEmail()
    {
        await using var host = await Start();
        using var client = Client(host);

        var created = await Post(client, "/api/auth/register", new
        {
            username = "Alice_User",
            email = "Alice@Example.com",
            password = "password123"
        });
        Assert.Equal(HttpStatusCode.Created, created.Status);
        Assert.Equal(3, created.Body.EnumerateObject().Count());
        Assert.True(created.Body.GetProperty("ok").GetBoolean());
        Assert.Equal("alice_user", created.Body.GetProperty("username").GetString());
        Assert.Equal("live", created.Body.GetProperty("mode").GetString());
        Assert.Contains("no-store", created.CacheControl, StringComparison.OrdinalIgnoreCase);

        var byName = await Post(client, "/api/auth/login", new { username = "ALICE_USER", password = "password123" });
        Assert.Equal(HttpStatusCode.OK, byName.Status);
        Assert.True(byName.Body.GetProperty("ok").GetBoolean());
        Assert.Equal("alice_user", byName.Body.GetProperty("username").GetString());
        Assert.Equal("live", byName.Body.GetProperty("mode").GetString());

        var byEmail = await Post(client, "/api/auth/login", new { username = "alice@example.com", password = "password123" });
        Assert.Equal(HttpStatusCode.OK, byEmail.Status);
        Assert.Equal("alice_user", byEmail.Body.GetProperty("username").GetString());
    }

    [Fact]
    public async Task Register_RejectsBadInput_AndConflicts()
    {
        await using var host = await Start();
        using var client = Client(host);

        await AssertError(client, "/api/auth/register", "not-json", HttpStatusCode.BadRequest, "invalid_body");
        await AssertError(client, "/api/auth/register", "{}", HttpStatusCode.BadRequest, "invalid_username");
        await AssertError(client, "/api/auth/register",
            Body(new { username = "1abc", email = "a@example.com", password = "password123" }),
            HttpStatusCode.BadRequest, "invalid_username");
        await AssertError(client, "/api/auth/register",
            Body(new { username = "alice", email = "not-an-email", password = "password123" }),
            HttpStatusCode.BadRequest, "invalid_email");
        await AssertError(client, "/api/auth/register",
            Body(new { username = "alice", email = "a@example.com", password = "short" }),
            HttpStatusCode.BadRequest, "invalid_password");
        await AssertError(client, "/api/auth/register",
            Body(new { username = "alice", email = "a@example.com", password = "password 123" }),
            HttpStatusCode.BadRequest, "invalid_password");
        await AssertError(client, "/api/auth/register",
            Body(new { username = "otheruser", email = "other@example.com", password = "otheruser" }),
            HttpStatusCode.BadRequest, "password_matches_username");
        await AssertError(client, "/api/auth/register",
            Body(new { username = "otheruser", email = "other@example.com", password = "other@example.com" }),
            HttpStatusCode.BadRequest, "password_matches_username");

        var first = await Post(client, "/api/auth/register", new
        {
            username = "taken",
            email = "taken@example.com",
            password = "password123"
        });
        Assert.Equal(HttpStatusCode.Created, first.Status);

        await AssertError(client, "/api/auth/register",
            Body(new { username = "taken", email = "fresh@example.com", password = "password123" }),
            HttpStatusCode.Conflict, "username_taken");
        await AssertError(client, "/api/auth/register",
            Body(new { username = "fresh", email = "Taken@Example.com", password = "password123" }),
            HttpStatusCode.Conflict, "email_taken");
    }

    [Fact]
    public async Task Login_RejectsUnknownShape_AndBadCredentials()
    {
        await using var host = await Start();
        using var client = Client(host);

        await AssertError(client, "/api/auth/login", "", HttpStatusCode.BadRequest, "invalid_body");
        await AssertError(client, "/api/auth/login",
            Body(new { username = "ab", password = "password123" }),
            HttpStatusCode.BadRequest, "invalid_username");
        await AssertError(client, "/api/auth/login",
            Body(new { username = "not-email@", password = "password123" }),
            HttpStatusCode.BadRequest, "invalid_email");
        await AssertError(client, "/api/auth/login",
            Body(new { username = "alice" }),
            HttpStatusCode.BadRequest, "invalid_password");

        var created = await Post(client, "/api/auth/register", new
        {
            username = "alice",
            email = "alice@example.com",
            password = "password123"
        });
        Assert.Equal(HttpStatusCode.Created, created.Status);

        await AssertError(client, "/api/auth/login",
            Body(new { username = "alice", password = "wrong-password" }),
            HttpStatusCode.Unauthorized, "invalid_credentials");
        await AssertError(client, "/api/auth/login",
            Body(new { username = "nobody", password = "password123" }),
            HttpStatusCode.Unauthorized, "invalid_credentials");
        await AssertError(client, "/api/auth/login",
            Body(new { username = "missing@example.com", password = "password123" }),
            HttpStatusCode.Unauthorized, "invalid_credentials");
    }

    [Fact]
    public async Task HttpAccount_IsTheTcpStore_IncludingShortTcpPasswords()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fineage-login-http-" + Guid.NewGuid().ToString("n"));
        string path = Path.Combine(dir, "accounts.json");
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var service = new LoginService(JsonAccountStore.Open(path), tokens);
        Assert.True(service.Register("carol", "secret1").Ok);

        await using var host = await LoginHttpHost.StartAsync(service, IPAddress.Loopback, 0);
        using var client = Client(host);

        var tcpLogin = await Post(client, "/api/auth/login", new { username = "Carol", password = "secret1" });
        Assert.Equal(HttpStatusCode.OK, tcpLogin.Status);
        Assert.Equal("carol", tcpLogin.Body.GetProperty("username").GetString());

        var created = await Post(client, "/api/auth/register", new
        {
            username = "dave_1",
            email = "dave@example.com",
            password = "password123"
        });
        Assert.Equal(HttpStatusCode.Created, created.Status);

        var reloaded = new LoginService(JsonAccountStore.Open(path), tokens);
        Assert.Equal("dave_1", reloaded.Login("dave_1", "password123").Value);
        Assert.Equal("dave_1", reloaded.Login("dave@example.com", "password123").Value);
        string json = File.ReadAllText(path);
        Assert.Contains("dave@example.com", json);
        Assert.DoesNotContain("password123", json);
        Assert.DoesNotContain("secret1", json);
    }

    static Task<LoginHttpHost> Start() =>
        LoginHttpHost.StartAsync(new LoginService(JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret)), IPAddress.Loopback, 0);

    static HttpClient Client(LoginHttpHost host) =>
        new() { BaseAddress = new Uri(host.BaseUrl + "/") };

    static async Task<AuthCall> Post(HttpClient client, string path, object body) =>
        await Send(client, path, Body(body));

    static async Task AssertError(HttpClient client, string path, string json, HttpStatusCode status, string code)
    {
        var call = await Send(client, path, json);
        Assert.Equal(status, call.Status);
        Assert.Equal(2, call.Body.EnumerateObject().Count());
        Assert.False(call.Body.GetProperty("ok").GetBoolean());
        Assert.Equal(code, call.Body.GetProperty("error").GetString());
    }

    static async Task<AuthCall> Send(HttpClient client, string path, string json)
    {
        using var response = await client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
        string text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return new AuthCall(response.StatusCode, doc.RootElement.Clone(), response.Headers.CacheControl?.ToString() ?? "");
    }

    static string Body(object value) => JsonSerializer.Serialize(value);

    sealed record AuthCall(HttpStatusCode Status, JsonElement Body, string CacheControl);
}
