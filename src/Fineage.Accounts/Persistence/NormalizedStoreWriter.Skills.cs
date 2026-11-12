using System.Globalization;
using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Accounts;

static partial class NormalizedStoreWriter
{
    public static void ReplaceLearnedSkills(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<StoredSkill>? skills)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_skills WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (skills == null)
            return;
        foreach (var s in skills)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO character_skills (character_id, skill_id, skill_level) VALUES (@cid, @sid, @lvl) " +
                "ON CONFLICT (character_id, skill_id) DO UPDATE SET skill_level = excluded.skill_level";
            ins.Parameters.AddWithValue("cid", characterId);
            ins.Parameters.AddWithValue("sid", s.SkillId);
            ins.Parameters.AddWithValue("lvl", s.Level);
            ins.ExecuteNonQuery();
        }
    }

    public static void ReplaceEffects(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<StoredEffect>? effects)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_skills_save WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (effects == null)
            return;
        foreach (var e in effects)
        {
            if (e == null || !e.IsValid)
                continue;
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                """
                INSERT INTO character_skills_save (character_id, skill_id, skill_level, remaining_time)
                VALUES (@cid, @sid, @lvl, @rem)
                ON CONFLICT (character_id, skill_id) DO UPDATE
                SET skill_level = excluded.skill_level, remaining_time = excluded.remaining_time
                """;
            ins.Parameters.AddWithValue("cid", characterId);
            ins.Parameters.AddWithValue("sid", e.SkillId);
            ins.Parameters.AddWithValue("lvl", e.Level);
            ins.Parameters.AddWithValue("rem", e.RemainingSeconds);
            ins.ExecuteNonQuery();
        }
    }

    static List<StoredSkill>? ReadLearnedSkills(JsonElement character)
    {
        if (!character.TryGetProperty("learnedSkills", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var rows = new List<StoredSkill>();
        foreach (var s in arr.EnumerateArray())
        {
            var row = new StoredSkill(ReadInt(s, "skillId", 0), ReadInt(s, "level", 0));
            if (row.IsValid)
                rows.Add(row);
        }

        return rows;
    }

    public static void ReplaceShortcuts(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<StoredShortcut>? shortcuts)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_shortcuts WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (shortcuts == null)
            return;

        foreach (var s in shortcuts)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                """
                INSERT INTO character_shortcuts (character_id, slot, page, type, shortcut_id, level)
                VALUES (@cid, @slot, @page, @type, @sid, @level)
                ON CONFLICT (character_id, slot, page) DO UPDATE
                SET type = excluded.type, shortcut_id = excluded.shortcut_id, level = excluded.level
                """;
            ins.Parameters.AddWithValue("cid", characterId);
            ins.Parameters.AddWithValue("slot", s.Slot);
            ins.Parameters.AddWithValue("page", s.Page);
            ins.Parameters.AddWithValue("type", s.Type);
            ins.Parameters.AddWithValue("sid", s.Id);
            ins.Parameters.AddWithValue("level", s.Level);
            ins.ExecuteNonQuery();
        }
    }

    static List<StoredShortcut>? ReadShortcuts(JsonElement character)
    {
        if (!character.TryGetProperty("shortcuts", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var rows = new List<StoredShortcut>();
        foreach (var s in arr.EnumerateArray())
        {
            var row = new StoredShortcut(
                ReadInt(s, "slot", -1),
                ReadInt(s, "page", -1),
                ReadInt(s, "type", 0),
                ReadInt(s, "id", 0),
                ReadInt(s, "level", 0));
            if (row.IsValid)
                rows.Add(row);
        }

        return rows;
    }

    static List<QuestSaveRow>? ReadQuests(JsonElement character)
    {
        if (!character.TryGetProperty("quests", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var rows = new List<QuestSaveRow>();
        foreach (var q in arr.EnumerateArray())
        {
            var vars = new List<QuestSaveVar>();
            if (q.TryGetProperty("vars", out var varsEl) && varsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in varsEl.EnumerateArray())
                {
                    string key = v.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                    string value = v.TryGetProperty("value", out var val) ? val.GetString() ?? "" : "";
                    if (key.Length > 0)
                        vars.Add(new QuestSaveVar(key, value));
                }
            }

            rows.Add(new QuestSaveRow(ReadInt(q, "questId", 0), ReadInt(q, "state", 0), vars));
        }

        return rows;
    }

    static int ReadInt(JsonElement el, string name, int fallback) =>
        el.TryGetProperty(name, out var p) && p.TryGetInt32(out int v) ? v : fallback;

    static long ReadLong(JsonElement el, string name, long fallback) =>
        el.TryGetProperty(name, out var p) && p.TryGetInt64(out long v) ? v : fallback;

    static DateTimeOffset? ReadDate(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            return null;
        string? text = p.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }
}
