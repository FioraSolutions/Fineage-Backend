using Npgsql;

namespace Fineage.Accounts;

static partial class NormalizedStoreWriter
{
    public static IReadOnlyList<WeddingCoupleRecord> LoadWeddingCouples(NpgsqlConnection conn)
    {
        var rows = new List<WeddingCoupleRecord>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT id, player1_id, player2_id, married, affiance_date_unix_ms, wedding_date_unix_ms
            FROM wedding_couples
            ORDER BY id
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new WeddingCoupleRecord(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetBoolean(3),
                reader.GetInt64(4),
                reader.GetInt64(5)));
        }

        return rows;
    }

    public static bool HasWeddingMeta(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM store_meta WHERE key = 'next_wedding_couple_id' LIMIT 1";
        return cmd.ExecuteScalar() != null;
    }

    public static int ReadNextWeddingCoupleId(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value_bigint FROM store_meta WHERE key = 'next_wedding_couple_id'";
        object? raw = cmd.ExecuteScalar();
        if (raw is long l && l >= 1)
            return (int)l;
        return 1;
    }

    public static void SaveWeddingCouples(NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyList<WeddingCoupleRecord> couples, int nextId)
    {
        Run(conn, tx, "DELETE FROM wedding_couples");
        foreach (var row in couples.OrderBy(c => c.Id))
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                """
                INSERT INTO wedding_couples (id, player1_id, player2_id, married, affiance_date_unix_ms, wedding_date_unix_ms)
                VALUES (@id, @p1, @p2, @married, @aff, @wed)
                """;
            ins.Parameters.AddWithValue("id", row.Id);
            ins.Parameters.AddWithValue("p1", row.Player1Id);
            ins.Parameters.AddWithValue("p2", row.Player2Id);
            ins.Parameters.AddWithValue("married", row.Married);
            ins.Parameters.AddWithValue("aff", row.AffianceDateUnixMs);
            ins.Parameters.AddWithValue("wed", row.WeddingDateUnixMs);
            ins.ExecuteNonQuery();
        }

        SetMeta(conn, tx, "next_wedding_couple_id", nextId);
    }

    static void Run(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public static void PurgeWeddingCouplesForCharacter(NpgsqlConnection conn, NpgsqlTransaction? tx, int characterId)
    {
        using var del = conn.CreateCommand();
        del.Transaction = tx;
        del.CommandText = "DELETE FROM wedding_couples WHERE player1_id = @id OR player2_id = @id";
        del.Parameters.AddWithValue("id", characterId);
        del.ExecuteNonQuery();
    }
}