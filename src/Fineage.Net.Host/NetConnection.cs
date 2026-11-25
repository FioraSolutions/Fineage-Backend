using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Channels;
using Fineage.Net.Contract;
using Fineage.Protocol.Telemetry;

namespace Fineage.Net.Host;

/// <summary>
/// One accepted Net socket.
/// Inbound envelopes land in a bounded <see cref="Inbox"/>. Outbound envelopes share one mailbox.
/// Producers never block on it: it is bounded in bytes (soft and hard limit, see <see cref="OutboundMailbox"/>).
/// Movement and status coalesce to the newest per entity, visuals are shed above the soft limit, and a frame that
/// must not be lost and does not fit under the hard limit closes this (slow) connection.
/// The pump writes whenever the mailbox is non-empty and only then waits for a wake, so a missed
/// edge cannot leave queued bytes sitting while the socket is idle.
/// </summary>
public sealed class NetConnection : IAsyncDisposable
{
    static long _nextConnectionId;

    /// <summary>Frames combined into one socket write before the next batch is queued (issue #867 PR3).</summary>
    internal const int MaxFramesPerBatch = 32;

    /// <summary>Bytes combined into one socket write, including frame headers.</summary>
    internal const int MaxBytesPerBatch = 64 * 1024;

    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly Channel<PacketEnvelope> _inbox;
    readonly Channel<byte> _pumpWake;
    readonly OutboundMailbox _mailbox;
    readonly CommandDeduper _deduper = new();
    readonly string _endpoint;
    readonly Task _pump;
    int _closed;
    int _failed;
    int _boundObjectId;
    uint _boundGeneration;
    string? _peerLabel;

    public ulong ConnectionId { get; } = (ulong)Interlocked.Increment(ref _nextConnectionId);

