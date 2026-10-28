namespace Fineage.Zone.NpcHtml;

/// <summary>
/// Command token to handler. The token is the first word, compared without case.
/// <c>quest_talk:</c> and <c>quest_event:</c> resolve to the Quest handler when one is registered.
/// </summary>
public sealed class BypassRegistry
{
    readonly object _gate = new();
    readonly Dictionary<string, IBypassHandler> _byCommand = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IBypassHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (handler.Commands == null || handler.Commands.Count == 0)
            throw new ArgumentException("Handler has no commands.", nameof(handler));

        lock (_gate)
        {
            foreach (string command in handler.Commands)
            {
                if (string.IsNullOrWhiteSpace(command) || command.Contains(' ', StringComparison.Ordinal))
                    throw new ArgumentException($"Bypass command '{command}' is not a single token.", nameof(handler));
                _byCommand[command.Trim()] = handler;
            }
        }
    }

    public IBypassHandler? Find(string command)
    {
        var token = BypassCommands.Token(command);
        lock (_gate)
        {
            if (_byCommand.TryGetValue(token, out var handler))
                return handler;
            if (token.StartsWith("quest_talk:", StringComparison.OrdinalIgnoreCase) ||
                token.StartsWith("quest_event:", StringComparison.OrdinalIgnoreCase))
                return _byCommand.GetValueOrDefault("Quest");
            return null;
        }
    }

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_gate)
            {
                var names = _byCommand.Keys.ToArray();
                Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                return names;
            }
        }
    }

    public static BypassRegistry CreateDefault(NpcHtmlCatalog html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var registry = new BypassRegistry();
        registry.Register(new ChatBypassHandler(html));
        registry.Register(new LinkBypassHandler(html));
        registry.Register(new QuestBypassHandler());
        registry.Register(new ShopBypassHandler());
        registry.Register(new StubBypassHandler("Wear", "Wear"));
        registry.Register(new TeleportBypassHandler());
        registry.Register(new StubBypassHandler("Teleport", "showNoblesSelect"));
        registry.Register(new WarehouseBypassHandler());
        registry.Register(new StubBypassHandler(
            "Warehouse",
            "DepositC",
            "WithdrawC",
            "DepositF",
            "WithdrawF",
            "package_deposit",
            "package_withdraw"));
        registry.Register(new BuffBypassHandler());
        return registry;
    }

    public const string NotImplemented =
        "<html><body>%npcname%:<br>That action is not implemented yet.</body></html>";
}
