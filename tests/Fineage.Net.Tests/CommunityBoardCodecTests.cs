using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class CommunityBoardCodecTests
{
    [Fact]
    public void ShowBoard_roundtrips_content_and_show_flag()
    {
        var original = new ShowBoardNetMsg
        {
            Show = 1,
            Content = "101\u0008<html><body>hi</body></html>"
        };
        var decoded = NetCodec.DecodeShowBoard(NetCodec.EncodeShowBoard(original));
        Assert.Equal(original.Show, decoded.Show);
        Assert.Equal(original.Content, decoded.Content);
    }

    [Fact]
    public void BBSwrite_roundtrips_six_strings()
    {
        var original = new BBSwriteNetMsg
        {
            Url = "Topic",
            Arg1 = "a1",
            Arg2 = "a2",
            Arg3 = "a3",
            Arg4 = "a4",
            Arg5 = "a5"
        };
        var decoded = NetCodec.DecodeBBSwrite(NetCodec.EncodeBBSwrite(original));
        Assert.Equal(original.Url, decoded.Url);
        Assert.Equal(original.Arg1, decoded.Arg1);
        Assert.Equal(original.Arg5, decoded.Arg5);
    }

    [Fact]
    public void ShowBoardRequest_roundtrips_unused_int()
    {
        var original = new ShowBoardRequestNetMsg { Unused = 7 };
        var decoded = NetCodec.DecodeShowBoardRequest(NetCodec.EncodeShowBoardRequest(original));
        Assert.Equal(7, decoded.Unused);
    }
}
