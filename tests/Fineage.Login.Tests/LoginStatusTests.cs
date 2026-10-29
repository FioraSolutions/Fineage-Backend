using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Login.Tests;

public sealed class LoginStatusTests
{
    [Fact]
    public async Task Status_shape_cache_control_and_zone_down_zero_players()
    {
        var zone = new MutableZone();
        await using var host = await LoginHttpHost.StartAsync(
            new LoginService(JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret)),
            IPAddress.Loopback,
            0,
            zoneStatus: zone);
        using var client = new HttpClient { BaseAddress = new Uri(host.BaseUrl + "/") };

        zone.Reading = new ZoneStatusReading(true, 4);
        using (HttpResponseMessage up = await client.GetAsync("/api/status"))
        {
            Assert.Equal(HttpStatusCode.OK, up.StatusCode);
            Assert.Equal("application/json", up.Content.Headers.ContentType?.MediaType);
            Assert.True(up.Headers.CacheControl?.NoStore);
            Assert.Null(up.Headers.CacheControl?.MaxAge);
            string text = await up.Content.ReadAsStringAsync();
            Assert.Equal("{\"ok\":true,\"login\":true,\"zone\":true,\"playersOnline\":4}", text);
            using var doc = JsonDocument.Parse(text);
            Assert.Equal(4, doc.RootElement.EnumerateObject().Count());
            Assert.Equal(JsonValueKind.True, doc.RootElement.GetProperty("ok").ValueKind);
            Assert.Equal(JsonValueKind.True, doc.RootElement.GetProperty("login").ValueKind);
            Assert.Equal(JsonValueKind.True, doc.RootElement.GetProperty("zone").ValueKind);
            Assert.Equal(JsonValueKind.Number, doc.RootElement.GetProperty("playersOnline").ValueKind);
        }

        zone.Reading = new ZoneStatusReading(true, 0);
        using (HttpResponseMessage empty = await client.GetAsync("/api/status"))
        {
            Assert.Equal(
                "{\"ok\":true,\"login\":true,\"zone\":true,\"playersOnline\":0}",
                await empty.Content.ReadAsStringAsync());
        }

        zone.Reading = ZoneStatusReading.Down;
        using (HttpResponseMessage down = await client.GetAsync("/api/status"))
        {
            Assert.Equal(HttpStatusCode.OK, down.StatusCode);
            Assert.True(down.Headers.CacheControl?.NoStore);
            Assert.Equal(
                "{\"ok\":true,\"login\":true,\"zone\":false,\"playersOnline\":0}",
                await down.Content.ReadAsStringAsync());
        }

        zone.Reading = new ZoneStatusReading(true, -3);
        using HttpResponseMessage clamped = await client.GetAsync("/api/status");
        Assert.Equal(
            "{\"ok\":true,\"login\":true,\"zone\":true,\"playersOnline\":0}",
            await clamped.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Probe_reads_count_and_treats_failures_as_down()
    {
        var script = new ScriptedHandler();
        using var source = new HttpZoneStatusSource(new Uri("http://127.0.0.1:9/status"), TimeSpan.FromSeconds(1), script);

        script.Body = "{\"playersOnline\":2}";
        Assert.Equal(new ZoneStatusReading(true, 2), await source.ReadAsync(CancellationToken.None));

        script.Body = "{\"PlayersOnline\":6}";
        Assert.Equal(new ZoneStatusReading(true, 6), await source.ReadAsync(CancellationToken.None));

        script.Body = "{\"playersOnline\":0}";
        Assert.Equal(new ZoneStatusReading(true, 0), await source.ReadAsync(CancellationToken.None));

        script.Body = "{\"playersOnline\":-4}";
        Assert.Equal(new ZoneStatusReading(true, 0), await source.ReadAsync(CancellationToken.None));

        script.Status = HttpStatusCode.ServiceUnavailable;
        script.Body = "unavailable";
        Assert.Equal(ZoneStatusReading.Down, await source.ReadAsync(CancellationToken.None));

        script.Status = HttpStatusCode.OK;
        script.Body = "not-json";
        Assert.Equal(ZoneStatusReading.Down, await source.ReadAsync(CancellationToken.None));

        script.Body = "{}";
        Assert.Equal(ZoneStatusReading.Down, await source.ReadAsync(CancellationToken.None));

        script.Error = new HttpRequestException("down");
        Assert.Equal(ZoneStatusReading.Down, await source.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Probe_closed_port_is_zone_down()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var source = new HttpZoneStatusSource(new Uri(ZoneStatus.LoopbackUrl(port)), TimeSpan.FromMilliseconds(500));
        Assert.Equal(ZoneStatusReading.Down, await source.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(null, null, "http://127.0.0.1:7780/status")]
    [InlineData("", "", "http://127.0.0.1:7780/status")]
    [InlineData(null, "7791", "http://127.0.0.1:7791/status")]
    [InlineData("http://10.1.2.3:9/status", "off", "http://10.1.2.3:9/status")]
    public void Probe_url_defaults_to_loopback_status(string? url, string? port, string expected)
    {
        Uri? resolved = ZoneStatus.ResolveProbeUrl(url, port, out string? error);
        Assert.Null(error);
        Assert.Equal(expected, resolved?.AbsoluteUri);
    }

    [Theory]
    [InlineData(null, "0")]
    [InlineData(null, "off")]
    [InlineData(null, "disabled")]
    public void Probe_url_disabled_port_does_not_probe(string? url, string? port)
    {
        Assert.Null(ZoneStatus.ResolveProbeUrl(url, port, out string? error));
        Assert.Null(error);
    }

    [Fact]
    public void Probe_url_rejects_garbage()
    {
        Assert.Null(ZoneStatus.ResolveProbeUrl("not a url", null, out string? urlError));
        Assert.Contains(ZoneStatus.UrlVariable, urlError);
        Assert.Null(ZoneStatus.ResolveProbeUrl("ftp://127.0.0.1/status", null, out _));

        Assert.Equal(0, ZoneStatus.ResolvePort("nope", out string? portError));
        Assert.False(string.IsNullOrEmpty(portError));
        Assert.Null(ZoneStatus.ResolveProbeUrl(null, "nope", out string? probeError));
        Assert.Contains(ZoneStatus.PortVariable, probeError);
        Assert.Equal(ZoneStatus.DefaultPort, ZoneStatus.ResolvePort(null, out _));
    }

    sealed class MutableZone : IZoneStatusSource
    {
        public ZoneStatusReading Reading { get; set; }

        public Task<ZoneStatusReading> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Reading);
    }

    sealed class ScriptedHandler : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Body { get; set; } = "{\"playersOnline\":0}";
        public Exception? Error { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Error != null)
                throw Error;
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json"),
                RequestMessage = request
            });
        }
    }
}
