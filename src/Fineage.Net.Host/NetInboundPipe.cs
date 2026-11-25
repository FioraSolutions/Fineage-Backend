using System.Buffers;
using System.IO.Pipelines;
using Fineage.Net.Contract;

namespace Fineage.Net.Host;

/// <summary>
/// Inbound framing for one connection. The stream is read into a pipe so a partial header or body stays buffered
/// and several frames in one read are all delivered. <see cref="PipeReader.AdvanceTo(SequencePosition, SequencePosition)"/>
/// consumes only bytes through the last complete frame and examines everything looked at.
/// </summary>
public static class NetInboundPipe
{
    public static Task ReadFramesAsync(
        Stream stream,
        Func<PacketEnvelope, CancellationToken, ValueTask> onFrame,
        CancellationToken ct)
        => ReadFramesAsync(stream, onFrame, ct, bufferSize: -1, minimumReadSize: -1);

    /// <param name="bufferSize">Pipe segment size. <c>-1</c> is the pipe default. Tests pass <c>1</c> so each read yields one byte.</param>
    /// <param name="minimumReadSize">Bytes the pipe gathers before returning. <c>-1</c> is the pipe default.</param>
    public static async Task ReadFramesAsync(
        Stream stream,
        Func<PacketEnvelope, CancellationToken, ValueTask> onFrame,
        CancellationToken ct,
        int bufferSize,
        int minimumReadSize)
    {
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(
            bufferSize: bufferSize,
            minimumReadSize: minimumReadSize,
            leaveOpen: true));
        try
        {
            await ReadFramesAsync(reader, onFrame, ct).ConfigureAwait(false);
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
        }
    }

    public static async Task ReadFramesAsync(
        PipeReader reader,
        Func<PacketEnvelope, CancellationToken, ValueTask> onFrame,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ReadResult result = await reader.ReadAsync(ct).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;
            try
            {
                string? error = null;
                while (NetFraming.TryReadFrame(buffer, out PacketEnvelope? envelope, out error, out SequencePosition end))
                {
                    buffer = buffer.Slice(end);
                    await onFrame(envelope!, ct).ConfigureAwait(false);
                }

                if (error != null)
                    throw new InvalidOperationException(error);
            }
            finally
            {
                // Unparsed bytes start at buffer.Start. Examining through buffer.End waits for more data
                // instead of delivering the same partial frame again, and does not discard it.
                reader.AdvanceTo(buffer.Start, buffer.End);
            }

            if (result.IsCompleted || result.IsCanceled)
                break;
        }
    }
}
