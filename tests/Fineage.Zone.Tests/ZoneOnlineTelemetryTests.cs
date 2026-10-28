using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Telemetry;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

[Collection("Zone pipeline OTEL telemetry")]
public sealed class ZoneOnlineTelemetryTests
{
    [Fact]
    public async Task Enter_world_observable_zone_online_gauge()
    {
        int observed = -1;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.zone.online")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<int>((_, measurement, _, _) => observed = measurement);
        meterListener.Start();

        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var world = new ZoneWorld();
        var server = new ZoneServer(tokens, world);
        FineageTelemetry.OnlineCount = () => world.OnlinePlayerCount;
        try
        {
            var login = new LoginService(JsonAccountStore.InMemory(), tokens);
            var ticket = login.Play("hero", "pass1234", "Fighter", HumanClasses.Fighter);
            Assert.True(ticket.Ok && ticket.Value != null);

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var cts = new CancellationTokenSource();
            var acceptTask = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = accepted.GetStream();
                await server.HandleAsync(stream, cts.Token);
            });

            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            await using var stream = client.GetStream();
            await FrameIO.SendAsync(stream, (ushort)MsgType.Hello, MessagePackSerializer.Serialize(new HelloMsg()));
            await ReadAsync(stream, MsgType.HelloAck);
            await FrameIO.SendAsync(stream, (ushort)MsgType.Auth,
                MessagePackSerializer.Serialize(ticket.Value!.ToZoneAuth()));
            await ReadAsync(stream, MsgType.AuthOk);
            await FrameIO.SendAsync(stream, (ushort)MsgType.EnterWorld, MessagePackSerializer.Serialize(new EnterWorldMsg()));
            await ReadAsync(stream, MsgType.UserInfo);
            await ReadAsync(stream, MsgType.ItemList);
        await ZoneEnterWire.ReadSkillListAsync(stream);
            await ReadAsync(stream, MsgType.PlayerSpawned);
            for (int i = 0; i < TalkingIsland.NpcCount; i++)
                await ReadAsync(stream, MsgType.NpcInfo);

            meterListener.RecordObservableInstruments();
            Assert.Equal(1, observed);

            listener.Stop();
            await cts.CancelAsync();
            try
            {
                await acceptTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
        finally
        {
            FineageTelemetry.OnlineCount = null;
        }
    }

    static async Task ReadAsync(NetworkStream stream, MsgType expected)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)expected, frame!.Value.Type);
    }
}
