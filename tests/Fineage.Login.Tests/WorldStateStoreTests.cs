using Fineage.Accounts;
using Fineage.Game.Economy;
using Npgsql;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>
/// #996: <c>world_state</c> rows ride the economy commit. A document lands only with its ledger rows and bags, an older
/// revision never replaces a newer one, and the one-time sidecar import inserts the row and its record together or not
/// at all. Memory and Postgres stores keep the same rules.
/// </summary>
public sealed class WorldStateStoreTests
{
    static WorldStateDocument Doc(string system, long revision, string body = "\"x\":1") =>
        new(system, 1, revision, "{\"schemaVersion\":1," + body + "}");

    static void Contract(IAccountStore store)
    {
        store.CommitEconomy([], [], [Doc("lottery", 5, "\"n\":5")]);
        store.CommitEconomy([], [], [Doc("lottery", 3, "\"n\":3")]);
        var saved = store.LoadWorldState("lottery")!;
        Assert.Equal(5, saved.Revision);
        using (var json = System.Text.Json.JsonDocument.Parse(saved.Json))
            Assert.Equal(5, json.RootElement.GetProperty("n").GetInt32());

        Assert.Throws<ArgumentException>(() => store.CommitEconomy([], [], [new WorldStateDocument("lottery", 1, 9, "{\"schemaVersion\":2}")]));
        Assert.Equal(5, store.LoadWorldState("lottery")!.Revision);

        var import = new WorldStateImport("olympiad", "/state/olympiad.json", "abc", "/state/olympiad.json.imported-x", DateTimeOffset.UnixEpoch);
        Assert.True(store.TryImportWorldState(Doc("olympiad", 1), import));
        Assert.False(store.TryImportWorldState(Doc("olympiad", 1, "\"x\":2"), import with { SourceSha256 = "def" }));
        var record = Assert.Single(store.ListWorldStateImports());
        Assert.Equal("abc", record.SourceSha256);
        Assert.Equal(["lottery", "olympiad"], store.LoadWorldState().Select(d => d.System).Order().ToArray());
    }

    [Fact]
    public void Memory_store_keeps_the_world_state_contract() => Contract(MemoryAccountStore.InMemory());

    [Fact]
    public void Memory_store_rolls_the_document_back_with_a_failed_bag()
    {
        var store = MemoryAccountStore.InMemory();
        Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("ws", "hash", "salt", 1, null));
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
            "ws", "Ws", 0, 0, 0, out var created, items: [new StoredItem(57, 5, -1, 0, 100_001)]));

        Assert.ThrowsAny<Exception>(() => store.CommitEconomy(
            [],
            [new EconomyBagWrite("ws", created!.Id) { Inventory = [new StoredItem(57, 0, -1, 0, 100_001)] }],
            [Doc("lottery", 1)]));

        Assert.Null(store.LoadWorldState("lottery"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Postgres_store_keeps_the_world_state_contract_and_its_check()
    {
        WithSchema(url =>
        {
            using (var store = PostgresAccountStore.Open(url))
                Contract(store);

            using (var reopened = PostgresAccountStore.Open(url))
                Assert.Equal(5, reopened.LoadWorldState("lottery")!.Revision);

            var refused = Assert.Throws<PostgresException>(() => Execute(url,
                "INSERT INTO world_state(system, schema_version, revision, payload) VALUES ('x', 1, 1, '{\"schemaVersion\":2}')"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        });
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Postgres_failed_bag_write_rolls_back_the_ledger_row_and_the_document()
    {
        WithSchema(url =>
        {
            using var store = PostgresAccountStore.Open(url);
            Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("ws", "hash", "salt", 1, null));
            Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
                "ws", "Ws", 0, 0, 0, out var created, items: [new StoredItem(57, 10, -1, 0, 100_001)]));
            var journal = new MemoryLedgerJournal();
            var ledger = new EconomyLedger(journal, 100);
            var bag = LedgerAccount.Inventory(created!.Id);
            var shop = LedgerAccount.Of(SystemAccount.NpcShop);
            Assert.True(ledger.Submit(new LedgerTransaction(new LedgerTxId(10, 0), LedgerTxKind.ShopBuy, created.Id, 30001, 2000,
                [new LedgerPosting(shop, 57, -2), new LedgerPosting(bag, 57, 2)])).Ok);

            Assert.Throws<InvalidOperationException>(() => store.CommitEconomy(
                [journal.Entries[0]],
                [new EconomyBagWrite("ws", created.Id) { Inventory = [new StoredItem(57, 12, -1, 0, 100_001), new StoredItem(57, 1, -1, 0, 0)] }],
                [Doc("lottery", 1)]));

            Assert.Empty(store.OpenLedgerJournal().ReadAll());
            Assert.Null(store.LoadWorldState("lottery"));

            store.CommitEconomy(
                [journal.Entries[0]],
                [new EconomyBagWrite("ws", created.Id) { Inventory = [new StoredItem(57, 12, -1, 0, 100_001)] }],
                [Doc("lottery", 1)]);
            Assert.Single(store.OpenLedgerJournal().ReadAll());
            Assert.Equal(1, store.LoadWorldState("lottery")!.Revision);
        });
    }

    static void WithSchema(Action<string> body)
    {
        string? baseUrl = ConnectionString();
        if (baseUrl == null)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "PostgreSQL is not reachable. Start `docker compose up -d postgres` or set FINEAGE_DATABASE_URL.");
        }

        string schema = "ws_" + Guid.NewGuid().ToString("n")[..8];
        Execute(baseUrl, $"CREATE SCHEMA {schema}");
        try
        {
            body(new NpgsqlConnectionStringBuilder(baseUrl) { SearchPath = schema }.ConnectionString);
        }
        finally
        {
            Execute(baseUrl, $"DROP SCHEMA {schema} CASCADE");
        }
    }

    static void Execute(string url, string sql)
    {
        using var conn = new NpgsqlConnection(url);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static string? ConnectionString()
    {
        string url = AccountStoreFactory.ResolveConnectionString();
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
}
