namespace Fineage.Zone.NpcHtml;

/// <summary>
/// One bypass command family. Register it on <see cref="BypassRegistry"/>.
/// A later registration of the same command replaces the previous handler.
/// Shop work replaces Buy, Sell, Multisell, Wear, and Exchange.
/// Teleport, warehouse, and buff work replaces goto, showTeleports, the Deposit, Withdraw,
/// and package commands, SupportMagic, SupportBlessing, and GiveBlessing.
/// Handlers return a reply. The world applies it. They do not touch zone state.
/// </summary>
public interface IBypassHandler
{
    IReadOnlyList<string> Commands { get; }

    BypassReply Handle(BypassCall call);
}

/// <summary>The NPC and the player a bypass is aimed at. The command still includes its first token.</summary>
public sealed record BypassCall
{
    public required int NpcObjectId { get; init; }
    public required int NpcId { get; init; }
    public required string NpcName { get; init; }
    public required string NpcType { get; init; }
    public required string PlayerName { get; init; }
    public required string Command { get; init; }
}

/// <summary>What the world should send after a bypass. At most one of the actions is set.</summary>
public sealed class BypassReply
{
    public string? Html { get; init; }
    public bool ReplayTalk { get; init; }
    public bool ShowQuestList { get; init; }
    public bool QuestAction { get; init; }
    public int QuestId { get; init; }
    public string QuestEvent { get; init; } = "";
    public string? Error { get; init; }

    public static BypassReply Page(string html) => new() { Html = html };

    public static BypassReply TalkAgain() => new() { ReplayTalk = true };

    public static BypassReply QuestList() => new() { ShowQuestList = true };

    public static BypassReply Quest(int questId, string? evt) => new()
    {
        QuestAction = true,
        QuestId = questId,
        QuestEvent = evt ?? ""
    };

    public static BypassReply Fail(string error) => new() { Error = error };
}
