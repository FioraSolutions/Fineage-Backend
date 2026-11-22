using Fineage.Accounts;
using Fineage.Game.Economy;
using Npgsql;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>
/// The ledger insert and the item replace share one commit. A failure of the item write keeps both the
/// ledger table and the bag at the previous rows.
/// </summary>
public sealed class EconomyCommitTests
{
    static readonly LedgerAccount Shop = LedgerAccount.Of(SystemAccount.NpcShop);

    [Fact]
    public void A_failed_second_bag_write_rolls_the_first_bag_back()
    {
        var store = MemoryAccountStore.InMemory();
        Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("eco", "hash", "salt", 1, null));
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
            "eco", "One", 0, 0, 0, out var first, items: [new StoredItem(57, 5, -1, 0, 100_001)]));
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
            "eco", "Two", 0, 0, 0, out var second, items: [new StoredItem(57, 5, -1, 0, 100_002)]));
        Assert.NotNull(first);
        Assert.NotNull(second);

        var failed = Assert.Throws<ArgumentOutOfRangeException>(() => store.CommitEconomy(
            [],
            [
                new EconomyBagWrite("eco", first.Id) { Inventory = [new StoredItem(57, 9, -1, 0, 100_001)] },
                new EconomyBagWrite("eco", second.Id) { Inventory = [new StoredItem(57, 0, -1, 0, 100_002)] },
            ]));
        Assert.Contains("count", failed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(5, store.FindById("eco", first.Id)!.Items!.Single().Count);
        Assert.Equal(5, store.FindById("eco", second.Id)!.Items!.Single().Count);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void Item_replace_failure_rolls_back_the_ledger_row_and_the_bag()
    {
        string? baseUrl = ConnectionString();
        if (baseUrl == null)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "PostgreSQL is not reachable. Start `docker compose up -d postgres` or set FINEAGE_DATABASE_URL.");
        }

        string schema = "econ_" + Guid.NewGuid().ToString("n")[..8];
        Execute(baseUrl, $"CREATE SCHEMA {schema}");
        try
        {
            string url = new NpgsqlConnectionStringBuilder(baseUrl) { SearchPath = schema }.ConnectionString;
            using var store = PostgresAccountStore.Open(url);
            Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("eco", "hash", "salt", 1, null));
            Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
                "eco", "Eco", 0, 0, 0, out var created, items: [new StoredItem(57, 10, -1, 0, 100_001)]));
            Assert.NotNull(created);

            var journal = new MemoryLedgerJournal();
            var ledger = new EconomyLedger(journal, 100);
            var bag = LedgerAccount.Inventory(created.Id);
            Assert.True(ledger.Submit(Tx(created.Id, 10, 0, new(Shop, 57, -15), new(bag, 57, 15))).Ok);
            Assert.True(ledger.Submit(Tx(created.Id, 10, 1, new(Shop, 57, -5), new(bag, 57, 5))).Ok);

            store.CommitEconomy(
                [journal.Entries[0]],
                [new EconomyBagWrite("eco", created.Id) { Inventory = [new StoredItem(57, 25, -1, 0, 100_001)] }]);
            Assert.Equal([1L], store.OpenLedgerJournal().ReadAll().Select(entry => entry.Position));
            Assert.Equal(25, store.FindById("eco", created.Id)!.Items!.Single().Count);

            var failed = Assert.Throws<InvalidOperationException>(() => store.CommitEconomy(
                [journal.Entries[1]],
                [new EconomyBagWrite("eco", created.Id)
                {
                    Inventory =
                    [
                        new StoredItem(57, 1, -1, 0, 100_002),
                        new StoredItem(57, 1, -1, 0, 0),
                    ],
                }]));
            Assert.Contains("missing object_id", failed.Message, StringComparison.Ordinal);

            var durable = store.OpenLedgerJournal().ReadAll();
            Assert.Equal([1L], durable.Select(entry => entry.Position));
            var items = store.FindById("eco", created.Id)!.Items!;
            var kept = Assert.Single(items);
            Assert.Equal(100_001, kept.ObjectId);
            Assert.Equal(25, kept.Count);
            Assert.Equal(0, CountObject(url, 100_002));
        }
        finally
        {
            Execute(baseUrl, $"DROP SCHEMA {schema} CASCADE");
        }
    }

    static LedgerTransaction Tx(int characterId, long tick, int sequence, params LedgerPosting[] postings) =>
        new(new LedgerTxId(tick, sequence), LedgerTxKind.ShopBuy, characterId, 30001, tick * 200, postings);

    static int CountObject(string url, int objectId)
    {
        using var conn = new NpgsqlConnection(url);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM character_items WHERE object_id = @id";
        cmd.Parameters.AddWithValue("id", (long)objectId);
        return Convert.ToInt32(cmd.ExecuteScalar());
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
