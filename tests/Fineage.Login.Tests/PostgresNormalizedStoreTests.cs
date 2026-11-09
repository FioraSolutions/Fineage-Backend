using Fineage.Login;
using Npgsql;
using Xunit;
using Fineage.Accounts;

namespace Fineage.Login.Tests;

/// <summary>Requires local docker-compose postgres (FINEAGE_DATABASE_URL or defaults).</summary>
public class PostgresNormalizedStoreTests
{
    static string? ConnectionString()
    {
        string? url = Environment.GetEnvironmentVariable("FINEAGE_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(url))
            url = AccountStoreFactory.DefaultPostgresUrl;
        try
        {
            using var conn = new NpgsqlConnection(url);
            conn.Open();
            return url;
        }
        catch
        {
            return null;
        }
    }

    [Fact]
    public void Create_presence_and_items_land_in_normalized_tables()
    {
        string? url = ConnectionString();
        if (url == null)
        {
            // Local CI without Postgres: skip without failing the suite.
            return;
        }

        string suffix = Guid.NewGuid().ToString("n")[..8];
        string account = "pgnorm" + suffix;
        string charName = "Pg" + suffix;

        using var store = PostgresAccountStore.Open(url);
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        var registered = service.Register(account, "secret1");
        Assert.True(registered.Ok, registered.Message);
        var created = service.CreateHuman(account, charName, HumanClasses.Fighter);
        Assert.True(created.Ok, created.Message);
        int characterId = created.Value!.Id;
        int swordOid = created.Value.Items!.Single(item => item.ItemId == 219).ObjectId;

        Assert.True(store.TrySavePresence(account, characterId, 100, 200, 300, DateTimeOffset.UtcNow));

        using var conn = new NpgsqlConnection(url);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT x, y, z, has_position FROM characters WHERE id = @id";
            cmd.Parameters.AddWithValue("id", characterId);
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(100, reader.GetInt32(0));
            Assert.Equal(200, reader.GetInt32(1));
            Assert.Equal(300, reader.GetInt32(2));
            Assert.True(reader.GetBoolean(3));
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                """
                SELECT object_id, loc, loc_data FROM character_items
                WHERE character_id = @id AND item_id = 219
                """;
            cmd.Parameters.AddWithValue("id", characterId);
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(swordOid, reader.GetInt64(0));
            Assert.Equal("paperdoll", reader.GetString(1));
            Assert.Equal(7, reader.GetInt32(2));
        }

        // Presence hot-path must not wipe items.
        Assert.True(store.TrySavePresence(account, characterId, 101, 201, 301, null));
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM character_items WHERE character_id = @id";
            cmd.Parameters.AddWithValue("id", characterId);
            Assert.True(Convert.ToInt64(cmd.ExecuteScalar()) >= 12);
        }
    }

    [Fact]
    public void FindById_reads_normalized_pg_as_source_of_truth_over_stale_memory()
    {
        string? url = ConnectionString();
        if (url == null)
            return;

        string suffix = Guid.NewGuid().ToString("n")[..8];
        string account = "pgcross" + suffix;
        string charName = "Xc" + suffix;

        using var store = PostgresAccountStore.Open(url);
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register(account, "secret1").Ok);
        var created = service.CreateHuman(account, charName, HumanClasses.Mystic);
        Assert.True(created.Ok, created.Message);
        int characterId = created.Value!.Id;

        // Mutate durable race without touching in-memory mirror (Zone lag scenario).
        using (var conn = new NpgsqlConnection(url))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE characters SET race = 1 WHERE id = @id";
            cmd.Parameters.AddWithValue("id", characterId);
            Assert.Equal(1, cmd.ExecuteNonQuery());
        }

        var row = store.FindById(account, characterId);
        Assert.NotNull(row);
        Assert.Equal(1, row!.Race);
        Assert.Equal(HumanClasses.Mystic, row.ClassId);
        Assert.True(row.Items is { Count: > 0 });
    }

    [Fact]
    public void Shortcuts_land_in_character_shortcuts_and_reload()
    {
        string? url = ConnectionString();
        if (url == null)
            return;

        string suffix = Guid.NewGuid().ToString("n")[..8];
        string account = "pgbar" + suffix;
        int characterId;
        using (var store = PostgresAccountStore.Open(url))
        {
            var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
            Assert.True(service.Register(account, "secret1").Ok);
            var created = service.CreateHuman(account, "Bar" + suffix, HumanClasses.Fighter);
            Assert.True(created.Ok, created.Message);
            characterId = created.Value!.Id;
            Assert.True(store.TryReplaceShortcuts(account, characterId,
            [
                new StoredShortcut(0, 0, StoredShortcut.TypeSkill, 3, 1),
                new StoredShortcut(2, 1, StoredShortcut.TypeAction, 2, 0)
            ]));
        }

        using (var conn = new NpgsqlConnection(url))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM character_shortcuts WHERE character_id = @id";
            cmd.Parameters.AddWithValue("id", characterId);
            Assert.Equal(2L, (long)cmd.ExecuteScalar()!);
        }

        using var reopened = PostgresAccountStore.Open(url);
        var rows = reopened.FindById(account, characterId)!.Shortcuts!;
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, s => s.WorldSlot == 14 && s.Type == StoredShortcut.TypeAction);
    }

    [Fact]
    public void Open_does_not_reproject_stale_snapshot_over_live_normalized_rows()
    {
        string? url = ConnectionString();
        if (url == null)
            return;

        string suffix = Guid.NewGuid().ToString("n")[..8];
        string account = "pgopen" + suffix;
        int characterId;
        long itemCount;
        using (var store = PostgresAccountStore.Open(url))
        {
            var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
            Assert.True(service.Register(account, "secret1").Ok);
            var created = service.CreateHuman(account, "Op" + suffix, HumanClasses.Fighter);
            Assert.True(created.Ok, created.Message);
            characterId = created.Value!.Id;
            // Simulate Zone hot-path inventory write: SQL rows change, store_snapshot stays stale.
            Assert.True(store.TryReplaceItems(account, characterId,
            [
                new StoredItem(57, 100, EquippedSlot: -1)
            ]));
        }

        using (var conn = new NpgsqlConnection(url))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM character_items WHERE character_id = @id";
            cmd.Parameters.AddWithValue("id", characterId);
            itemCount = (long)cmd.ExecuteScalar()!;
            Assert.Equal(1L, itemCount);
        }

        // Re-open must keep the hot-path SQL row (not wipe from the stale JSON backup).
        using var reopened = PostgresAccountStore.Open(url);
        var row = reopened.FindById(account, characterId);
        Assert.NotNull(row);
        Assert.NotNull(row!.Items);
        Assert.Single(row.Items!);
        Assert.Equal(57, row.Items![0].ItemId);
        Assert.Equal(100, row.Items[0].Count);
    }
}
