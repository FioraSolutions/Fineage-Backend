namespace Fineage.Zone.Net;

/// <summary>
/// Write-only sink for the V2 wire that a Net session still emits alongside Net fanout
/// (<see cref="ZoneWorld"/> builds V2 messages, <see cref="NetPresenceHub"/> is the
/// client-facing path). A <see cref="MemoryStream"/> used to stand in for this, which kept
/// every frame of a long session in memory; this sink drops them instead.
/// </summary>
sealed class DiscardingStream : Stream
{
    public long DroppedBytes { get; private set; }

    public long DroppedFrames { get; private set; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => 0;

    public override long Position
    {
        get => 0;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override void Write(byte[] buffer, int offset, int count) => Note(count);

    public override void Write(ReadOnlySpan<byte> buffer) => Note(buffer.Length);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Note(count);
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Note(buffer.Length);
        return ValueTask.CompletedTask;
    }

    void Note(int count)
    {
        DroppedFrames++;
        DroppedBytes += count;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
