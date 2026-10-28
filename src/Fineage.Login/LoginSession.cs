using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Protocol.V2;
using MessagePack;

namespace Fineage.Login;

public sealed class LoginSession
{
    readonly Stream? _stream;
    readonly PipelineConnection? _pipeline;
    readonly SemaphoreSlim _send = new(1, 1);

    public LoginSession(Stream stream) => _stream = stream;

    public LoginSession(PipelineConnection pipeline) => _pipeline = pipeline;

    public async Task<(ushort Type, byte[] Body)?> ReadAsync(CancellationToken ct)
    {
        if (_pipeline != null)
        {
            var frame = await PipeFrames.ReadV2Async(_pipeline.Reader, ct).ConfigureAwait(false);
            return frame == null ? null : (frame.Value.Type, frame.Value.Body);
        }

        return await FrameIO.ReadAsync(_stream!, ct).ConfigureAwait(false);
    }

    public Task SendAsync<T>(Protocol.MsgType type, T message, CancellationToken ct)
    {
        byte[] body = MessagePackSerializer.Serialize(message);
        return SendFrameAsync((ushort)type, body, ct);
    }

    public async Task SendFrameAsync(ushort type, byte[] body, CancellationToken ct)
    {
        await _send.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pipeline != null)
            {
                byte[] frame = V2Codec.Frame((Fineage.Protocol.V2.MsgType)type, body.AsSpan());
                await _pipeline.SendAsync(frame, ct).ConfigureAwait(false);
            }
            else
            {
                await FrameIO.SendAsync(_stream!, type, body, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _send.Release();
        }
    }
}
