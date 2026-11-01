using Fineage.Login;
using Npgsql;
using Xunit;

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
}
