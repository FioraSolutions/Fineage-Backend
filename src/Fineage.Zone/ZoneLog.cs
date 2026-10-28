using Fineage.Login;

namespace Fineage.Zone;

static class ZoneLog
{
    const string Category = "Fineage.Zone";

    public static void Info(string message) =>
        FineageLogging.Write(Category, "Information", "log", ("message", message));

    public static void Connect(int sessionId, string remote) =>
        FineageLogging.Write(Category, "Information", "Connect",
            ("session", sessionId),
            ("account", ""),
            ("character", ""),
            ("packet", 0),
            ("remote", remote));

    public static void Disconnect(ZoneSession session) =>
        FineageLogging.Write(Category, "Information", "Disconnect",
            ("session", session.WireSessionId),
            ("account", session.Account),
            ("character", session.Name),
            ("packet", session.LastPacketId));

    public static void EnterWorld(ZoneSession session, int known) =>
        FineageLogging.Write(Category, "Information", "EnterWorld",
            ("session", session.WireSessionId),
            ("account", session.Account),
            ("character", session.Name),
            ("packet", (int)Protocol.MsgType.EnterWorld),
            ("object", session.ObjectId),
            ("known", known));

    public static void LeaveWorld(ZoneSession session) =>
        FineageLogging.Write(Category, "Information", "LeaveWorld",
            ("session", session.WireSessionId),
            ("account", session.Account),
            ("character", session.Name),
            ("packet", session.LastPacketId));

    public static void Packet(ZoneSession session, string code, bool rejected) =>
        FineageLogging.Write(Category, rejected ? "Warning" : "Error", rejected ? "PacketRejected" : "PacketError",
            ("session", session.WireSessionId),
            ("account", session.Account),
            ("character", session.Name),
            ("packet", session.LastPacketId),
            ("code", code));

    public static void Fault(ZoneSession session, ushort packet, Exception ex) =>
        FineageLogging.Write(Category, "Error", "PacketError",
            ("session", session.WireSessionId),
            ("account", session.Account),
            ("character", session.Name),
            ("packet", packet),
            ("code", ex.GetType().Name));
}
