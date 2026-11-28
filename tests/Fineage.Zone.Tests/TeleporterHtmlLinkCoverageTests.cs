using System.Text.RegularExpressions;
using Fineage.Zone.NpcHtml;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// #954 / #969: every <c>Chat N</c> and <c>Link</c> on a teleporter page must resolve to a real page.
/// A dead Chat target used to serve <c>npcdefault.htm</c> ("I have nothing to say") with no import error.
/// </summary>
public sealed class TeleporterHtmlLinkCoverageTests
{
    static readonly Regex Bypass = new(
        "action=\"bypass\\s+-h\\s+([^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    [Fact]
    public void Every_Chat_and_Link_on_teleporter_pages_resolves()
    {
        var catalog = NpcHtmlCatalog.LoadDefault();
        var failures = new List<string>();
        var pages = catalog.RelativePaths
            .Where(k => k.StartsWith("teleporter/", StringComparison.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Assert.True(pages.Count > 100, $"only {pages.Count} teleporter pages loaded");

        foreach (string relative in pages)
        {
            Assert.True(catalog.TryGet(relative, out var html));
            // Shared templates (nobles_select.htm, …) are not per-npcId; Chat N is only meaningful on {id}.htm.
            bool hasNpcId = TryParseNpcId(relative, out int npcId);
            foreach (Match match in Bypass.Matches(html))
            {
                string command = match.Groups[1].Value.Trim();
                if (command.StartsWith("npc_%objectId%_Chat", StringComparison.OrdinalIgnoreCase) ||
                    command.StartsWith("Chat", StringComparison.OrdinalIgnoreCase))
                {
                    if (!hasNpcId)
                        continue;
                    string chat = command.StartsWith("Chat", StringComparison.OrdinalIgnoreCase)
                        ? command
                        : command["npc_%objectId%_".Length..];
                    int page = NpcHtmlPaths.ChatPage(chat);
                    string body = NpcHtmlPaths.Read(catalog, "Teleporter", npcId, page);
                    if (body.Contains("I have nothing to say to you.", StringComparison.Ordinal) ||
                        body.Contains("My Text is missing:", StringComparison.Ordinal))
                    {
                        failures.Add($"{relative}: Chat {page} missing");
                    }

                    continue;
                }

                if (command.StartsWith("npc_%objectId%_Link", StringComparison.OrdinalIgnoreCase) ||
                    command.StartsWith("Link", StringComparison.OrdinalIgnoreCase))
                {
                    string linkCommand = command.StartsWith("Link", StringComparison.OrdinalIgnoreCase)
                        ? command
                        : command["npc_%objectId%_".Length..];
                    var link = NpcHtmlPaths.ReadLink(catalog, "Teleporter", linkCommand);
                    if (link.RejectedPath ||
                        link.Html.Contains("I have nothing to say to you.", StringComparison.Ordinal) ||
                        link.Html.Contains("My Text is missing:", StringComparison.Ordinal))
                    {
                        failures.Add($"{relative}: {command} missing");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    static bool TryParseNpcId(string relative, out int npcId)
    {
        string file = Path.GetFileNameWithoutExtension(relative.Replace('\\', '/'));
        int dash = file.IndexOf('-');
        string idPart = dash < 0 ? file : file[..dash];
        return int.TryParse(idPart, out npcId);
    }
}
