using System.Text.Json;
using Npgsql;

namespace Fineage.Accounts;

/// <summary>
/// Pets: the <c>pets</c> table is the source of truth (Mobius <c>pets</c>, keyed by the collar's object id). Writes
/// go through the in-memory store silently (no snapshot rewrite, see <c>RunSilent</c>) and then to SQL; reads hit SQL
/// so a collar traded between characters on any process finds the same pet.
/// </summary>
public sealed partial class PostgresAccountStore
{
    public StoredPet? FindPet(int controlObjectId)
    {
        if (controlObjectId <= 0)
            return null;
        using (EnterPersist())
        {
            using var conn = _dataSource.OpenConnection();
            return NormalizedStoreWriter.LoadPet(conn, controlObjectId);
        }
    }

    public bool SavePet(StoredPet pet) =>
        RunSilent(() => _inner.SavePet(pet), conn => NormalizedStoreWriter.UpsertPet(conn, null, pet));

    public bool DeletePet(int controlObjectId) =>
        RunSilent(() =>
        {
            _inner.DeletePet(controlObjectId);
            return controlObjectId > 0;
        }, conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM pets WHERE control_object_id = @id";
            cmd.Parameters.AddWithValue("id", controlObjectId);
            cmd.ExecuteNonQuery();
        });

    public bool PetNameExists(string name, int npcId, int exceptControlObjectId)
    {
        if (string.IsNullOrEmpty(name) || npcId <= 0)
            return false;
        using (EnterPersist())
        {
            using var conn = _dataSource.OpenConnection();
            return NormalizedStoreWriter.PetNameExists(conn, name, npcId, exceptControlObjectId);
        }
    }
}

static partial class NormalizedStoreWriter
{
    static readonly JsonSerializerOptions PetJson = new(JsonSerializerDefaults.Web);

    public static void UpsertPet(NpgsqlConnection conn, NpgsqlTransaction? tx, StoredPet pet)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO pets (control_object_id, owner_character_id, npc_id, name, level, exp, cur_hp, cur_mp, fed, items, updated_at) " +
            "VALUES (@id, @owner, @npc, @name, @level, @exp, @hp, @mp, @fed, @items::jsonb, now()) " +
            "ON CONFLICT (control_object_id) DO UPDATE SET owner_character_id = excluded.owner_character_id, " +
            "npc_id = excluded.npc_id, name = excluded.name, level = excluded.level, exp = excluded.exp, " +
            "cur_hp = excluded.cur_hp, cur_mp = excluded.cur_mp, fed = excluded.fed, items = excluded.items, updated_at = now()";
        cmd.Parameters.AddWithValue("id", pet.ControlObjectId);
        cmd.Parameters.AddWithValue("owner", pet.OwnerCharacterId);
        cmd.Parameters.AddWithValue("npc", pet.NpcId);
        cmd.Parameters.AddWithValue("name", pet.Name ?? "");
        cmd.Parameters.AddWithValue("level", pet.Level);
        cmd.Parameters.AddWithValue("exp", pet.Exp);
        cmd.Parameters.AddWithValue("hp", pet.CurHp);
        cmd.Parameters.AddWithValue("mp", pet.CurMp);
        cmd.Parameters.AddWithValue("fed", pet.Fed);
        cmd.Parameters.AddWithValue("items", JsonSerializer.Serialize(pet.Items ?? [], PetJson));
        cmd.ExecuteNonQuery();
    }

    public static StoredPet? LoadPet(NpgsqlConnection conn, int controlObjectId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT owner_character_id, npc_id, name, level, exp, cur_hp, cur_mp, fed, items::text FROM pets WHERE control_object_id = @id";
        cmd.Parameters.AddWithValue("id", controlObjectId);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        var items = JsonSerializer.Deserialize<List<StoredItem>>(reader.GetString(8), PetJson) ?? [];
        return new StoredPet(controlObjectId, reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
            reader.GetInt64(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), items);
    }

    public static bool PetNameExists(NpgsqlConnection conn, string name, int npcId, int exceptControlObjectId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT 1 FROM pets WHERE name = @name AND npc_id = @npc AND control_object_id <> @except LIMIT 1";
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("npc", npcId);
        cmd.Parameters.AddWithValue("except", exceptControlObjectId);
        using var reader = cmd.ExecuteReader();
        return reader.Read();
    }

    /// <summary>Bootstrap / recovery: the snapshot's pets into <c>pets</c> (rows already there are replaced).</summary>
    static void SyncPets(NpgsqlConnection conn, NpgsqlTransaction tx, JsonElement root)
    {
        if (!root.TryGetProperty("pets", out var pets) || pets.ValueKind != JsonValueKind.Array)
            return;
        foreach (var row in pets.EnumerateArray())
        {
            var pet = JsonSerializer.Deserialize<StoredPet>(row.GetRawText(), PetJson);
            if (pet is { ControlObjectId: > 0, NpcId: > 0 })
                UpsertPet(conn, tx, pet with { Items = pet.Items ?? [] });
        }
    }
}
