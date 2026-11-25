using System.Text.Json;
using Fineage.Game.Economy;
using Npgsql;
using NpgsqlTypes;

namespace Fineage.Accounts;

/// <summary>
/// <see cref="ILedgerJournal"/> on the <c>economy_ledger</c> table. Appends are buffered in memory.
/// <see cref="Flush"/> inserts that buffer in one transaction. <see cref="CommitWith"/> hands the same buffer to
/// the caller so the insert can share a transaction with the item rows; a failure puts the buffer back.
/// </summary>
public sealed class PostgresLedgerJournal : ILedgerJournal
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly NpgsqlDataSource _dataSource;
    readonly object _pendingLock = new();
    readonly object _flushLock = new();
    readonly List<LedgerEntry> _pending = [];

    internal PostgresLedgerJournal(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public int Pending
    {
        get
        {
            lock (_pendingLock)
                return _pending.Count;
        }
    }

    public int PendingFlushCount => Pending;

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
        lock (_pendingLock)
            _pending.Add(entry);
    }

    public void Flush() => CommitWith(entries =>
    {
        if (entries.Count == 0)
            return;
        using var conn = _dataSource.OpenConnection();
        using var batch = conn.BeginTransaction();
        Write(conn, batch, entries);
        batch.Commit();
    });

    /// <summary>
    /// Removes the buffered entries, calls <paramref name="write"/>, and puts them back if <paramref name="write"/> throws.
    /// The caller inserts them. This method does not open a transaction of its own.
    /// </summary>
    public void CommitWith(Action<IReadOnlyList<LedgerEntry>> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        lock (_flushLock)
        {
            List<LedgerEntry> taken;
            lock (_pendingLock)
            {
                taken = [.. _pending];
                _pending.Clear();
            }

            try
            {
                write(taken);
            }
            catch
            {
                if (taken.Count > 0)
                {
                    lock (_pendingLock)
                        _pending.InsertRange(0, taken);
                }

                throw;
            }
        }
    }

    internal static void Write(NpgsqlConnection conn, NpgsqlTransaction tx, IReadOnlyList<LedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(conn);
        ArgumentNullException.ThrowIfNull(tx);
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries)
        {
            var row = entry.Transaction;
            using var insert = conn.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText =
                """
                INSERT INTO economy_ledger (position, tick, sequence, kind, caused_by, reference, wall_unix_ms, postings, hash)
                VALUES (@position, @tick, @sequence, @kind, @caused_by, @reference, @wall, @postings, @hash)
                """;
            insert.Parameters.AddWithValue("position", entry.Position);
            insert.Parameters.AddWithValue("tick", row.Id.Tick);
            insert.Parameters.AddWithValue("sequence", row.Id.Sequence);
            insert.Parameters.AddWithValue("kind", row.Kind.ToString());
            insert.Parameters.AddWithValue("caused_by", row.CausedBy);
            insert.Parameters.AddWithValue("reference", row.Reference);
            insert.Parameters.AddWithValue("wall", row.WallUnixMs);
            insert.Parameters.AddWithValue("postings", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(row.Postings.Select(PostingRow.Of).ToList(), Json));
            insert.Parameters.AddWithValue("hash", entry.Hash);
            insert.ExecuteNonQuery();
        }
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
