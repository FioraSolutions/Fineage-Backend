namespace Fineage.Login;

static class LoginLog
{
    const string Category = "Fineage.Login";

    public static void Info(string message) =>
        FineageLogging.Write(Category, "Information", "log", ("message", message));

    public static void Connect(LoginSession session) =>
        FineageLogging.Write(Category, "Information", "Connect",
            ("session", session.WireSessionId),
            ("account", session.AccountName),
            ("character", session.CharacterName),
            ("packet", 0),
            ("remote", session.Remote));

    public static void Disconnect(LoginSession session) =>
        FineageLogging.Write(Category, "Information", "Disconnect",
            ("session", session.WireSessionId),
            ("account", session.AccountName),
            ("character", session.CharacterName),
            ("packet", session.LastPacketId));

    public static void EnterWorld(LoginSession session) =>
        FineageLogging.Write(Category, "Information", "EnterWorld",
            ("session", session.WireSessionId),
            ("account", session.AccountName),
            ("character", session.CharacterName),
            ("packet", session.LastPacketId));

    public static void Packet(LoginSession session, string code, bool rejected) =>
        FineageLogging.Write(Category, rejected ? "Warning" : "Error", rejected ? "PacketRejected" : "PacketError",
            ("session", session.WireSessionId),
            ("account", session.AccountName),
            ("character", session.CharacterName),
            ("packet", session.LastPacketId),
            ("code", code));

    public static void Fault(LoginSession session, Exception ex) =>
        FineageLogging.Write(Category, "Error", "PacketError",
            ("session", session.WireSessionId),
            ("account", session.AccountName),
            ("character", session.CharacterName),
            ("packet", session.LastPacketId),
            ("code", ex.GetType().Name));
}
