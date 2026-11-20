using System.Globalization;

namespace Fineage.Game.Quests;

/// <summary>Interlude questname-e step targets (quest_x/y/z) for radar without per-quest code.</summary>
public sealed class QuestJournalGeometry
{
    readonly Dictionary<(int QuestId, int Step), (int X, int Y, int Z)> _rows = new();

    public static QuestJournalGeometry LoadDefault()
    {
        var geo = new QuestJournalGeometry();
        string path = Path.Combine(AppContext.BaseDirectory, "data", "questname-e_interlude.txt");
        if (!File.Exists(path))
            path = Path.Combine(Directory.GetCurrentDirectory(), "data", "questname-e_interlude.txt");
        if (!File.Exists(path))
            return geo;
        geo.Read(path);
        return geo;
    }

    void Read(string path)
    {
        foreach (string line in File.ReadLines(path).Skip(1))
        {
            string[] cols = line.Split('\t');
            if (cols.Length < 40)
                continue;
            if (!int.TryParse(cols[1], out int questId) || !int.TryParse(cols[2], out int step))
                continue;
            if (!TryCoord(cols, 36, out int x, out int y, out int z))
                continue;
            _rows[(questId, step)] = (x, y, z);
        }
    }

    static bool TryCoord(string[] cols, int xIndex, out int x, out int y, out int z)
    {
        x = y = z = 0;
        if (cols.Length <= xIndex + 2)
            return false;
        if (!float.TryParse(cols[xIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out float fx))
            return false;
        if (!float.TryParse(cols[xIndex + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float fy))
            return false;
        if (!float.TryParse(cols[xIndex + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float fz))
            return false;
        x = (int)fx;
        y = (int)fy;
        z = (int)fz;
        return x != 0 || y != 0;
    }

    public bool TryTarget(int questId, int step, out int x, out int y, out int z)
    {
        if (_rows.TryGetValue((questId, step), out var t))
        {
            x = t.X;
            y = t.Y;
            z = t.Z;
            return true;
        }

        x = y = z = 0;
        return false;
    }
}