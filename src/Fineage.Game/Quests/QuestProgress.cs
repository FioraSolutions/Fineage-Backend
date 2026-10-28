namespace Fineage.Game.Quests;

/// <summary>In-memory quest state for one character and one quest.</summary>
public sealed class QuestProgress
{
    public const string CondVar = "cond";
    public const string FlagsVar = "__compltdStateFlags";
    public const int MaxKeyLength = 20;
    public const int MaxValueLength = 255;

    readonly Dictionary<string, string> _vars;

    public QuestProgress(int questId, QuestPhase phase, IReadOnlyDictionary<string, string>? vars = null)
    {
        if (questId <= 0)
            throw new ArgumentOutOfRangeException(nameof(questId));
        QuestId = questId;
        Phase = phase;
        _vars = vars == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(vars, StringComparer.Ordinal);
    }

    public int QuestId { get; }

    public QuestPhase Phase { get; set; }

    public IReadOnlyDictionary<string, string> Vars => _vars;

    public int Cond => ReadInt(CondVar);

    public int Flags => ReadInt(FlagsVar);

    /// <summary>Classic journal word: completion flags when set, otherwise <see cref="Cond"/>.</summary>
    public int JournalWord => Flags != 0 ? Flags : Cond;

    public string? Get(string key) => _vars.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string value) => _vars[key] = value;

    public void Unset(string key) => _vars.Remove(key);

    public void ClearVars() => _vars.Clear();

    public void Replace(QuestPhase phase, IReadOnlyDictionary<string, string> vars)
    {
        Phase = phase;
        _vars.Clear();
        foreach (var pair in vars)
            _vars[pair.Key] = pair.Value;
    }

    public QuestProgress Clone() => new(QuestId, Phase, _vars);

    int ReadInt(string key)
    {
        if (!_vars.TryGetValue(key, out var text) || !int.TryParse(text, out int value))
            return 0;
        return value;
    }
}
