using System.IO.Pipelines;
using System.Threading.Channels;
using Fineage.Protocol.Telemetry;

namespace Fineage.Protocol.Net;

/// <summary>
/// One accepted socket. Inbound bytes land in a <see cref="PipeReader"/>.
/// Outbound frames sit in a bounded channel so a slow client cannot grow memory without limit.
/// </summary>
public sealed class PipelineConnection : IAsyncDisposable
{
    readonly Stream _stream;
    readonly PipeWriter _writer;
    readonly Channel<byte[]> _outbound;
    readonly Task _pump;
    readonly string _endpoint;
    int _closed;

    public PipelineConnection(int id, string remote, Stream stream, int outboundCapacity, string endpoint, CancellationToken ct)
    {
        Id = id;
        Remote = remote;
        _endpoint = endpoint;
        _stream = stream;
        Reader = PipeReader.Create(stream, new StreamPipeReaderOptions(
            bufferSize: 16 * 1024,
            minimumReadSize: 1024,
            leaveOpen: true));
        _writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(leaveOpen: true));
        _outbound = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(Math.Max(8, outboundCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        _pump = PumpAsync(ct);
    }

    public int Id { get; }
    public string Remote { get; }
    public PipeReader Reader { get; }

    public async ValueTask SendAsync(byte[] frame, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await _outbound.Writer.WriteAsync(frame, timeout.Token).ConfigureAwait(false);
    }

    public bool TrySend(byte[] frame)
    {
        if (_outbound.Writer.TryWrite(frame))
            return true;

        FineageTelemetry.OutboundDropped.Add(1, new KeyValuePair<string, object?>("endpoint", _endpoint));
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        _outbound.Writer.TryComplete();
        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
        }

        await Reader.CompleteAsync().ConfigureAwait(false);
        await _writer.CompleteAsync().ConfigureAwait(false);
    }

    async Task PumpAsync(CancellationToken ct)
    {
        try
        {
            await foreach (byte[] frame in _outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                PipelineTelemetry.FrameOut(_endpoint);
                FlushResult flush = await _writer.WriteAsync(frame, ct).ConfigureAwait(false);
                if (flush.IsCompleted)
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }
}
