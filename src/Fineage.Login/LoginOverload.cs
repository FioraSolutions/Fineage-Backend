using Fineage.Protocol;
using Fineage.Protocol.V2;

namespace Fineage.Login;

static class LoginOverload
{
    public static async ValueTask RejectAsync(Stream stream)
    {
        try
        {
            byte[] body = V2Codec.Serialize(new Protocol.ErrorMsg
            {
                Code = "server_busy",
                Message = "Login is at capacity."
            });
            await FrameIO.SendAsync(stream, (ushort)Protocol.MsgType.Error, body, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }
}
