using Fineage.Zone.Net;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// A Net session still runs <see cref="ZoneWorld"/> code that emits V2 frames. Those frames have
/// no reader, so <see cref="NetZoneServer"/> hands <see cref="ZoneSession"/> a sink that drops
/// them. A <see cref="MemoryStream"/> did the job before, but it kept every byte of a long
/// session alive.
/// </summary>
public class DiscardingStreamTests
{
    [Fact]
    public void Writes_are_counted_and_not_buffered()
    {
        var sink = new DiscardingStream();
        byte[] frame = new byte[512];

        sink.Write(frame, 0, frame.Length);
        sink.Write(frame, 0, frame.Length);

        Assert.Equal(2, sink.DroppedFrames);
        Assert.Equal(1024, sink.DroppedBytes);
        Assert.Equal(0, sink.Length);
        Assert.Equal(0, sink.Position);
    }

    [Fact]
    public async Task Async_writes_are_dropped_too()
    {
        var sink = new DiscardingStream();

        await sink.WriteAsync(new byte[16], 0, 16, CancellationToken.None);
        await sink.WriteAsync(new byte[8], CancellationToken.None);
        await sink.FlushAsync(CancellationToken.None);

        Assert.Equal(2, sink.DroppedFrames);
        Assert.Equal(24, sink.DroppedBytes);
    }

    [Fact]
    public void Sink_is_write_only()
    {
        var sink = new DiscardingStream();

        Assert.True(sink.CanWrite);
        Assert.False(sink.CanRead);
        Assert.False(sink.CanSeek);
        Assert.Throws<NotSupportedException>(() => sink.Read(new byte[1], 0, 1));
        Assert.Throws<NotSupportedException>(() => sink.Seek(0, SeekOrigin.Begin));
    }
}
