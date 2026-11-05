using System.Net.Sockets;
using System.Threading.Channels;
using Fineage.Net.Contract;

namespace Fineage.Net.Host;

public sealed class NetConnection : IAsyncDisposable
{
    readonly TcpClient _client;
    readonly NetworkStream _stream;
    readonly Channel<PacketEnvelope> _inbox;
    readonly SemaphoreSlim _send = new(1, 1);
    readonly CommandDeduper _deduper = new();
    readonly string _endpoint;

    public NetConnection(TcpClient client, int inboxCapacity, string endpoint = "net")
    {
        _client = client;
        _endpoint = endpoint;
        _stream = client.GetStream();
        _inbox = Channel.CreateBounded<PacketEnvelope>(new BoundedChannelOptions(inboxCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<PacketEnvelope> Inbox => _inbox.Reader;
    public bool HelloCompleted { get; set; }
    public uint FeatureFlags { get; set; }

    public async Task RunReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                PacketEnvelope? envelope;
                try
                {
                    envelope = await NetFraming.ReadAsync(_stream, ct).ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                if (envelope == null)
                    break;

                if (envelope.Major != PacketEnvelope.ProtocolMajor)
                    continue;

                if (envelope.Payload.Length > PacketEnvelope.MaxPayloadBytes)
                    continue;

                if (!_deduper.TryAccept(envelope.RequestSequence))
                    continue;

                NetTelemetry.FrameIn(_endpoint, envelope.Payload.Length);

                await _inbox.Writer.WriteAsync(envelope, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            _inbox.Writer.TryComplete();
        }
    }

    public async Task SendAsync(ushort opcode, byte[] payload, uint requestSequence = 0, CancellationToken ct = default)
    {
        var envelope = new PacketEnvelope
        {
            Opcode = opcode,
            Flags = (ushort)PacketFlags.Reliable,
            RequestSequence = requestSequence,
            Payload = payload ?? Array.Empty<byte>()
        };
        await _send.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await NetFraming.WriteAsync(_stream, envelope, ct).ConfigureAwait(false);
            NetTelemetry.FrameOut(_endpoint, envelope.Payload.Length);
        }
        finally
        {
            _send.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        try { _stream.Dispose(); } catch { /* ignore */ }
        try { _client.Dispose(); } catch { /* ignore */ }
        _send.Dispose();
        return ValueTask.CompletedTask;
    }
}
