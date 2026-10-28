namespace Fineage.Zone.NpcHtml;

/// <summary>
/// Splits a Mobius bypass string. The client may send <c>npc_&lt;objectId&gt;_Chat 1</c>
/// or the command alone when <see cref="Fineage.Protocol.BypassRequestMsg.NpcObjectId"/> is set.
/// A leading <c>bypass -h</c> is stripped. The two object ids must agree when both are present.
/// </summary>
public static class BypassCommands
{
    public const int MaxLength = 512;

    public static bool TrySplit(int npcObjectId, string? raw, out int resolvedId, out string command)
    {
        resolvedId = 0;
        command = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var text = raw.Trim();
        if (text.Length > MaxLength || HasControl(text))
            return false;

        if (text.StartsWith("bypass -h ", StringComparison.OrdinalIgnoreCase))
            text = text["bypass -h ".Length..].TrimStart();
        else if (text.StartsWith("bypass ", StringComparison.OrdinalIgnoreCase))
            text = text["bypass ".Length..].TrimStart();

        if (text.StartsWith("npc_", StringComparison.OrdinalIgnoreCase))
        {
            int split = text.IndexOf('_', 4);
            if (split <= 4)
                return false;
            if (!int.TryParse(text[4..split], out int parsed) || parsed <= 0)
                return false;
            if (npcObjectId != 0 && npcObjectId != parsed)
                return false;
            var rest = text[(split + 1)..].Trim();
            if (rest.Length == 0 || HasControl(rest))
                return false;
            resolvedId = parsed;
            command = rest;
            return true;
        }

        if (npcObjectId <= 0 || text.Length == 0)
            return false;
        resolvedId = npcObjectId;
        command = text;
        return true;
    }

    public static string Token(string command)
    {
        command = command.Trim();
        int space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }

    static bool HasControl(string text)
    {
        foreach (char c in text)
        {
            if (char.IsControl(c))
                return true;
        }

        return false;
    }
}
