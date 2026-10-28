namespace Fineage.Zone.NpcHtml;

/// <summary>Mobius <c>ChatLink</c>. Page 0 runs first talk again. Any other page is that chat file.</summary>
public sealed class ChatBypassHandler : IBypassHandler
{
    readonly NpcHtmlCatalog _html;

    public ChatBypassHandler(NpcHtmlCatalog html)
    {
        _html = html ?? throw new ArgumentNullException(nameof(html));
    }

    public IReadOnlyList<string> Commands { get; } = ["Chat"];

    public BypassReply Handle(BypassCall call)
    {
        int page = NpcHtmlPaths.ChatPage(call.Command);
        if (page == 0)
            return BypassReply.TalkAgain();
        return BypassReply.Page(NpcHtmlPaths.Read(_html, call.NpcType, call.NpcId, page));
    }
}

/// <summary>Mobius <c>Link</c>. The path stays under <c>data/html</c>.</summary>
public sealed class LinkBypassHandler : IBypassHandler
{
    readonly NpcHtmlCatalog _html;

    public LinkBypassHandler(NpcHtmlCatalog html)
    {
        _html = html ?? throw new ArgumentNullException(nameof(html));
    }

    public IReadOnlyList<string> Commands { get; } = ["Link"];

    public BypassReply Handle(BypassCall call)
    {
        var page = NpcHtmlPaths.ReadLink(_html, call.NpcType, call.Command);
        if (page.RejectedPath)
            return BypassReply.Fail("bad_request");
        return BypassReply.Page(page.Html);
    }
}

/// <summary>
/// Mobius <c>Quest</c> bypass. No script name opens the quest list.
/// <c>Quest &lt;id&gt;</c> is the talk hook. <c>Quest &lt;id&gt; &lt;event&gt;</c> is the adv-event hook.
/// <c>quest_talk:</c> and <c>quest_event:</c> are the links already used by quest HTML.
/// A non-numeric script name is a readable page, because those Mobius quest classes are not loaded.
/// </summary>
public sealed class QuestBypassHandler : IBypassHandler
{
    public const string Unavailable = "<html><body>%npcname%:<br>That action is not implemented yet.</body></html>";

    /// <summary>
    /// <c>Quest</c> is the Mobius quest bypass. <c>Script</c> with no name is the Quest button
    /// in the Interlude HTML (<c>bypass Script</c>). <c>questlist</c> is the adventurer guild list.
    /// </summary>
    public IReadOnlyList<string> Commands { get; } = ["Quest", "Script", "questlist"];

    public BypassReply Handle(BypassCall call)
    {
        var command = call.Command.Trim();
        if (command.StartsWith("quest_talk:", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(command["quest_talk:".Length..], out int talkId) || talkId <= 0)
                return BypassReply.Page(Unavailable);
            return BypassReply.Quest(talkId, "");
        }

        if (command.StartsWith("quest_event:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = command["quest_event:".Length..];
            var parts = rest.Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[0], out int eventId) && eventId > 0 && parts[1].Length > 0)
                return BypassReply.Quest(eventId, parts[1]);
            return BypassReply.Page(Unavailable);
        }

        var token = BypassCommands.Token(command);
        if (token.Equals("questlist", StringComparison.OrdinalIgnoreCase))
            return BypassReply.QuestList();

        var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (args.Length <= 1)
            return BypassReply.QuestList();
        if (!int.TryParse(args[1], out int questId) || questId <= 0)
            return BypassReply.Page(Unavailable);
        if (args.Length == 2)
            return BypassReply.Quest(questId, "");
        return BypassReply.Quest(questId, args[2]);
    }
}

/// <summary>
/// Readable stand-in for a service that does not run yet.
/// The sentence is <c>{Service} is not implemented yet.</c>
/// </summary>
public sealed class StubBypassHandler : IBypassHandler
{
    public StubBypassHandler(string service, params string[] commands)
    {
        if (string.IsNullOrWhiteSpace(service))
            throw new ArgumentException("Service name is empty.", nameof(service));
        if (commands == null || commands.Length == 0)
            throw new ArgumentException("A stub needs at least one command.", nameof(commands));
        Service = service.Trim();
        Commands = commands;
    }

    public string Service { get; }

    public IReadOnlyList<string> Commands { get; }

    public BypassReply Handle(BypassCall call) =>
        BypassReply.Page($"<html><body>%npcname%:<br>{Service} is not implemented yet.</body></html>");
}
