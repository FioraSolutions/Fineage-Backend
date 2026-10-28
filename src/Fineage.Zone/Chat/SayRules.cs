using Fineage.Protocol;

namespace Fineage.Zone.Chat;

/// <summary>
/// Say text checks and who should hear a line. Delivery itself lives on <c>ZoneWorld</c>.
/// </summary>
public static class SayRules
{
    public static bool TryNormalize(string? raw, out string text)
    {
        text = "";
        if (string.IsNullOrEmpty(raw) || raw.Length > ChatProtocol.MaxTextLength || HasControl(raw))
            return false;

        string trimmed = raw.Trim();
        if (trimmed.Length == 0 || trimmed.Length > ChatProtocol.MaxTextLength || HasControl(trimmed))
            return false;

        text = trimmed;
        return true;
    }

    public static bool IsDelivered(int chatType) =>
        chatType == ChatProtocol.All || chatType == ChatProtocol.Shout;

    public static bool IsFlood(long lastUnixMs, long nowUnixMs) =>
        lastUnixMs > 0 && nowUnixMs - lastUnixMs < ChatProtocol.FloodIntervalMs;

    public static bool InLocalRange(int x1, int y1, int x2, int y2)
    {
        long dx = x1 - (long)x2;
        long dy = y1 - (long)y2;
        long range = ChatProtocol.LocalRange;
        return dx * dx + dy * dy <= range * range;
    }

    public static SystemMessageMsg Welcome(string name)
    {
        string who = name ?? "";
        return new SystemMessageMsg
        {
            Id = ChatProtocol.Welcome,
            Params = [who],
            Text = $"Welcome, {who}."
        };
    }

    public static SystemMessageMsg NotSupported(int chatType) => new()
    {
        Id = ChatProtocol.ChatNotSupported,
        Params = [chatType.ToString()],
        Text = "not supported yet"
    };

    public static SystemMessageMsg Flood() => new()
    {
        Id = ChatProtocol.ChatFlood,
        Params = [],
        Text = "Please do not spam the chat."
    };

    static bool HasControl(string text)
    {
        foreach (char c in text)
        {
            if (c < ' ' || c == '\u007f')
                return true;
        }

        return false;
    }
}
