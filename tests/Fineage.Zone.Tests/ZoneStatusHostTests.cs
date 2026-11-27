using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Fineage.Accounts;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Health;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneStatusHostTests
{
    [Fact]
    public async Task Loopback_status_reports_live_count_and_readiness()
    {
        int count = 0;
        ReadinessReport readiness = Ready("zone");
        await using ZoneStatusHost? host = ZoneStatusHost.TryStart(0, () => count, TextWriter.Null, () => ValueTask.FromResult(readiness));
        Assert.NotNull(host);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using HttpResponseMessage up = await client.GetAsync(host.Url);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal("application/json", up.Content.Headers.ContentType?.MediaType);
        Assert.True(up.Headers.CacheControl?.NoStore);
        using (JsonDocument doc = JsonDocument.Parse(await up.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, doc.RootElement.GetProperty("playersOnline").GetInt32());
            Assert.Single(doc.RootElement.EnumerateObject());
        }

        count = 3;
        using HttpResponseMessage liveCount = await client.GetAsync(host.Url);
        using (JsonDocument doc = JsonDocument.Parse(await liveCount.Content.ReadAsStringAsync()))
            Assert.Equal(3, doc.RootElement.GetProperty("playersOnline").GetInt32());

        readiness = NotReady("zone");
        using HttpResponseMessage notReady = await client.GetAsync(host.Url);
        Assert.Equal(HttpStatusCode.OK, notReady.StatusCode);
        using (JsonDocument doc = JsonDocument.Parse(await notReady.Content.ReadAsStringAsync()))
            Assert.Equal(3, doc.RootElement.GetProperty("playersOnline").GetInt32());

        using HttpResponseMessage ready = await client.GetAsync($"http://127.0.0.1:{host.Port}/readyz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);

        using HttpResponseMessage missing = await client.GetAsync($"http://127.0.0.1:{host.Port}/metrics");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Livez_answers_even_when_readiness_fails()
    {
        await using ZoneStatusHost? host = ZoneStatusHost.TryStart(
            0,
            () => 0,
            TextWriter.Null,
            () => ValueTask.FromResult(NotReady("zone")));
        Assert.NotNull(host);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        using HttpResponseMessage live = await client.GetAsync($"http://127.0.0.1:{host.Port}/livez");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(await live.Content.ReadAsStringAsync());
        Assert.Equal("live", doc.RootElement.GetProperty("status").GetString());

        using HttpResponseMessage ready = await client.GetAsync($"http://127.0.0.1:{host.Port}/readyz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }

    [Fact]
    public async Task Missing_readiness_probe_is_not_healthy()
    {
        await using ZoneStatusHost? host = ZoneStatusHost.TryStart(0, () => 0, TextWriter.Null);
        Assert.NotNull(host);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using HttpResponseMessage ready = await client.GetAsync($"http://127.0.0.1:{host.Port}/readyz");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        using JsonDocument doc = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.False(doc.RootElement.GetProperty("ready").GetBoolean());
        Assert.Contains(
            doc.RootElement.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()),
            reason => reason != null && reason.Contains("not configured", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Callback_failure_is_unreachable_to_login()
    {
        await using ZoneStatusHost? host = ZoneStatusHost.TryStart(
            0,
            () => throw new InvalidOperationException("boom"),
            TextWriter.Null,
            () => ValueTask.FromResult(Ready("zone")));
        Assert.NotNull(host);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using HttpResponseMessage failed = await client.GetAsync(host.Url);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);

        using var probe = new HttpZoneStatusSource(new Uri(host.Url), TimeSpan.FromSeconds(2));
        ZoneStatusReading reading = await probe.ReadAsync(CancellationToken.None);
        Assert.False(reading.Reachable);
    }

    [Fact]
    public void Port_in_use_does_not_throw()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Assert.Null(ZoneStatusHost.TryStart(port, () => 1, TextWriter.Null));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData("GET /status HTTP/1.1", true)]
    [InlineData("GET /status/ HTTP/1.1", true)]
    [InlineData("GET /status?x=1 HTTP/1.1", true)]
    [InlineData("GET /metrics HTTP/1.1", false)]
    [InlineData("POST /status HTTP/1.1", false)]
    public void Request_line_matches_status_path(string request, bool expected) =>
        Assert.Equal(expected, ZoneStatusHost.IsStatusGet(request));

    static ReadinessReport Ready(string role) => ReadinessReport.From(
    [
        new ReadinessCheck("database", true),
        new ReadinessCheck("migrations", true),
        new ReadinessCheck("ledger", true),
        new ReadinessCheck("catalogs", true),
        new ReadinessCheck("world", true),
        new ReadinessCheck("spawns", true),
        new ReadinessCheck("sidecars", true),
        new ReadinessCheck("handoff", true),
        new ReadinessCheck("simulation", true),
        new ReadinessCheck("net_listener", true)
    ]);

    static ReadinessReport NotReady(string role) => ReadinessReport.From(
    [
        new ReadinessCheck("database", false, "connection refused"),
        new ReadinessCheck("migrations", true),
        new ReadinessCheck("ledger", true),
        new ReadinessCheck("catalogs", true),
        new ReadinessCheck("world", true),
        new ReadinessCheck("spawns", true),
        new ReadinessCheck("sidecars", true),
        new ReadinessCheck("handoff", true),
        new ReadinessCheck("simulation", true),
        new ReadinessCheck("net_listener", true)
    ]);
}
