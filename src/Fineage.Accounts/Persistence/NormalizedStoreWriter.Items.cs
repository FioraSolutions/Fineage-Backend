using System.Globalization;
using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Accounts;

static partial class NormalizedStoreWriter
{
    public static void ReplaceItems(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<StoredItem>? inventory,
        IReadOnlyList<StoredItem>? warehouse)
    {
        // Caller may have already wiped all items during a full snapshot upsert.
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_items WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (inventory != null)
        {
            foreach (var item in inventory)
                InsertItem(conn, tx, characterId, item, bagLoc: LocInventory);
        }

        if (warehouse != null)
        {
            foreach (var item in warehouse)
                InsertItem(conn, tx, characterId, item, bagLoc: LocWarehouse);
        }
    }

    public static void ReplaceInventoryOnly(
        NpgsqlConnection conn,
        int characterId,
        IReadOnlyList<StoredItem> inventory)
    {
        using var tx = conn.BeginTransaction();
        DeleteItemsByObjectIds(conn, tx, inventory);
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_items WHERE character_id = @id AND loc <> @wh";
            del.Parameters.AddWithValue("id", characterId);
            del.Parameters.AddWithValue("wh", LocWarehouse);
            del.ExecuteNonQuery();
        }

        foreach (var item in inventory)
            InsertItem(conn, tx, characterId, item, bagLoc: LocInventory);
        tx.Commit();
    }

    public static void ReplaceWarehouseOnly(
        NpgsqlConnection conn,
        int characterId,
        IReadOnlyList<StoredItem> warehouse)
    {
        using var tx = conn.BeginTransaction();
        DeleteItemsByObjectIds(conn, tx, warehouse);
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_items WHERE character_id = @id AND loc = @wh";
            del.Parameters.AddWithValue("id", characterId);
            del.Parameters.AddWithValue("wh", LocWarehouse);
            del.ExecuteNonQuery();
        }

        foreach (var item in warehouse)
            InsertItem(conn, tx, characterId, item, bagLoc: LocWarehouse);
        tx.Commit();
    }

    static void DeleteItemsByObjectIds(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        IReadOnlyList<StoredItem> items)
    {
        foreach (var item in items)
        {
            if (item.ObjectId <= 0)
                continue;
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_items WHERE object_id = @oid";
            del.Parameters.AddWithValue("oid", (long)item.ObjectId);
            del.ExecuteNonQuery();
        }
    }

    static void DeleteMissingCharacters(NpgsqlConnection conn, NpgsqlTransaction tx, HashSet<int> keep)
    {
        using var list = conn.CreateCommand();
        list.Transaction = tx;
        list.CommandText = "SELECT id FROM characters";
        var drop = new List<int>();
        using (var reader = list.ExecuteReader())
        {
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                if (!keep.Contains(id))
                    drop.Add(id);
            }
        }

        foreach (int id in drop)
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM characters WHERE id = @id";
            del.Parameters.AddWithValue("id", id);
            del.ExecuteNonQuery();
        }
    }
}
