using System;
using System.Buffers.Binary;

namespace Fineage.Net.Contract;

/// <summary>
/// The per-character inventory cursor is one little-endian int32 after the
/// <see cref="NetCodec.EncodeItemList"/> / <see cref="NetCodec.EncodeInventoryUpdate"/> body.
/// Those encoders do not write it. A client that re-encodes the decoded message therefore
/// sees exactly four extra bytes, which is how it reads the version (FioraSolutions/Fineage#1667).
/// Zero extra bytes means the packet is unversioned. Any other tail length is not a version.
/// </summary>
public static class InventoryVersionTail
{
    public static byte[] Append(byte[] body, int version)
    {
        if (body == null)
            throw new ArgumentNullException(nameof(body));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), version, "An inventory version on the wire is at least 1.");

        var payload = new byte[body.Length + 4];
        body.CopyTo(payload, 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(body.Length), version);
        return payload;
    }
}
