namespace Fineage.Net.Host;

/// <summary>One encoded frame waiting to be written on a connection, in audience order.</summary>
internal readonly record struct OutboundFrame(ushort Opcode, byte[] Payload, long MailboxAtTimestamp = 0);
