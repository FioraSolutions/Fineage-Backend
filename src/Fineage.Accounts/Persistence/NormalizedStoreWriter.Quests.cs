using System.Globalization;
using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Accounts;

static partial class NormalizedStoreWriter
{
    public static void ReplaceQuests(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<QuestSaveRow>? quests)
    {
        using (var delVars = conn.CreateCommand())
        {
            delVars.Transaction = tx;
            delVars.CommandText = "DELETE FROM character_quest_vars WHERE character_id = @id";
            delVars.Parameters.AddWithValue("id", characterId);
            delVars.ExecuteNonQuery();
        }

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_quests WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (quests == null)
            return;

        foreach (var quest in quests)
        {
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText =
                    """
                    INSERT INTO character_quests (character_id, quest_id, state)
                    VALUES (@cid, @qid, @state)
                    """;
                ins.Parameters.AddWithValue("cid", characterId);
                ins.Parameters.AddWithValue("qid", quest.QuestId);
                ins.Parameters.AddWithValue("state", quest.State);
                ins.ExecuteNonQuery();
            }

            foreach (var variable in quest.Vars)
            {
                using var insVar = conn.CreateCommand();
                insVar.Transaction = tx;
                insVar.CommandText =
                    """
                    INSERT INTO character_quest_vars (character_id, quest_id, var_key, var_value)
                    VALUES (@cid, @qid, @key, @value)
                    """;
                insVar.Parameters.AddWithValue("cid", characterId);
                insVar.Parameters.AddWithValue("qid", quest.QuestId);
                insVar.Parameters.AddWithValue("key", variable.Key);
                insVar.Parameters.AddWithValue("value", variable.Value);
                insVar.ExecuteNonQuery();
            }
        }
    }
}
