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
                INSERT INTO character_skills_save (
                    character_id, skill_id, skill_level, remaining_time, reuse_delay, systime, restore_type)
                VALUES (@cid, @sid, @lvl, @rem, @reuse, @sys, @rtype)
                ON CONFLICT (character_id, skill_id) DO UPDATE
                SET skill_level = excluded.skill_level,
                    remaining_time = excluded.remaining_time,
                    reuse_delay = excluded.reuse_delay,
                    systime = excluded.systime,
                    restore_type = excluded.restore_type
                """;
            ins.Parameters.AddWithValue("cid", characterId);
            ins.Parameters.AddWithValue("sid", e.SkillId);
            ins.Parameters.AddWithValue("lvl", Math.Max(1, e.Level));
            ins.Parameters.AddWithValue("rem", e.IsEffect ? e.RemainingSeconds : -1);
            ins.Parameters.AddWithValue("reuse", e.ReuseDelayMs);
            ins.Parameters.AddWithValue("sys", e.SystimeMs);
            ins.Parameters.AddWithValue("rtype", e.RestoreType);
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

    public static void ReplaceMacros(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<StoredMacro>? macros)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_macros WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (macros == null)
            return;

        foreach (var m in macros)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                """
                INSERT INTO character_macros (character_id, macro_id, icon, name, descr, acronym, commands)
                VALUES (@cid, @mid, @icon, @name, @descr, @acronym, @commands)
                ON CONFLICT (character_id, macro_id) DO UPDATE
                SET icon = excluded.icon, name = excluded.name, descr = excluded.descr,
                    acronym = excluded.acronym, commands = excluded.commands
                """;
            ins.Parameters.AddWithValue("cid", characterId);
            ins.Parameters.AddWithValue("mid", m.Id);
            ins.Parameters.AddWithValue("icon", m.Icon);
            ins.Parameters.AddWithValue("name", m.Name ?? "");
            ins.Parameters.AddWithValue("descr", m.Descr ?? "");
            ins.Parameters.AddWithValue("acronym", m.Acronym ?? "");
            ins.Parameters.AddWithValue("commands", EncodeMacroCommands(m.Commands));
            ins.ExecuteNonQuery();
        }
    }

    /// <summary>Mobius DB commands column: <c>type,d1,d2[,cmd];...</c> capped at 255.</summary>
    internal static string EncodeMacroCommands(IReadOnlyList<StoredMacroCmd>? commands)
    {
        if (commands == null || commands.Count == 0)
            return "";
        var sb = new System.Text.StringBuilder(300);
        foreach (var cmd in commands)
        {
            sb.Append(cmd.Type).Append(',').Append(cmd.D1).Append(',').Append(cmd.D2);
            if (!string.IsNullOrEmpty(cmd.Cmd))
                sb.Append(',').Append(cmd.Cmd);
            sb.Append(';');
        }

        if (sb.Length > StoredMacro.MaxCommandsLength)
            sb.Length = StoredMacro.MaxCommandsLength;
        return sb.ToString();
    }

    static List<StoredMacro>? ReadMacros(JsonElement character)
    {
        if (!character.TryGetProperty("macros", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var rows = new List<StoredMacro>();
        foreach (var m in arr.EnumerateArray())
        {
            int id = ReadInt(m, "id", 0);
            if (id <= 0)
                continue;
            string name = ReadString(m, "name");
            if (string.IsNullOrEmpty(name))
                continue;
            var cmds = new List<StoredMacroCmd>();
            if (m.TryGetProperty("commands", out var cmdArr) && cmdArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cmdArr.EnumerateArray())
                {
                    cmds.Add(new StoredMacroCmd(
                        ReadInt(c, "entry", cmds.Count),
                        ReadInt(c, "type", 0),
                        ReadInt(c, "d1", 0),
                        ReadInt(c, "d2", 0),
                        ReadString(c, "cmd")));
                }
            }

            rows.Add(new StoredMacro(
                id,
                ReadInt(m, "icon", 0),
                name,
                ReadString(m, "descr"),
                ReadString(m, "acronym"),
                cmds));
        }

        return rows.Count == 0 ? null : rows;
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

    static string ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

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
