using System;

namespace Fineage.Net.Contract
{
    public static partial class NetCodec
    {
        public static byte[] EncodeBypassUserCmd(BypassUserCmdNetMsg m) => Write(w => w.WriteInt32(m.CommandId));

        public static BypassUserCmdNetMsg DecodeBypassUserCmd(ReadOnlySpan<byte> p) =>
            new() { CommandId = new Reader(p).ReadInt32() };
    }
}
