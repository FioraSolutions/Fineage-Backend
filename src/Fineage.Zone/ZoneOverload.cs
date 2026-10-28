using Fineage.Protocol;
using Fineage.Protocol.V2;

namespace Fineage.Zone;

static class ZoneOverload
{
    public static async ValueTask RejectAsync(Stream stream)
    {
        try
        {
            byte[] body = V2Codec.Serialize(new Protocol.ErrorMsg
            {
                Code = "server_busy",
                Message = "Zone is at capacity."
            });
            await FrameIO.SendAsync(stream, (ushort)Protocol.MsgType.Error, body, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }
}
