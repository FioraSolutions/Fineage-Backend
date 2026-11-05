using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneStatusHostTests
{
    [Fact]
    public async Task Loopback_status_returns_live_count_and_rejects_other_paths()
    {
        int count = 0;
        await using ZoneStatusHost? host = ZoneStatusHost.TryStart(0, () => count, TextWriter.Null);
        Assert.NotNull(host);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        Assert.Equal("{\"playersOnline\":0}", await client.GetStringAsync(host.Url));
        count = 3;
        using HttpResponseMessage up = await client.GetAsync(host.Url);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.Equal("application/json", up.Content.Headers.ContentType?.MediaType);
        Assert.True(up.Headers.CacheControl?.NoStore);
        Assert.Equal("{\"playersOnline\":3}", await up.Content.ReadAsStringAsync());

        count = -2;
        Assert.Equal("{\"playersOnline\":0}", await client.GetStringAsync(host.Url));

        using HttpResponseMessage missing = await client.GetAsync($"http://127.0.0.1:{host.Port}/metrics");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Callback_failure_is_unreachable_to_login()
    {
        await using ZoneStatusHost? host = ZoneStatusHost.TryStart(0, () => throw new InvalidOperationException("boom"), TextWriter.Null);
        Assert.NotNull(host);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using HttpResponseMessage failed = await client.GetAsync(host.Url);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);

        using var probe = new HttpZoneStatusSource(new Uri(host.Url), TimeSpan.FromSeconds(2));
        Assert.Equal(ZoneStatusReading.Down, await probe.ReadAsync(CancellationToken.None));
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
}
