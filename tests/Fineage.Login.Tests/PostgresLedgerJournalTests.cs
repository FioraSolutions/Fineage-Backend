using Fineage.Accounts;
using Fineage.Game.Economy;
using Npgsql;
using Xunit;

namespace Fineage.Login.Tests;

/// <summary>Requires local docker-compose postgres (FINEAGE_DATABASE_URL or defaults); each run uses its own schema.</summary>
[Trait("Category", "Integration")]
public sealed class PostgresLedgerJournalTests
{
    static readonly LedgerAccount Shop = LedgerAccount.Of(SystemAccount.NpcShop);
    static readonly LedgerAccount Bag = LedgerAccount.Inventory(77);

    [Fact]
    public void Flushed_entries_replay_into_the_same_ledger_after_a_restart()
    {
        string? baseUrl = ConnectionString();
        if (baseUrl == null)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "PostgreSQL is not reachable. Start `docker compose up -d postgres` or set FINEAGE_DATABASE_URL.");
        }

        string schema = "ledger_" + Guid.NewGuid().ToString("n")[..8];
        Execute(baseUrl, $"CREATE SCHEMA {schema}");
        try
        {
            string url = new NpgsqlConnectionStringBuilder(baseUrl) { SearchPath = schema }.ConnectionString;
            string head;
            using (var store = PostgresAccountStore.Open(url))
            {
                var journal = store.OpenLedgerJournal();
                var ledger = new EconomyLedger(journal, 100);
                Assert.True(ledger.Submit(Tx(10, 0, new(Shop, 57, -500), new(Bag, 57, 500))).Ok);
                Assert.True(ledger.Submit(Tx(10, 1, new(Shop, 2369, -1), new(Bag, 2369, 1, [9001]))).Ok);
                Assert.Equal(2, journal.Pending);
                Assert.Empty(store.OpenLedgerJournal().ReadAll());

                ledger.Flush();
                Assert.Equal(0, journal.Pending);
                Assert.True(ledger.Submit(Tx(11, 0, new(Bag, 57, -40), new(Shop, 57, 40))).Ok);
                ledger.Flush();
                head = ledger.Head;
            }

            using (var reopened = PostgresAccountStore.Open(url))
            {
                var stored = reopened.OpenLedgerJournal().ReadAll();
                Assert.Equal([1L, 2L, 3L], stored.Select(entry => entry.Position));
                Assert.Equal([9001], stored[1].Transaction.Postings[1].InstanceIds);

                var ledger = new EconomyLedger(reopened.OpenLedgerJournal(), 100);
                Assert.Equal(head, ledger.Head);
                Assert.Equal(460, ledger.HoldingsOf(Bag).CountOf(57));
                Assert.Equal(Bag, ledger.OwnerOf(9001));
                Assert.Equal(LedgerOutcome.Duplicate, ledger.Submit(stored[2].Transaction).Outcome);
            }

            var appendOnly = Assert.Throws<PostgresException>(() =>
                Execute(url, "UPDATE economy_ledger SET postings = jsonb_set(postings, '{1,amount}', '400') WHERE position = 1"));
            Assert.Contains("append-only", appendOnly.MessageText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Execute(baseUrl, $"DROP SCHEMA {schema} CASCADE");
        }
    }

    /// <summary>#955: a batch commits the buffered prefix its bags depend on; later rows wait for their own batch.</summary>
    [Fact]
    public void CommitThrough_writes_the_buffered_prefix_and_keeps_later_rows()
    {
        string? baseUrl = ConnectionString();
        if (baseUrl == null)
        {
            throw Xunit.Sdk.SkipException.ForSkip(
                "PostgreSQL is not reachable. Start `docker compose up -d postgres` or set FINEAGE_DATABASE_URL.");
        }

        string schema = "ledger_" + Guid.NewGuid().ToString("n")[..8];
        Execute(baseUrl, $"CREATE SCHEMA {schema}");
        try
        {
            string url = new NpgsqlConnectionStringBuilder(baseUrl) { SearchPath = schema }.ConnectionString;
            using var store = PostgresAccountStore.Open(url);
            var journal = store.OpenLedgerJournal();
            var ledger = new EconomyLedger(journal, 100);
            Assert.True(ledger.Submit(Tx(10, 0, new(Shop, 57, -500), new(Bag, 57, 500))).Ok);
            long mark = ledger.Position;
            Assert.True(ledger.Submit(Tx(10, 1, new(Bag, 57, -40), new(Shop, 57, 40))).Ok);

            Assert.Throws<InvalidOperationException>(() =>
                journal.CommitThrough(mark, _ => throw new InvalidOperationException("crash inside the transaction")));
            Assert.Equal(2, journal.Pending);

            List<long> written = [];
            journal.CommitThrough(mark, entries =>
            {
                written.AddRange(entries.Select(entry => entry.Position));
                using var conn = new NpgsqlConnection(url);
                conn.Open();
                using var tx = conn.BeginTransaction();
                PostgresLedgerJournal.Write(conn, tx, entries);
                tx.Commit();
            });
            Assert.Equal([mark], written);
            Assert.Equal(1, journal.Pending);
            Assert.Equal([mark], store.OpenLedgerJournal().ReadAll().Select(entry => entry.Position));
            ledger.Flush();
            Assert.Equal([mark, mark + 1], store.OpenLedgerJournal().ReadAll().Select(entry => entry.Position));
        }
        finally
        {
            Execute(baseUrl, $"DROP SCHEMA {schema} CASCADE");
        }
    }

    static LedgerTransaction Tx(long tick, int sequence, params LedgerPosting[] postings) =>
        new(new LedgerTxId(tick, sequence), LedgerTxKind.ShopBuy, 77, 30001, tick * 200, postings);

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
