using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class PetitionCodecTests
{
    [Fact]
    public void PetitionRequest_roundtrips()
    {
        var original = new PetitionRequestNetMsg { Content = "help", Type = 3 };
        var decoded = NetCodec.DecodePetitionRequest(NetCodec.EncodePetitionRequest(original));
        Assert.Equal("help", decoded.Content);
        Assert.Equal(3, decoded.Type);
    }

    [Fact]
    public void PetitionCancel_encodes_empty_body()
    {
        var bytes = NetCodec.EncodePetitionCancel(new PetitionCancelNetMsg());
        Assert.Empty(bytes);
        _ = NetCodec.DecodePetitionCancel(bytes);
    }
}
