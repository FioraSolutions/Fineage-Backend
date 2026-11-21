using Npgsql;

namespace Fineage.Accounts;

static partial class NormalizedStoreWriter
{
    public static IReadOnlyList<CursedWeaponRecord> LoadCursedWeapons(NpgsqlConnection conn)
    {
        var rows = new List<CursedWeaponRecord>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT item_id, character_id, player_reputation, player_pk_kills, nb_kills, end_time_unix_ms,
                   activated, dropped, ground_object_id
            FROM cursed_weapons
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CursedWeaponRecord
            {
                ItemId = reader.GetInt32(0),
                CharacterId = reader.GetInt32(1),
                PlayerReputation = reader.GetInt32(2),
                PlayerPkKills = reader.GetInt32(3),
                NbKills = reader.GetInt32(4),
                EndTimeUnixMs = reader.GetInt64(5),
                Activated = reader.GetBoolean(6),
                Dropped = reader.GetBoolean(7),
                GroundObjectId = reader.GetInt32(8)
            });
        }

        return rows;
    }

    public static void SaveCursedWeapons(NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyList<CursedWeaponRecord> rows)
    {
        Run(conn, tx, "DELETE FROM cursed_weapons");
        foreach (var row in rows)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                """
                INSERT INTO cursed_weapons
                    (item_id, character_id, player_reputation, player_pk_kills, nb_kills, end_time_unix_ms,
                     activated, dropped, ground_object_id)
                VALUES (@item, @char, @rep, @pk, @kills, @end, @act, @drop, @ground)
                """;
            ins.Parameters.AddWithValue("item", row.ItemId);
            ins.Parameters.AddWithValue("char", row.CharacterId);
            ins.Parameters.AddWithValue("rep", row.PlayerReputation);
            ins.Parameters.AddWithValue("pk", row.PlayerPkKills);
            ins.Parameters.AddWithValue("kills", row.NbKills);
            ins.Parameters.AddWithValue("end", row.EndTimeUnixMs);
            ins.Parameters.AddWithValue("act", row.Activated);
            ins.Parameters.AddWithValue("drop", row.Dropped);
            ins.Parameters.AddWithValue("ground", row.GroundObjectId);
            ins.ExecuteNonQuery();
        }
    }
}