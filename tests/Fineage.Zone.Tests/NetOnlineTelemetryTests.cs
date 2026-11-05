using System.Diagnostics.Metrics;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Zone.Tests;

[Collection("Zone pipeline OTEL telemetry")]
public sealed class NetOnlineTelemetryTests
{
    [Fact]
    public async Task Enter_world_observable_zone_online_gauge()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        int observed = -1;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.zone.online")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<int>((_, measurement, _, _) => observed = measurement);
        meterListener.Start();

        await using var host = await NetZoneHost.StartAsync();
        await using var session = await host.ConnectAsync();
        FineageTelemetry.OnlineCount = () => host.World.OnlinePlayerCount;
        try
        {
            await session.HelloAsync(timeout.Token);
            await session.AuthAsync("hero", "Fighter", timeout.Token);
            await session.EnterWorldAsync(timeout.Token);
            Assert.Equal(1, host.World.OnlinePlayerCount);

            meterListener.RecordObservableInstruments();
            Assert.Equal(1, observed);

            await session.DisconnectAsync();
            meterListener.RecordObservableInstruments();
            Assert.Equal(0, observed);
        }
        finally
        {
            FineageTelemetry.OnlineCount = null;
        }
    }
}
