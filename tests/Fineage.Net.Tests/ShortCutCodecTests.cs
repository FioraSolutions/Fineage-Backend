using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>Mobius ShortCutInit / ShortCutRegister / RequestShortCutReg / RequestShortCutDel payloads.</summary>
public class ShortCutCodecTests
{
    [Fact]
    public void Opcodes_are_distinct_and_after_user_info()
    {
        ushort[] ops =
        [
            NetOpcode.RequestShortCutReg, NetOpcode.RequestShortCutDel, NetOpcode.ShortCutInit,
            NetOpcode.ShortCutRegister, NetOpcode.ShortCutDelete
        ];
        Assert.Equal(ops.Length, ops.Distinct().Count());
        Assert.All(ops, op => Assert.True(op > NetOpcode.UserInfo));
        Assert.Equal((ushort)0x0358, NetOpcode.RequestShortCutReg);
        Assert.Equal((ushort)0x035C, NetOpcode.ShortCutDelete);
    }

    [Fact]
    public void Register_roundtrip()
    {
        var m = new ShortCutNetMsg { Type = 2, Slot = 13, Id = 1177, Level = 3 };
        var back = NetCodec.DecodeShortCut(NetCodec.EncodeShortCut(m));
        Assert.Equal(2, back.Type);
        Assert.Equal(13, back.Slot);
        Assert.Equal(1177, back.Id);
        Assert.Equal(3, back.Level);
    }

    [Fact]
    public void Init_roundtrip_and_empty()
    {
        var m = new ShortCutInitNetMsg
        {
            Shortcuts =
            [
                new ShortCutNetMsg { Type = 1, Slot = 0, Id = 268_435_500, Level = 0 },
                new ShortCutNetMsg { Type = 2, Slot = 1, Id = 1177, Level = 1 },
                new ShortCutNetMsg { Type = 3, Slot = 59, Id = 2, Level = 0 }
            ]
        };
        var back = NetCodec.DecodeShortCutInit(NetCodec.EncodeShortCutInit(m));
        Assert.Equal(3, back.Shortcuts.Length);
        Assert.Equal(268_435_500, back.Shortcuts[0].Id);
        Assert.Equal(59, back.Shortcuts[2].Slot);
        Assert.Empty(NetCodec.DecodeShortCutInit(NetCodec.EncodeShortCutInit(new ShortCutInitNetMsg())).Shortcuts);
    }

    [Fact]
    public void Del_roundtrip()
    {
        Assert.Equal(47, NetCodec.DecodeShortCutDel(NetCodec.EncodeShortCutDel(new ShortCutDelNetMsg { Slot = 47 })).Slot);
    }
}
