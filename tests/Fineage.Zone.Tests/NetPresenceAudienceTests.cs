using System.Net;
using System.Net.Sockets;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Zone.Net;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class NetPresenceAudienceTests
{
    [Fact]
    public void Audience_snapshot_stores_connection_handles_and_names_missing_object_ids()
    {
        var hub = new NetPresenceHub();
        using var first = new Pair();
        using var second = new Pair();
        hub.Register(1, first.Connection, EntityId.FromObjectId(1));
        hub.Register(2, second.Connection, EntityId.FromObjectId(2, generation: 4));

        AudienceSnapshot audience = hub.CaptureAudience([1, 99, 2], NetOpcode.MoveState);

        Assert.Equal(2, audience.Count);
        Assert.Equal(1, audience.Missing);
        Assert.Equal(1, audience.Handles[0].ObjectId);
        Assert.Equal(1u, audience.Handles[0].EntityGeneration);
        Assert.Same(first.Connection, audience.Handles[0].Connection);
        Assert.Equal(2, audience.Handles[1].ObjectId);
        Assert.Equal(4u, audience.Handles[1].EntityGeneration);
        Assert.Same(second.Connection, audience.Handles[1].Connection);

        hub.Unregister(1, first.Connection);
        AudienceSnapshot afterLeave = hub.CaptureAudience([1], NetOpcode.MoveState);
        Assert.Equal(0, afterLeave.Count);
        Assert.Equal(1, afterLeave.Missing);
    }

    [Fact]
    public async Task Reconnect_generation_does_not_deliver_the_previous_handle()
    {
        await using var stripes = new NetFanoutStripes(stripeCount: 1, queueCapacity: 32);
        var hub = new NetPresenceHub(stripes);
        using var older = new Pair();
        hub.Register(7, older.Connection, EntityId.FromObjectId(7, generation: 1));
        AudienceSnapshot first = hub.CaptureAudience([7], NetOpcode.AttackState);
        Assert.Equal(1, first.Count);

        using var newer = new Pair();
        hub.Register(7, newer.Connection, EntityId.FromObjectId(7, generation: 2));

        // The handle captured before the reconnect must not be written to the new socket.
        stripes.EnqueueTrySend(first.Handles[0], NetOpcode.AttackState, [9, 9, 9, 9]);
        stripes.EnqueueTrySend(hub.CaptureAudience([7]).Handles[0], NetOpcode.AttackState, [1, 2, 3, 4]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var got = await ReadOneAsync(newer.Client, cts.Token);
        Assert.Equal(NetOpcode.AttackState, got.Opcode);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, got.Payload);

        using var quiet = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadOneAsync(older.Client, quiet.Token));
    }

    [Fact]
    public void A_peer_that_detached_before_its_turn_is_skipped_without_a_missing_handle_error()
    {
        var hub = new NetPresenceHub();
        using var stillHere = new Pair();
        using var gone = new Pair();
        hub.Register(11, stillHere.Connection, EntityId.FromObjectId(11));
        hub.Register(12, gone.Connection, EntityId.FromObjectId(12));

        // Detaching runs one player at a time: the snapshot taken before the leave still lists a peer whose entry the
        // earlier player's unload already removed. That is not a lost frame, so it must not report a missing handle.
        AudienceSnapshot ranWhileBothWereLive = hub.CaptureLiveAudience([11, 12]);
        Assert.Equal(2, ranWhileBothWereLive.Count);
        Assert.Equal(0, ranWhileBothWereLive.Missing);

        hub.Unregister(12, gone.Connection);
        AudienceSnapshot afterOneLeft = hub.CaptureLiveAudience([11, 12]);
        Assert.Equal(1, afterOneLeft.Count);
        Assert.Equal(0, afterOneLeft.Missing);
        Assert.Same(stillHere.Connection, afterOneLeft.Handles[0].Connection);
        Assert.False(hub.IsRegistered(12));
        Assert.True(hub.IsRegistered(11));

        // The strict audience capture keeps reporting it, so a genuinely lost handle is still visible.
        AudienceSnapshot strict = hub.CaptureAudience([11, 12], NetOpcode.MoveState);
        Assert.Equal(1, strict.Missing);
    }

    static async Task<PacketEnvelope> ReadOneAsync(TcpClient client, CancellationToken ct)
    {
        PacketEnvelope? frame = await NetFraming.ReadAsync(client.GetStream(), ct);
        Assert.NotNull(frame);
        return frame!;
    }

    sealed class Pair : IDisposable
    {
        readonly TcpListener _listener;
        readonly TcpClient _serverSide;

        public Pair()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Client = new TcpClient();
            Client.Connect((IPEndPoint)_listener.LocalEndpoint!);
            _serverSide = _listener.AcceptTcpClient();
            Connection = new NetConnection(_serverSide, 8);
        }

        public TcpClient Client { get; }
        public NetConnection Connection { get; }

        public void Dispose()
        {
            Connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Client.Dispose();
            _listener.Stop();
        }
    }
}
