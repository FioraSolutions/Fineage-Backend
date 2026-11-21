using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
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
    public void Create_after_reopen_allocates_unique_item_object_ids()
    {
        string? url = ConnectionString();
        if (url == null)
            return;

        string suffix = Guid.NewGuid().ToString("n")[..8];
        string account = "pgoid" + suffix;
        using (var store = PostgresAccountStore.Open(url))
        {
            var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
            Assert.True(service.Register(account, "secret1").Ok);
            var created = service.CreateHuman(account, "A" + suffix, HumanClasses.Fighter);
            Assert.True(created.Ok, created.Message);
        }

        // Lagging store_meta mimics a stale store_snapshot allocator after durable items landed in SQL.
        using (var conn = new NpgsqlConnection(url))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE store_meta SET value_bigint = @low WHERE key = 'next_item_object_id'";
            cmd.Parameters.AddWithValue("low", PlayerInventory.FirstObjectId);
            Assert.Equal(1, cmd.ExecuteNonQuery());
        }

        using var reopened = PostgresAccountStore.Open(url);
        var service2 = new LoginService(reopened, new HandoffTokens(HandoffTokens.DefaultSecret));
        var created2 = service2.CreateHuman(account, "B" + suffix, HumanClasses.Fighter);
        Assert.True(created2.Ok, created2.Message);

        using (var conn = new NpgsqlConnection(url))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), COUNT(DISTINCT object_id) FROM character_items";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(reader.GetInt64(0), reader.GetInt64(1));
            Assert.True(reader.GetInt64(0) >= 24);
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
    public void Item_custom_types_roundtrip_through_postgres_inventory()
    {
        string? url = ConnectionString();
        if (url == null)
            return;

        string suffix = Guid.NewGuid().ToString("n")[..8];
        string account = "pgct" + suffix;
        int characterId;
        using (var store = PostgresAccountStore.Open(url))
        {
            var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
            Assert.True(service.Register(account, "secret1").Ok);
            var created = service.CreateHuman(account, "Ct" + suffix, HumanClasses.Fighter);
            Assert.True(created.Ok, created.Message);
            characterId = created.Value!.Id;
            Assert.True(store.TryReplaceItems(account, characterId,
            [
                new StoredItem(MonsterRaceProtocol.TicketItemId, 1, -1, EnchantLevel: 15, ObjectId: 880_100, CustomType1: 4,
                    CustomType2: 100)
            ]));
        }

        using var reopened = PostgresAccountStore.Open(url);
        var ticket = Assert.Single(reopened.FindById(account, characterId)!.Items!);
        Assert.Equal(MonsterRaceProtocol.TicketItemId, ticket.ItemId);
        Assert.Equal(4, ticket.CustomType1);
        Assert.Equal(100, ticket.CustomType2);
        Assert.Equal(15, ticket.EnchantLevel);
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

    [Fact]
    public void Login_snapshot_backup_does_not_reproject_over_zone_inventory_rows()
    {
        string? url = ConnectionString();
        if (url == null)
            return;

        string suffix = Guid.NewGuid().ToString("n")[..8];
        string account = "pgbak" + suffix;
        using var store = PostgresAccountStore.Open(url);
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register(account, "secret1").Ok);
        var created = service.CreateHuman(account, "Bk" + suffix, HumanClasses.Fighter);
        Assert.True(created.Ok, created.Message);
        int characterId = created.Value!.Id;

        Assert.True(store.TryReplaceItems(account, characterId,
        [
            new StoredItem(57, 42, EquippedSlot: -1)
        ]));

        // Login mutation that still emits AfterSaveSnapshot (friends JSON mirror) must not
        // UpsertFromSnapshot and wipe the Zone hot-path inventory SoT.
        Assert.True(store.TryReplaceFriends(account, characterId, []));

        var row = store.FindById(account, characterId);
        Assert.NotNull(row);
        Assert.Single(row!.Items!);
        Assert.Equal(57, row.Items![0].ItemId);
        Assert.Equal(42, row.Items[0].Count);
    }

    [Fact]
    public void Hard_delete_removes_character_rows_from_sql()
    {
        string? url = ConnectionString();
        if (url == null)
            return;

        string suffix = Guid.NewGuid().ToString("n")[..8];
        string account = "pgdel" + suffix;
        using var store = PostgresAccountStore.Open(url);
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register(account, "secret1").Ok);
        var created = service.CreateHuman(account, "Dl" + suffix, HumanClasses.Fighter);
        Assert.True(created.Ok, created.Message);
        int characterId = created.Value!.Id;
        Assert.True(store.TryReplaceShortcuts(account, characterId, [
            new StoredShortcut(0, 0, StoredShortcut.TypeItem, 57, 1)
        ]));
        Assert.True(store.SavePet(new StoredPet(
            characterId * 10 + 1, characterId, 12077, "Wolf", 1, 0, 100, 100, 10, [])));

        var deleted = service.Delete(account, characterId: characterId);
        Assert.True(deleted.Ok, deleted.Message);
        Assert.True(deleted.Value!.HardDeleted);
        Assert.Null(store.FindById(account, characterId));

        using var conn = new NpgsqlConnection(url);
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT count(*) FROM characters WHERE id = @id";
            cmd.Parameters.AddWithValue("id", characterId);
            Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT count(*) FROM character_items WHERE character_id = @id";
            cmd.Parameters.AddWithValue("id", characterId);
            Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT count(*) FROM character_shortcuts WHERE character_id = @id";
            cmd.Parameters.AddWithValue("id", characterId);
            Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT count(*) FROM pets WHERE owner_character_id = @id";
            cmd.Parameters.AddWithValue("id", characterId);
            Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
        }

        // A reopen must not resurrect the row from a stale snapshot backup.
        using var reopened = PostgresAccountStore.Open(url);
        Assert.Null(reopened.FindById(account, characterId));
    }
}
