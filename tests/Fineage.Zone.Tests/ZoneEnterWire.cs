using System.IO;
using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Enter-world ordering after SkillList slice (#137).</summary>
internal static class ZoneEnterWire
{
    public static async Task ReadSkillListAsync(Stream stream)
    {
        var frame = await FrameIO.ReadAsync(stream);
        Assert.NotNull(frame);
        Assert.Equal((ushort)MsgType.SkillList, frame!.Value.Type);
        _ = MessagePackSerializer.Deserialize<SkillListMsg>(frame.Value.Body);
    }

    public static async Task ReadAfterItemListAsync(Stream stream)
    {
        await ReadSkillListAsync(stream);
        var spawn = await FrameIO.ReadAsync(stream);
        Assert.NotNull(spawn);
        Assert.Equal((ushort)MsgType.PlayerSpawned, spawn!.Value.Type);
    }
}
