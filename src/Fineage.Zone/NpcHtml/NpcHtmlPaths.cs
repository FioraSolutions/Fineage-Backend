namespace Fineage.Zone.NpcHtml;

/// <summary>
/// Picks the HTML file for an NPC type and page.
/// Mobius <c>Npc.getHtmlPath</c> uses <c>data/html/default/{id}.htm</c> for page 0
/// and <c>data/html/default/{id}-{page}.htm</c> otherwise, then <c>npcdefault.htm</c>.
/// Merchant, warehouse, teleporter, guard, trainer, fisherman, adventurer, pet manager,
/// and village master override that folder. A missing typed file falls back the same way.
/// <para>
/// The folder comes from which Mobius class the template instantiates, not from the type name: a
/// <c>DungeonGatekeeper</c> is a <c>DungeonGatekeeper</c> object and writes its pages to <c>teleporter/</c>
/// (<c>DungeonGatekeeper.getHtmlPath</c>), while <c>SepulcherNpc</c> writes to <c>SepulcherNpc/</c>. Routing
/// them to <c>default/</c> served every one of them the "nothing to say" page.
/// </para>
/// </summary>
public static class NpcHtmlPaths
{
    /// <summary>
    /// CT 0 Interlude Four Sepulchers NPCs are plain <c>Folk</c>; the <c>FourSepulchers</c> AI script answers
    /// <c>onFirstTalk</c> with <c>{npcId}.html</c> from its own folder (managers 31921-31924 plus
    /// <c>FIRST_TALK_NPCS</c>). Fineage keeps those pages under <c>SepulcherNpc/</c>, so routing is by id, not by
    /// the HighFive-only <c>SepulcherNpc</c> instance type.
    /// </summary>
    public static readonly IReadOnlySet<int> FourSepulchersTalkIds = new HashSet<int>
    {
        31452, 31453, 31454, 31919, 31920, 31921, 31922, 31923, 31924, 31925, 31926, 31927, 31928,
        31929, 31930, 31931, 31932, 31933, 31934, 31935, 31936, 31937, 31938, 31939, 31940, 31941,
        31942, 31943, 31944
    };

    /// <summary>Typed folder for an NPC: the Four Sepulchers script ids first, then the instance type.</summary>
    public static string Folder(string? npcType, int npcId) =>
        FourSepulchersTalkIds.Contains(npcId) ? "SepulcherNpc" : Folder(npcType);

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
            "Teleporter" or "DungeonGatekeeper" => "teleporter",
            "SepulcherNpc" => "SepulcherNpc",
            "Guard" => "guard",
            "Trainer" => "trainer",
            "Adventurer" => "adventurer_guildsman",
            "PetManager" => "petmanager",
            "Doorman" or "CastleDoorman" or "ClanHallDoorman" => "doorman",
            "SignsPriest" or "DawnPriest" or "DuskPriest" => "seven_signs",
            _ => "default"
        };
    }

    /// <summary>Mobius <c>data/html/npcdefault.htm</c>: the page of an NPC that has none of its own.</summary>
    public const string DefaultPage = "npcdefault.htm";

    public static string FileName(int npcId, int page) =>
        page <= 0 ? $"{npcId}.htm" : $"{npcId}-{page}.htm";

    /// <summary>Type folder, then <c>default</c>, then <c>npcdefault.htm</c>. This is page choice, not a language fallback.</summary>
    public static IReadOnlyList<string> Candidates(string? npcType, int npcId, int page)
    {
        string file = FileName(npcId, page);
        string folder = Folder(npcType, npcId);
        if (string.Equals(folder, "default", StringComparison.Ordinal))
            return [$"{folder}/{file}", DefaultPage];
        return [$"{folder}/{file}", $"default/{file}", DefaultPage];
    }

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
