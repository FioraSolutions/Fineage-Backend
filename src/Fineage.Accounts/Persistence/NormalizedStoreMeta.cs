using Fineage.Game;
using Npgsql;

namespace Fineage.Accounts;

/// <summary>Reads durable id allocation counters from normalized PG tables.</summary>
static class NormalizedStoreMeta
{
    public readonly record struct AllocationCounters(int NextCharacterId, int NextClanId, long NextItemObjectId);

    public static AllocationCounters ReadAllocationCounters(NpgsqlConnection conn)
    {
        long nextItem = Math.Max(
            ReadMeta(conn, "next_item_object_id", PlayerInventory.FirstObjectId),
            Math.Max(ReadMaxItemObjectId(conn) + 1, PlayerInventory.FirstObjectId));
        int nextCharacter = Math.Max(
            (int)ReadMeta(conn, "next_character_id", 1),
            ReadMaxCharacterId(conn) + 1);
        int nextClan = Math.Max(
            (int)ReadMeta(conn, "next_clan_id", 1),
            ReadMaxClanId(conn) + 1);
        return new AllocationCounters(nextCharacter, nextClan, nextItem);
    }

    public static void WriteAllocationCounters(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        AllocationCounters counters)
    {
        SetMeta(conn, tx, "next_character_id", counters.NextCharacterId);
        SetMeta(conn, tx, "next_clan_id", counters.NextClanId);
        SetMeta(conn, tx, "next_item_object_id", counters.NextItemObjectId);
        BumpSequence(conn, tx, "item_object_id_seq", counters.NextItemObjectId - 1);
        BumpSequence(conn, tx, "character_id_seq", counters.NextCharacterId - 1);
        BumpSequence(conn, tx, "clan_id_seq", counters.NextClanId - 1);
    }

    static long ReadMeta(NpgsqlConnection conn, string key, long fallback)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value_bigint FROM store_meta WHERE key = @key";
        cmd.Parameters.AddWithValue("key", key);
        object? raw = cmd.ExecuteScalar();
        if (raw is long value)
            return value;
        if (raw is int iv)
            return iv;
        return fallback;
    }

    static long ReadMaxItemObjectId(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(object_id), 0) FROM character_items";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    static int ReadMaxCharacterId(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(id), 0) FROM characters";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static int ReadMaxClanId(NpgsqlConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(id), 0) FROM clans";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    static void SetMeta(NpgsqlConnection conn, NpgsqlTransaction tx, string key, long value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO store_meta (key, value_bigint) VALUES (@key, @value)
            ON CONFLICT (key) DO UPDATE SET value_bigint = GREATEST(store_meta.value_bigint, EXCLUDED.value_bigint)
            """;
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", value);
        cmd.ExecuteNonQuery();
    }

    static void BumpSequence(NpgsqlConnection conn, NpgsqlTransaction tx, string sequence, long lastValue)
    {
        if (lastValue < 0)
            return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT setval(@seq::regclass, GREATEST(1, @last), true)";
        cmd.Parameters.AddWithValue("seq", sequence);
        cmd.Parameters.AddWithValue("last", lastValue);
        cmd.ExecuteNonQuery();
    }
}