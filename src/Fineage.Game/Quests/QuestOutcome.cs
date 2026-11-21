namespace Fineage.Game.Quests;

public enum QuestFailure
{
    None,
    BadRequest,
    Unknown,
    Limit,
    State,
    Abort,
    Item
}

/// <summary>
/// System-message ids on packet 1040. Chat uses 1–3, enchant uses 10–20, clan uses 100–114.
/// These values are the wire ids in <c>QuestProtocol</c>.
/// </summary>
public enum QuestNotice
{
    Acquired = 30,
    TooMany = 31,
    Completed = 32,
    Cancelled = 33,
    ItemEarned = 34,
    Adena = 35,
    ExpSp = 36,
    AlreadyFinished = 37,
    NotAbortable = 38
}

public sealed record QuestLine(QuestNotice Notice, string Text, string[] Params);

public enum BagTouchKind
{
    Add = 0,
    Modify = 1,
    Remove = 2
}

public readonly record struct BagTouch(BagTouchKind Kind, ItemInstance Item);

public sealed class QuestOutcome
{
    public QuestFailure Failure { get; init; }
    public string Message { get; init; } = "";
    public string Html { get; init; } = "";

    /// <summary>Catalog key when <see cref="Html"/> came from a quest HTML file. Null for inline HTML.</summary>
    public string? HtmlKey { get; init; }
    public int QuestId { get; init; }
    public string Hook { get; init; } = "";
    public string Event { get; init; } = "";
    public bool JournalChanged { get; init; }
    public IReadOnlyList<string> Sounds { get; init; } = [];
    public IReadOnlyList<QuestLine> Lines { get; init; } = [];
    public IReadOnlyList<BagTouch> Items { get; init; } = [];
    public long Exp { get; init; }
    public int Sp { get; init; }
    /// <summary>Npc ids to spawn near the dialog NPC (Mobius quest <c>addSpawn</c>).</summary>
    public IReadOnlyList<int> Spawns { get; init; } = [];
    /// <summary>Quest spawns at fixed world points.</summary>
    public IReadOnlyList<QuestSpawnPoint> SpawnsAt { get; init; } = [];
    /// <summary>Mobius <c>setNoble(true)</c> requested by the step.</summary>
    public bool Noble { get; init; }
    /// <summary>Mobius <c>teleToLocation</c> requested by the step.</summary>
    public (int X, int Y, int Z)? Teleport { get; init; }
    /// <summary>Mobius SocialAction id broadcast for the player, 0 none.</summary>
    public int Social { get; init; }
    public QuestExtras Extras { get; init; } = QuestExtras.None;
    public IReadOnlyList<TutorialUiOp> TutorialUi { get; init; } = [];

    public bool Ok => Failure == QuestFailure.None;
}
