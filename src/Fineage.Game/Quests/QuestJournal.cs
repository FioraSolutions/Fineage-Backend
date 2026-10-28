namespace Fineage.Game.Quests;

/// <summary>Every quest row stored for one character.</summary>
public sealed class QuestJournal
{
    public const int CompletedMaskLength = 128;

    readonly Dictionary<int, QuestProgress> _rows = [];

    public int StartedCount
    {
        get
        {
            int count = 0;
            foreach (var row in _rows.Values)
            {
                if (row.Phase == QuestPhase.Started)
                    count++;
            }

            return count;
        }
    }

    public QuestProgress? Find(int questId) => _rows.TryGetValue(questId, out var row) ? row : null;

    public IReadOnlyList<QuestProgress> All()
    {
        var rows = new List<QuestProgress>(_rows.Values);
        rows.Sort(static (a, b) => a.QuestId.CompareTo(b.QuestId));
        return rows;
    }

    public QuestProgress Add(int questId, QuestPhase phase)
    {
        var row = new QuestProgress(questId, phase);
        _rows[questId] = row;
        return row;
    }

    public void Remove(int questId) => _rows.Remove(questId);

    public static QuestJournal Load(IReadOnlyList<QuestSaveRow>? rows)
    {
        var journal = new QuestJournal();
        if (rows == null)
            return journal;

        foreach (var row in rows)
        {
            if (row.QuestId <= 0 || journal._rows.ContainsKey(row.QuestId))
                continue;
            if (row.State is not ((int)QuestPhase.Created or (int)QuestPhase.Started or (int)QuestPhase.Completed))
                continue;

            var vars = new Dictionary<string, string>(StringComparer.Ordinal);
            if (row.State != (int)QuestPhase.Completed && row.Vars != null)
            {
                foreach (var variable in row.Vars)
                {
                    if (!IsStoredVar(variable.Key, variable.Value) || vars.ContainsKey(variable.Key))
                        continue;
                    vars[variable.Key] = variable.Value;
                }
            }

            journal._rows[row.QuestId] = new QuestProgress(row.QuestId, (QuestPhase)row.State, vars);
        }

        return journal;
    }

    public QuestSaveRow[] Save()
    {
        var rows = new List<QuestSaveRow>(_rows.Count);
        foreach (var progress in All())
        {
            var vars = new List<QuestSaveVar>();
            if (progress.Phase != QuestPhase.Completed)
            {
                foreach (var pair in progress.Vars.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                {
                    if (IsStoredVar(pair.Key, pair.Value))
                        vars.Add(new QuestSaveVar(pair.Key, pair.Value));
                }
            }

            rows.Add(new QuestSaveRow(progress.QuestId, (int)progress.Phase, vars));
        }

        return rows.ToArray();
    }

    public static bool IsStoredVar(string? key, string? value)
    {
        if (string.IsNullOrEmpty(key) || key.Length > QuestProgress.MaxKeyLength)
            return false;
        if (value == null || value.Length > QuestProgress.MaxValueLength)
            return false;
        foreach (char c in key)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                return false;
        }

        return true;
    }
}