    public NetConnection(TcpClient client, int inboxCapacity, string endpoint = "net", int outboundCapacity = DefaultOutboundCapacity)
    {
        _client = client;
        _endpoint = endpoint;
        _stream = client.GetStream();
        // Wait (not DropOldest): trade/inventory/auth commands must not silently lose earlier
        // frames while later ones still run. Full inbox applies TCP backpressure via the read loop.
        // Movement coalesce can land later as a separate inbound class.
        _inbox = Channel.CreateBounded<PacketEnvelope>(new BoundedChannelOptions(inboxCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _pumpWake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        // Frame cap and byte cap describe the same budget: a mailbox of tiny frames must be able to
        // fill MaxBytesPerBatch before the frame count reports full. outboundCapacity raises that floor.
        int byteBudget = MaxBytesPerBatch;
        int frameBudget = Math.Max(
            Math.Max(8, outboundCapacity),
            Math.Max(1, byteBudget / PacketEnvelope.HeaderSize));
        _mailbox = new OutboundMailbox(frameBudget, byteBudget, ConnectionId, endpoint);
        _pump = PumpAsync();
    }

    void WakePump() => _pumpWake.Writer.TryWrite(1);

    public const int DefaultOutboundCapacity = 256;

    public ChannelReader<PacketEnvelope> Inbox => _inbox.Reader;
    public bool HelloCompleted { get; set; }
    public uint FeatureFlags { get; set; }

    /// <summary>Publishes the player object this socket is currently bound to. Called by the presence hub before the handle is visible.</summary>
    internal void BindPresence(int objectId, uint generation)
    {
        Volatile.Write(ref _boundObjectId, objectId);
        Volatile.Write(ref _boundGeneration, generation);
    }

    /// <summary>Who is on this socket (account / character), for disconnect and overflow logs.</summary>
    public void SetPeerLabel(string label) => Volatile.Write(ref _peerLabel, label);

    /// <summary>connectionId, endpoint, bound object and the peer label, for logs.</summary>
    public string Describe()
    {
        string label = Volatile.Read(ref _peerLabel) ?? "unauthenticated";
        return $"connectionId={ConnectionId} endpoint={_endpoint} objectId={Volatile.Read(ref _boundObjectId)} {label}";
    }

    /// <summary>Clears the binding when it still names <paramref name="objectId"/> at <paramref name="generation"/>.</summary>
    internal void UnbindPresence(int objectId, uint generation)
    {
        if (Volatile.Read(ref _boundObjectId) == objectId && Volatile.Read(ref _boundGeneration) == generation)
        {
            Volatile.Write(ref _boundGeneration, 0);
            Volatile.Write(ref _boundObjectId, 0);
        }
    }

    /// <summary>Send-path generation check. No presence lock: the connection publishes its own binding.</summary>
    internal bool IsBoundTo(ConnectionHandle handle) =>
        Volatile.Read(ref _closed) == 0
        && Volatile.Read(ref _boundObjectId) == handle.ObjectId
        && Volatile.Read(ref _boundGeneration) == handle.EntityGeneration;

    public async Task RunReadLoopAsync(CancellationToken ct)
    {
        try
        {
            await NetInboundPipe.ReadFramesAsync(_stream, AcceptFrameAsync, ct).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or SocketException)
        {
            NetDisconnectDiagnostics.Log("read", ConnectionId, _endpoint, ex, _mailbox.QueuedFrames);
        }
        finally
        {
            _inbox.Writer.TryComplete();
        }
    }

    ValueTask AcceptFrameAsync(PacketEnvelope envelope, CancellationToken ct)
    {
        if (envelope.Major != PacketEnvelope.ProtocolMajor)
            return ValueTask.CompletedTask;

        if (envelope.Payload.Length > PacketEnvelope.MaxPayloadBytes)
            return ValueTask.CompletedTask;

        if (!_deduper.TryAccept(envelope.RequestSequence))
            return ValueTask.CompletedTask;

        NetTelemetry.FrameIn(_endpoint, envelope.Payload.Length);
        return _inbox.Writer.WriteAsync(envelope, ct);
    }

    /// <summary>
    /// Queues <paramref name="payload"/> without waiting. A reliable frame that does not fit closes this connection.
    /// </summary>
    public Task SendAsync(
        ushort opcode, byte[] payload, uint requestSequence = 0, CancellationToken ct = default, long mailboxAtTimestamp = 0)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled(ct);

        if (!TryEnvelope(opcode, payload, requestSequence, out PacketEnvelope envelope))
            return Task.CompletedTask;
        if (mailboxAtTimestamp > 0)
            envelope.MailboxQueuedAtTimestamp = mailboxAtTimestamp;

        Admit(envelope, reliableOverflowCloses: true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Non-blocking enqueue. Movement coalesces, visuals may drop, and a reliable frame that does not fit
    /// closes this connection. The caller never waits on the mailbox or the socket.
    /// </summary>
    public bool TrySend(ushort opcode, byte[] payload, uint requestSequence = 0, long mailboxAtTimestamp = 0)
    {
        if (!TryEnvelope(opcode, payload, requestSequence, out PacketEnvelope envelope))
            return false;
        if (mailboxAtTimestamp > 0)
            envelope.MailboxQueuedAtTimestamp = mailboxAtTimestamp;

        return Admit(envelope, reliableOverflowCloses: true) == MailboxAdmit.Accepted;
    }

    /// <summary>
    /// Stripe path: queue every frame in order and return. Does not wait for room.
    /// A reliable frame that does not fit closes this connection; the stripe worker moves on.
    /// One wake covers the whole batch.
    /// </summary>
    internal void SubmitOrderedBatch(List<OutboundFrame> frames)
    {
        if (frames.Count == 0)
            return;

        if (Volatile.Read(ref _closed) != 0)
        {
            NetHostLog.Error(
                $"connectionId={ConnectionId} endpoint={_endpoint} rejected {frames.Count} ordered frames: connection is closed");
            return;
        }

        var envelopes = new List<PacketEnvelope>(frames.Count);
        Collect(frames, envelopes);
        if (envelopes.Count == 0)
            return;

        MailboxAdmit admit = _mailbox.TryAdmitBatch(envelopes, forceWholeBatch: false);
        if (admit == MailboxAdmit.Accepted)
            WakePump();
        else if (admit == MailboxAdmit.Rejected)
        {
            NetTelemetry.ReliableOverflow(_endpoint);
            FailOutbound(
                $"batch of {envelopes.Count} frames does not fit under the outbound hard limit ({_mailbox.HardBytes} bytes)");
        }
    }

    /// <summary>Knownlist / appearance burst. Merges into the pending snapshot and returns. Never waits.</summary>
    internal void SubmitSnapshot(List<OutboundFrame> frames)
    {
        if (frames.Count == 0)
            return;

        if (Volatile.Read(ref _closed) != 0)
        {
            NetHostLog.Error(
                $"connectionId={ConnectionId} endpoint={_endpoint} rejected knownlist snapshot of {frames.Count} frames: connection is closed");
            return;
        }

        var envelopes = new List<PacketEnvelope>(frames.Count);
        Collect(frames, envelopes);
        if (envelopes.Count == 0)
            return;

        MailboxAdmit admit = _mailbox.TryMergeSnapshot(envelopes);
        if (admit == MailboxAdmit.Accepted)
            WakePump();
        else if (admit == MailboxAdmit.Rejected)
        {
            NetTelemetry.ReliableOverflow(_endpoint);
            FailOutbound(
                $"knownlist snapshot of {envelopes.Count} frames does not fit under the outbound hard limit ({_mailbox.HardBytes} bytes)");
        }
    }

    /// <summary>
    /// The outbound budget for this connection is exhausted, or the stripe could not take a reliable batch.
    /// Close the socket and keep going. The world tick must not wait on this client.
    /// </summary>
    internal void FailOutbound(string reason)
    {
        if (Interlocked.Exchange(ref _failed, 1) != 0)
            return;

        int queued = _mailbox.QueuedFrames;
        int held = _mailbox.HeldBytes;
        FineageTelemetry.OutboundSlowClientDisconnect.Add(1, new KeyValuePair<string, object?>("endpoint", _endpoint));
        NetHostLog.Error(
            $"slow_client_disconnect {Describe()}: {reason} (queuedFrames={queued} heldBytes={held} soft={_mailbox.SoftBytes} hard={_mailbox.HardBytes}). Closing the connection so a slow client cannot stall the world.");
        Volatile.Write(ref _closed, 1);
        _mailbox.Close();
        WakePump();
        try { _stream.Dispose(); } catch { /* the read loop and the pump observe the closed socket */ }
    }

    MailboxAdmit Admit(PacketEnvelope envelope, bool reliableOverflowCloses)
    {
        MailboxAdmit admit = _mailbox.TryAppend(envelope);
        if (admit == MailboxAdmit.Accepted)
        {
            WakePump();
            return admit;
        }

        if (admit == MailboxAdmit.Closed)
            return admit;

        NetTelemetry.OutboundDropped(_endpoint);
        if (admit == MailboxAdmit.Rejected)
        {
            NetTelemetry.ReliableOverflow(_endpoint);
            if (reliableOverflowCloses)
                FailOutbound($"opcode={envelope.Opcode} does not fit under the outbound hard limit ({_mailbox.HardBytes} bytes)");
        }

        return admit;
    }

    void Collect(List<OutboundFrame> frames, List<PacketEnvelope> envelopes)
    {
        for (int i = 0; i < frames.Count; i++)
        {
            if (!TryEnvelope(frames[i].Opcode, frames[i].Payload, 0, out PacketEnvelope envelope))
                continue;
            if (frames[i].MailboxAtTimestamp > 0)
                envelope.MailboxQueuedAtTimestamp = frames[i].MailboxAtTimestamp;
            envelope.FanoutEnqueuedAtTimestamp = frames[i].FanoutEnqueuedAtTimestamp;
            envelopes.Add(envelope);
        }
    }

    void RecordSocketWrite(long startedAt, int bytes, int frames)
    {
        double ms = (Stopwatch.GetTimestamp() - startedAt) * 1000.0 / Stopwatch.Frequency;
        var endpoint = new KeyValuePair<string, object?>("endpoint", _endpoint);
        FineageTelemetry.SocketWriteMilliseconds.Record(ms, endpoint);
        FineageTelemetry.SocketWriteBytes.Record(bytes, endpoint);
        FineageTelemetry.SocketWriteFrames.Record(frames, endpoint);
    }

    /// <summary>Age of the oldest command-batch frame in this write. Zero stamps are unordered fan-out.</summary>
    void RecordOutboundQueue(IReadOnlyList<PacketEnvelope> writing)
    {
        long oldest = 0;
        long oldestFanout = 0;
        for (int i = 0; i < writing.Count; i++)
        {
            long stamp = writing[i].MailboxQueuedAtTimestamp;
            if (stamp > 0 && (oldest == 0 || stamp < oldest))
                oldest = stamp;
            long fanout = writing[i].FanoutEnqueuedAtTimestamp;
            if (fanout > 0 && (oldestFanout == 0 || fanout < oldestFanout))
                oldestFanout = fanout;
        }

        long now = Stopwatch.GetTimestamp();
        if (oldestFanout > 0)
            FineageTelemetry.FanoutEnqueueToSocketWriteMilliseconds.Record(
                Math.Max(0, (now - oldestFanout) * 1000.0 / Stopwatch.Frequency));
        if (oldest <= 0)
            return;

        double ageMs = Math.Max(0, (now - oldest) * 1000.0 / Stopwatch.Frequency);
        var endpoint = new KeyValuePair<string, object?>("endpoint", _endpoint);
        FineageTelemetry.OutboundQueueAgeMilliseconds.Record(ageMs, endpoint);
        FineageTelemetry.CommandTimelineMilliseconds.Record(
            ageMs, new KeyValuePair<string, object?>("phase", "mailbox_to_socket_write"));
    }

    /// <summary>
    /// An oversized payload cannot be framed, and letting <see cref="NetFraming"/> throw inside the pump would
    /// tear down the whole connection. Refuse it here and count it like any other dropped frame.
    /// </summary>
    bool TryEnvelope(ushort opcode, byte[] payload, uint requestSequence, out PacketEnvelope envelope)
    {
        byte[] body = payload ?? Array.Empty<byte>();
        if (body.Length > PacketEnvelope.MaxPayloadBytes)
        {
            NetTelemetry.OutboundDropped(_endpoint);
            NetHostLog.Error(
                $"connectionId={ConnectionId} endpoint={_endpoint} rejected opcode={opcode}: payload length {body.Length} exceeds {PacketEnvelope.MaxPayloadBytes}");
            envelope = null!;
            return false;
        }

        envelope = new PacketEnvelope
        {
            Opcode = opcode,
            Flags = (ushort)PacketFlags.Reliable,
            RequestSequence = requestSequence,
            Payload = body
        };
        return true;
    }

    async Task PumpAsync()
    {
        // Level-triggered. The previous loop waited on a capacity-1 wake, drained that edge, then wrote.
        // A producer blocked in Monitor.Wait called the wake while still holding the mailbox lock; the pump
        // could consume the edge, block on that same lock, and then find nothing new to write. After PulseAll
        // the producer enqueued the frame that had been waiting, and no later wake was posted if every
        // producer was already inside Wait and the simulation thread was stuck on the stripe WriteAsync.
        // The socket engine then sat in WaitForSocketEvents with a full mailbox. Taking until the mailbox
        // is empty, and only then waiting, makes a dropped edge harmless: the next wake or the next loop
        // observes the queued frames.
        var combined = new List<PacketEnvelope>(MaxFramesPerBatch);
        try
        {
            while (true)
            {
                combined.Clear();
                MailboxTaken taken = _mailbox.Take(combined);
                if (taken.Frames == 0)
                {
                    if (Volatile.Read(ref _closed) != 0)
                        break;
                    if (!await _pumpWake.Reader.WaitToReadAsync().ConfigureAwait(false))
                        break;
                    while (_pumpWake.Reader.TryRead(out _))
                    {
                    }

                    continue;
                }

                await WriteCombinedAsync(combined).ConfigureAwait(false);
                _mailbox.ReleaseInflight(taken.Frames, taken.Bytes);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidOperationException)
        {
            if (ex is IOException or SocketException)
                NetDisconnectDiagnostics.Log("pump", ConnectionId, _endpoint, ex, _mailbox.QueuedFrames);
        }
        finally
        {
            try
            {
                combined.Clear();
                MailboxTaken tail = _mailbox.Take(combined);
                if (tail.Frames > 0)
                {
                    await WriteCombinedAsync(combined).ConfigureAwait(false);
                    _mailbox.ReleaseInflight(tail.Frames, tail.Bytes);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }

    async Task WriteCombinedAsync(List<PacketEnvelope> frames)
    {
        if (frames.Count == 0)
            return;

        // A taken snapshot can be larger than one socket write. Split at the frame and byte bounds.
        int offset = 0;
        while (offset < frames.Count)
        {
            int count = 0;
            int bytes = 0;
            while (offset + count < frames.Count)
            {
                int size = PacketEnvelope.HeaderSize + frames[offset + count].Payload.Length;
                if (count > 0 && (count >= MaxFramesPerBatch || bytes + size > MaxBytesPerBatch))
                    break;
                bytes += size;
                count++;
            }

            var slice = new PacketEnvelope[count];
            for (int i = 0; i < count; i++)
                slice[i] = frames[offset + i];

            using var writeActivity = FineageTelemetry.Activity.StartActivity("zone.socket.write");
            RecordOutboundQueue(slice);
            long started = Stopwatch.GetTimestamp();
            await NetFraming.WriteManyAsync(_stream, slice, CancellationToken.None).ConfigureAwait(false);
            RecordSocketWrite(started, bytes, count);
            for (int i = 0; i < count; i++)
                NetTelemetry.FrameOut(_endpoint, slice[i].Payload.Length);
            offset += count;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _failed, 1);
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        Volatile.Write(ref _boundGeneration, 0);
        Volatile.Write(ref _boundObjectId, 0);
        // Stop producers, then wake the pump so it writes whatever is still in the mailbox.
        _mailbox.Close();
        WakePump();
        try
        {
            await _pump.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException or ObjectDisposedException)
        {
        }

        try { _stream.Dispose(); } catch { /* ignore */ }
        try { _client.Dispose(); } catch { /* ignore */ }
    }
}
