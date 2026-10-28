namespace Fineage.Protocol;

/// <summary>
/// Zone quest wire. Requests sit at 42–45, after enchant 39–41 and clan 32–38.
/// Replies sit at 1061–1063, after enchant result 1060 and the clan band 1050–1059.
/// Chat stays 1040–1049. Siege uses 46–49 and 1064. 50 and 1065–1069 are left open.
/// </summary>
/// <remarks>
/// Rules follow L2J Mobius Interlude (not its Java layout):
/// <c>Quest</c> / <c>QuestState</c> states CREATED, STARTED, COMPLETED;
/// <c>cond</c> and arbitrary variables including <c>__compltdStateFlags</c>;
/// <c>QuestType</c> one-time versus repeatable;
/// <c>exitQuest(true)</c> deletes the row (abort, and repeatable completion) and
/// <c>exitQuest(false)</c> stores COMPLETED;
/// <c>RequestQuestAbort</c> uses the deleting exit;
/// active cap 25 (<c>MaxQuestCount</c>);
/// <c>QuestList</c> sends started quests plus a 128-byte one-time mask;
/// talk hooks match <c>onFirstTalk</c>, <c>onTalk</c>, and <c>onAdvEvent</c>;
/// <c>onKill</c> is <c>IQuestKillHook</c> and is not called from melee yet.
/// Layout: <c>docs/quest-v2-protocol.md</c>.
/// </remarks>
public static class QuestProtocol
{
    public const ushort RequestList = 42;
    public const ushort RequestAbort = 43;
    public const ushort Talk = 44;
    public const ushort Event = 45;

    public const ushort List = 1061;
    public const ushort Dialog = 1062;
    public const ushort Sound = 1063;

    public const int MaxActive = 25;
    public const int TalkRange = 150;
    public const int CompletedMaskLength = 128;

    public const string Unknown = "quest_unknown";
    public const string Limit = "quest_limit";
    public const string State = "quest_state";
    public const string Abort = "quest_abort";
    public const string Item = "quest_item";
    public const string Npc = "quest_npc";

    /// <summary>System message ids on <see cref="MsgType.SystemMessage"/>. Chat is 1–3. Enchant is 10–20. Clan is 100–114.</summary>
    public const int Acquired = 30;
    public const int TooMany = 31;
    public const int Completed = 32;
    public const int Cancelled = 33;
    public const int ItemEarned = 34;
    public const int Adena = 35;
    public const int ExpSp = 36;
    public const int AlreadyFinished = 37;
    public const int NotAbortable = 38;

    public const int Created = 0;
    public const int Started = 1;
    public const int CompletedState = 2;
}
