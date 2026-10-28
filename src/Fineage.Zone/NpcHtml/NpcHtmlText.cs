namespace Fineage.Zone.NpcHtml;

/// <summary>
/// Substitutes the tokens Mobius writes into an NPC HTML window before send.
/// <c>Npc.showChatWindow</c> replaces <c>%objectId%</c> and <c>%npcname%</c>.
/// <c>%playername%</c> is the same token warehouse and quest pages use.
/// Replacement is literal, not a regular expression.
/// </summary>
public static class NpcHtmlText
{
    public const string NothingToSay = "<html><body>I have nothing to say to you.</body></html>";

    public readonly record struct Tokens(int ObjectId, int NpcId, string NpcName, string PlayerName);

    public static string Apply(string? html, Tokens tokens)
    {
        if (string.IsNullOrEmpty(html))
            return "";

        return html
            .Replace("%objectId%", tokens.ObjectId.ToString(), StringComparison.Ordinal)
            .Replace("%npcId%", tokens.NpcId.ToString(), StringComparison.Ordinal)
            .Replace("%npcname%", tokens.NpcName ?? "", StringComparison.Ordinal)
            .Replace("%playername%", tokens.PlayerName ?? "", StringComparison.Ordinal)
            .Replace("%player_name%", tokens.PlayerName ?? "", StringComparison.Ordinal)
            .Replace("%festivalMins%", "0", StringComparison.Ordinal);
    }
}
