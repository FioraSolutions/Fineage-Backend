using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Accounts;

static partial class NormalizedStoreReader
{
    static List<StoredItem> LoadItems(NpgsqlConnection conn, int characterId, params string[] locs)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT object_id, item_id, count, enchant_level, loc, loc_data
            FROM character_items
            WHERE character_id = @cid AND loc = ANY(@locs)
            ORDER BY object_id
            """;
        cmd.Parameters.AddWithValue("cid", characterId);
        cmd.Parameters.AddWithValue("locs", locs);
        var rows = new List<StoredItem>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            long oid = reader.GetInt64(0);
            int itemId = reader.GetInt32(1);
            int count = reader.GetInt32(2);
            int enchant = reader.GetInt32(3);
            string loc = reader.GetString(4);
            int locData = reader.GetInt32(5);
            int equipped = loc == NormalizedStoreWriter.LocPaperdoll ? locData : -1;
            rows.Add(new StoredItem(itemId, count, equipped, enchant, checked((int)oid)));
        }

        return rows;
    }

    static List<QuestSaveRow>? LoadQuests(NpgsqlConnection conn, int characterId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT q.quest_id, q.state, v.var_key, v.var_value
            FROM character_quests q
            LEFT JOIN character_quest_vars v
              ON v.character_id = q.character_id AND v.quest_id = q.quest_id
            WHERE q.character_id = @cid
            ORDER BY q.quest_id, v.var_key
            """;
        cmd.Parameters.AddWithValue("cid", characterId);
        var byId = new Dictionary<int, (int State, List<QuestSaveVar> Vars)>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            int questId = reader.GetInt32(0);
            int state = reader.GetInt32(1);
            if (!byId.TryGetValue(questId, out var row))
            {
                row = (state, new List<QuestSaveVar>());
                byId[questId] = row;
            }

            if (!reader.IsDBNull(2))
            {
                string key = reader.GetString(2);
                string value = reader.IsDBNull(3) ? "" : reader.GetString(3);
                row.Vars.Add(new QuestSaveVar(key, value));
                byId[questId] = row;
            }
        }

        if (byId.Count == 0)
            return null;
        return byId.Select(kv => new QuestSaveRow(kv.Key, kv.Value.State, kv.Value.Vars)).ToList();
    }

    static List<StoredSkill>? LoadLearnedSkills(NpgsqlConnection conn, int characterId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT skill_id, skill_level FROM character_skills WHERE character_id = @cid ORDER BY skill_id";
        cmd.Parameters.AddWithValue("cid", characterId);
        var rows = new List<StoredSkill>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rows.Add(new StoredSkill(reader.GetInt32(0), reader.GetInt32(1)));
        return rows.Count == 0 ? null : rows;
    }

    static List<StoredEffect>? LoadEffects(NpgsqlConnection conn, int characterId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT skill_id, skill_level, remaining_time
            FROM character_skills_save
            WHERE character_id = @cid
            ORDER BY skill_id
            """;
        cmd.Parameters.AddWithValue("cid", characterId);
        var rows = new List<StoredEffect>();
        try
        {
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                rows.Add(new StoredEffect(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2)));
        }
        catch (PostgresException)
        {
            // Pre-migration hosts: table missing until 009 runs.
            return null;
        }

        return rows.Count == 0 ? null : rows;
    }

    static List<StoredShortcut>? LoadShortcuts(NpgsqlConnection conn, int characterId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT slot, page, type, shortcut_id, level
            FROM character_shortcuts
            WHERE character_id = @cid
            ORDER BY page, slot
            """;
        cmd.Parameters.AddWithValue("cid", characterId);
        var rows = new List<StoredShortcut>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new StoredShortcut(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return rows.Count == 0 ? null : rows;
    }
}
