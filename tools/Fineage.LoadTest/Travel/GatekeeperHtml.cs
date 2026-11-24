using System.Text.RegularExpressions;

namespace Fineage.LoadTest.Travel;

public enum GatekeeperLinkKind
{
    Teleport,
    ShowTeleports,
    Chat
}

/// <summary>One bypass a travelling bot may follow on a gatekeeper page.</summary>
public readonly record struct GatekeeperLink(GatekeeperLinkKind Kind, string Command, string List, int Id, string Label);

/// <summary>Reads the links a player would click on a gatekeeper dialog (Mobius bypass syntax).</summary>
public static partial class GatekeeperHtml
{
    [GeneratedRegex("<a\\s[^>]*action=\"bypass(?:\\s+-h)?\\s+npc_(?:%objectId%|\\d+)_(?<cmd>[^\"]+)\"[^>]*>(?<label>.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Anchor();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    public static IReadOnlyList<GatekeeperLink> Links(string html)
    {
        var links = new List<GatekeeperLink>();
        foreach (Match m in Anchor().Matches(html))
        {
            string cmd = m.Groups["cmd"].Value.Trim();
            string label = Tag().Replace(m.Groups["label"].Value, "").Trim();
            string[] parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;
            string verb = parts[0];
            if (verb.Equals("teleport", StringComparison.OrdinalIgnoreCase) && parts.Length == 3 &&
                int.TryParse(parts[2], out int id))
                links.Add(new GatekeeperLink(GatekeeperLinkKind.Teleport, cmd, parts[1], id, label));
            else if (verb.Equals("showTeleports", StringComparison.OrdinalIgnoreCase) &&
                     (parts.Length == 1 || parts[1].Equals("NORMAL", StringComparison.OrdinalIgnoreCase)))
                links.Add(new GatekeeperLink(GatekeeperLinkKind.ShowTeleports, cmd, parts.Length > 1 ? parts[1] : "", 0, label));
            else if (verb.Equals("Chat", StringComparison.OrdinalIgnoreCase) && parts.Length == 2 &&
                     int.TryParse(parts[1], out int page))
                links.Add(new GatekeeperLink(GatekeeperLinkKind.Chat, cmd, "", page, label));
        }

        return links;
    }
}
