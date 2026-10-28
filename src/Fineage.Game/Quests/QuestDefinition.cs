namespace Fineage.Game.Quests;

public enum QuestHook
{
    FirstTalk,
    Talk,
    Event,
    Kill
}

public enum QuestWhen
{
    Absent,
    Created,
    Started,
    Completed
}

public abstract record QuestOp;

public sealed record CreateOp : QuestOp;

public sealed record StartOp : QuestOp;

public sealed record SetCondOp(int Cond) : QuestOp;

public sealed record SetVarOp(string Key, string Value) : QuestOp;

public sealed record UnsetVarOp(string Key) : QuestOp;

public sealed record GiveItemOp(int ItemId, int Count) : QuestOp;

public sealed record GiveAdenaOp(int Count) : QuestOp;

public sealed record GiveExpSpOp(long Exp, int Sp) : QuestOp;

public sealed record TakeItemOp(int ItemId, int Count) : QuestOp;

public sealed record CompleteOp : QuestOp;

public sealed record SoundOp(string Name) : QuestOp;

public sealed record QuestDialog(
    int NpcId,
    QuestHook Hook,
    string Event,
    QuestWhen When,
    int? Cond,
    string? WhenVar,
    string? WhenValue,
    string Html,
    IReadOnlyList<QuestOp> Ops);

public sealed record QuestDefinition(
    int Id,
    string Name,
    bool Repeatable,
    bool Abortable,
    IReadOnlyList<int> QuestItems,
    IReadOnlyList<QuestDialog> Dialogs);
