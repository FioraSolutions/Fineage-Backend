using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Protocol.V2;
using MessagePack;

namespace Fineage.Zone;

public sealed class ZoneSession
{
    readonly Stream? _stream;
    readonly PipelineConnection? _pipeline;
    readonly SemaphoreSlim _send = new(1, 1);

    public ZoneSession(Stream stream) => _stream = stream;

    public ZoneSession(PipelineConnection pipeline) => _pipeline = pipeline;

    public int ObjectId { get; private set; }
    public string Account { get; private set; } = "";
    public string Name { get; private set; } = "";
    public int ClassId { get; private set; }
    public int WireSessionId { get; set; }
    public ushort LastPacketId { get; set; }
    internal bool FailureRecorded { get; set; }
    public bool InWorld { get; internal set; }
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public int Z { get; internal set; }
    public int SelectedTargetId { get; set; }

    /// <summary>Interlude-style auto attack: target kept swinging at until it dies or the player stops.</summary>
    public int AutoAttackTargetId { get; set; }
    public long NextAutoAttackUnixMs { get; set; }

    /// <summary>Unix milliseconds of the last accepted say. 0 means this session has not spoken.</summary>
    public long LastChatUnixMs { get; set; }

    public bool Bound => ObjectId != 0;

    public void Bind(int objectId, string account, string name, int classId)
    {
        ObjectId = objectId;
        Account = account;
        Name = name;
        ClassId = classId;
        InWorld = false;
    }

    internal void NoteForLog(string account)
    {
        if (!string.IsNullOrEmpty(account))
            Account = account;
    }

    public void Clear()
    {
        ObjectId = 0;
        Account = "";
        Name = "";
        ClassId = 0;
        InWorld = false;
        SelectedTargetId = 0;
        AutoAttackTargetId = 0;
        NextAutoAttackUnixMs = 0;
        LastChatUnixMs = 0;
    }

    public async Task SendAsync<T>(Fineage.Protocol.MsgType type, T message, CancellationToken ct)
    {
        byte[] body = MessagePackSerializer.Serialize(message);
        await SendFrameAsync((ushort)type, body, ct).ConfigureAwait(false);
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
