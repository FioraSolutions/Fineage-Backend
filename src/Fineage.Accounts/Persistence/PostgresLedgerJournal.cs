using System.Text.Json;
using Fineage.Game.Economy;
using Npgsql;
using NpgsqlTypes;

namespace Fineage.Accounts;

/// <summary>
/// <see cref="ILedgerJournal"/> on the <c>economy_ledger</c> table. Appends are buffered in memory and inserted in
/// position order by <see cref="Flush"/> in one transaction; a failed flush keeps them for the next one.
/// </summary>
public sealed class PostgresLedgerJournal : ILedgerJournal
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly NpgsqlDataSource _dataSource;
    readonly object _gate = new();
    readonly object _flushGate = new();
    readonly List<LedgerEntry> _pending = [];

    internal PostgresLedgerJournal(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public int Pending
    {
        get
        {
            lock (_gate)
                return _pending.Count;
        }
    }

    public IReadOnlyList<LedgerEntry> ReadAll()
    {
        var entries = new List<LedgerEntry>();
        using var conn = _dataSource.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT position, tick, sequence, kind, caused_by, reference, wall_unix_ms, postings::text, hash
            FROM economy_ledger ORDER BY position
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var postings = JsonSerializer.Deserialize<List<PostingRow>>(reader.GetString(7), Json)
                ?? throw new InvalidOperationException($"economy ledger row {reader.GetInt64(0)} has no postings");
            var tx = new LedgerTransaction(
                new LedgerTxId(reader.GetInt64(1), reader.GetInt32(2)),
                Enum.Parse<LedgerTxKind>(reader.GetString(3)),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt64(6),
                postings.Select(row => row.ToPosting()).ToList());
            entries.Add(new LedgerEntry(reader.GetInt64(0), tx, reader.GetString(8)));
        }

        return entries;
    }

    public void Append(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
            _pending.Add(entry);
    }

    public void Flush()
    {
        lock (_flushGate)
        {
            List<LedgerEntry> taken;
            lock (_gate)
            {
                if (_pending.Count == 0)
                    return;
                taken = [.. _pending];
                _pending.Clear();
            }

            try
            {
                Insert(taken);
            }
            catch
            {
                lock (_gate)
                    _pending.InsertRange(0, taken);
                throw;
            }
        }
    }

    void Insert(List<LedgerEntry> entries)
    {
        using var conn = _dataSource.OpenConnection();
        using var batch = conn.BeginTransaction();
        foreach (var entry in entries)
        {
            var tx = entry.Transaction;
            using var insert = conn.CreateCommand();
            insert.Transaction = batch;
            insert.CommandText =
                """
                INSERT INTO economy_ledger (position, tick, sequence, kind, caused_by, reference, wall_unix_ms, postings, hash)
                VALUES (@position, @tick, @sequence, @kind, @caused_by, @reference, @wall, @postings, @hash)
                """;
            insert.Parameters.AddWithValue("position", entry.Position);
            insert.Parameters.AddWithValue("tick", tx.Id.Tick);
            insert.Parameters.AddWithValue("sequence", tx.Id.Sequence);
            insert.Parameters.AddWithValue("kind", tx.Kind.ToString());
            insert.Parameters.AddWithValue("caused_by", tx.CausedBy);
            insert.Parameters.AddWithValue("reference", tx.Reference);
            insert.Parameters.AddWithValue("wall", tx.WallUnixMs);
            insert.Parameters.AddWithValue("postings", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(tx.Postings.Select(PostingRow.Of).ToList(), Json));
            insert.Parameters.AddWithValue("hash", entry.Hash);
            insert.ExecuteNonQuery();
        }

        batch.Commit();
    }

    sealed record PostingRow(string Owner, int OwnerId, string Container, int ItemId, long Amount, int[] Instances)
    {
        public static PostingRow Of(LedgerPosting posting) => new(
            posting.Account.Owner.ToString(),
            posting.Account.OwnerId,
            posting.Account.Container.ToString(),
            posting.ItemId,
            posting.Amount,
            posting.InstanceIds.ToArray());

        public LedgerPosting ToPosting() => new(
            new LedgerAccount(Enum.Parse<LedgerOwnerKind>(Owner), OwnerId, Enum.Parse<LedgerContainer>(Container)),
            ItemId,
            Amount,
            Instances);
    }
}
