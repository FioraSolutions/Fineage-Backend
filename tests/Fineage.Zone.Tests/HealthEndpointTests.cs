using System.Net;
using System.Net.Http;
using System.Text.Json;
using Fineage.Protocol;
using Fineage.Protocol.Health;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>#989: the health board and the zone's loopback /health/live and /health/ready.</summary>
public sealed class HealthEndpointTests
{
    [Fact]
    public void Pending_stage_keeps_readiness_failed_but_not_liveness()
    {
        var board = new HealthBoard();
        board.Require("world", "loading");
        Assert.False(board.Readiness().Ok);
        Assert.Contains("world: loading", board.Readiness().Failures, StringComparison.Ordinal);
        Assert.True(board.Liveness().Ok);
        board.Complete("world");
        Assert.True(board.Readiness().Ok);
        board.Fail("world", "boot validation failed");
        Assert.Equal(503, board.Readiness().StatusCode);
        Assert.Throws<InvalidOperationException>(() => board.Require("world"));
        Assert.Throws<InvalidOperationException>(() => board.Complete("unknown"));
    }

    [Fact]
    public void Throwing_probe_is_a_failed_check_with_the_exception()
    {
        var board = new HealthBoard();
        board.AddLiveProbe("heartbeat", () => throw new TimeoutException("stuck"));
        var live = board.Liveness();
        Assert.False(live.Ok);
        Assert.Contains(live.Checks, check => check.Name == "heartbeat" && check.Detail == "TimeoutException: stuck");
        Assert.False(board.Readiness().Ok);
    }

    [Fact]
    public void Cached_probe_runs_once_per_ttl()
    {
        int calls = 0;
        var probe = new CachedProbe(() => new HealthCheck("database", true, $"call {++calls}"), TimeSpan.FromMinutes(1));
        Assert.Equal("call 1", probe.Read().Detail);
        Assert.Equal("call 1", probe.Read().Detail);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Tick_lag_thresholds_read_the_environment_and_reject_garbage()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(HealthSettings.DefaultReadyTickLagMs), HealthSettings.ReadyTickLag(_ => null));
        Assert.Equal(TimeSpan.FromMilliseconds(250), HealthSettings.LiveTickLag(_ => "250"));
        Assert.Throws<InvalidOperationException>(() => HealthSettings.ReadyTickLag(_ => "soon"));
    }

    [Fact]
    public async Task Zone_status_host_serves_live_and_ready_with_503_until_ready()
    {
        var board = new HealthBoard();
        board.Require("state-audit", "waiting for the world");
        await using ZoneStatusHost? host = ZoneStatusHost.TryStart(0, () => 0, TextWriter.Null, board);
        Assert.NotNull(host);
        using var client = new HttpClient { BaseAddress = new Uri(host!.BaseUrl) };

        using (var live = await client.GetAsync("/health/live"))
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using (var ready = await client.GetAsync("/health/ready?verbose=1"))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            using var doc = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
            Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("state-audit", doc.RootElement.GetProperty("checks")[0].GetProperty("name").GetString());
        }

        board.Complete("state-audit");
        using (var ready = await client.GetAsync("/health/ready/"))
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("{\"playersOnline\":0}", await client.GetStringAsync("/status"));
    }

    [Fact]
    public async Task Simulation_health_follows_the_loop()
    {
        var bench = new WorldBench();
        var world = bench.World;
        Assert.False(world.SimulationStarted);
        Assert.False(world.SimulationHealth(TimeSpan.FromSeconds(5)).Ok);

        using var stop = new CancellationTokenSource();
        world.StartSimulation(stop.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        HealthCheck check;
        do
        {
            check = world.SimulationHealth(TimeSpan.FromSeconds(5));
            if (!check.Ok)
                await Task.Delay(20);
        }
        while (!check.Ok && DateTime.UtcNow < deadline);
        Assert.True(check.Ok, check.Detail);

        // A lag limit the loop cannot meet is a failed check naming the lag.
        await Task.Delay(5);
        var lagging = world.SimulationHealth(TimeSpan.FromTicks(1));
        Assert.False(lagging.Ok);
        Assert.Contains("last loop pass", lagging.Detail, StringComparison.Ordinal);

        stop.Cancel();
        try
        {
            await world.WaitSimulationAsync();
        }
        catch (OperationCanceledException)
        {
        }

        var ended = world.SimulationHealth(TimeSpan.FromSeconds(5));
        Assert.False(ended.Ok);
        Assert.Contains("ended", ended.Detail, StringComparison.Ordinal);
    }
}
