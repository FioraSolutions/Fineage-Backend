namespace Fineage.Zone.NpcHtml;

/// <summary>
/// Picks the HTML file for an NPC type and page.
/// Mobius <c>Npc.getHtmlPath</c> uses <c>data/html/default/{id}.htm</c> for page 0
/// and <c>data/html/default/{id}-{page}.htm</c> otherwise, then <c>npcdefault.htm</c>.
/// Merchant, warehouse, teleporter, guard, trainer, fisherman, adventurer, pet manager,
/// and village master override that folder. A missing typed file falls back the same way.
/// </summary>
public static class NpcHtmlPaths
{
    public static string Folder(string? npcType)
    {
        if (string.IsNullOrWhiteSpace(npcType))
            return "default";
        if (npcType.StartsWith("VillageMaster", StringComparison.Ordinal))
            return "villagemaster";

        return npcType switch
        {
            "Merchant" => "merchant",
            "Fisherman" => "fisherman",
            "Warehouse" => "warehouse",
            "Teleporter" => "teleporter",
            "Guard" => "guard",
            "Trainer" => "trainer",
            "Adventurer" => "adventurer_guildsman",
            "PetManager" => "petmanager",
            "Doorman" or "CastleDoorman" or "ClanHallDoorman" => "doorman",
            "SignsPriest" or "DawnPriest" or "DuskPriest" => "seven_signs",
            _ => "default"
        };
    }

    public static string FileName(int npcId, int page) =>
        page <= 0 ? $"{npcId}.htm" : $"{npcId}-{page}.htm";

    /// <summary>Page body for <c>showChatWindow</c>. Missing files use the standard nothing-to-say page.</summary>
    public static string Read(NpcHtmlCatalog catalog, string? npcType, int npcId, int page)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string file = FileName(npcId, page);
        string folder = Folder(npcType);
        if (catalog.TryGet($"{folder}/{file}", out var html))
            return html;
        if (!string.Equals(folder, "default", StringComparison.Ordinal) &&
            catalog.TryGet($"default/{file}", out html))
            return html;
        if (catalog.TryGet("npcdefault.htm", out html))
            return html;
        return NpcHtmlText.NothingToSay;
    }

    /// <summary>
    /// Mobius <c>ChatLink</c> parses the integer at <c>command.substring(5)</c>.
    /// <c>Chat</c> and a non-numeric tail are page 0, which re-runs first talk.
    /// </summary>
    public static int ChatPage(string command)
    {
        const string prefix = "Chat";
        if (string.IsNullOrEmpty(command) || command.Length <= prefix.Length + 1)
            return 0;
        var tail = command[(prefix.Length + 1)..].Trim();
        return int.TryParse(tail, out int page) && page > 0 ? page : 0;
    }

    /// <summary>
    /// Mobius <c>Link</c> loads <c>data/html/</c> plus the path and rejects <c>..</c>.
    /// A <c>teleporter/</c> path is empty unless the NPC is a teleporter.
    /// A legal path that is not in the catalog reports the missing file, the same as <c>setFile</c>.
    /// </summary>
    public static LinkPage ReadLink(NpcHtmlCatalog catalog, string? npcType, string command)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!TryRelative(command, out var relative))
            return LinkPage.Rejected();

        if (relative.StartsWith("teleporter/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(npcType, "Teleporter", StringComparison.Ordinal))
            return LinkPage.Body(NpcHtmlText.NothingToSay);

        if (catalog.TryGet(relative, out var html))
            return LinkPage.Body(html);

        return LinkPage.Body("<html><body>My Text is missing:<br>" + relative + "</body></html>");
    }

    public static bool TryRelative(string command, out string relative)
    {
        relative = "";
        const string prefix = "Link";
        if (string.IsNullOrWhiteSpace(command) ||
            !command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            command.Length <= prefix.Length)
            return false;

        var path = command[prefix.Length..].Trim().Replace('\\', '/');
        if (path.Length == 0 || path.StartsWith('/') || path.Contains(':', StringComparison.Ordinal))
            return false;

        var segments = path.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment is "." or "..")
                return false;
        }

        if (!path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) &&
            !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            return false;

        relative = path;
        return true;
    }

    public readonly record struct LinkPage(bool RejectedPath, string Html)
    {
        public static LinkPage Rejected() => new(true, "");
        public static LinkPage Body(string html) => new(false, html);
    }
}
