using Fineage.Net.Contract;
using Xunit;

namespace Fineage.Net.Tests;

public sealed class ClientSetTimeCodecTests
{
    [Fact]
    public void ClientSetTime_round_trips_mobius_layout()
    {
        var msg = new ClientSetTimeNetMsg { GameMinutes = 12_345, ClockSpeed = 6 };
        var back = NetCodec.DecodeClientSetTime(NetCodec.EncodeClientSetTime(msg));
        Assert.Equal(msg.GameMinutes, back.GameMinutes);
        Assert.Equal(6, back.ClockSpeed);
        Assert.Equal(NetOpcode.ClientSetTime, 0x03E1);
    }
}