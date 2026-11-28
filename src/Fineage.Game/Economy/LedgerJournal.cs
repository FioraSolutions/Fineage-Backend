using System.Security.Cryptography;

namespace Fineage.Game.Economy;

/// <summary>
/// Append-only storage of committed transactions. <see cref="Append"/> runs inside the step and must not block on
/// I/O; a durable journal buffers and writes on <see cref="Flush"/>, which the host calls outside the step.
/// </summary>
public interface ILedgerJournal
{
    /// <summary>Every stored entry in position order, read once when the ledger starts.</summary>
    IReadOnlyList<LedgerEntry> ReadAll();

    void Append(LedgerEntry entry);

    void Flush();

    /// <summary>Buffered entries not yet written by <see cref="Flush"/>.</summary>
    int PendingFlushCount { get; }

    /// <summary>
    /// Hands the buffered entries to <paramref name="write"/> so the caller can persist them in the same
    /// transaction as the item rows. <paramref name="write"/> throwing puts the entries back in the buffer.
    /// The memory journal has already stored them in <see cref="Append"/>, so it passes an empty list and
    /// still runs <see cref="Flush"/> first.
    /// </summary>
    void CommitWith(Action<IReadOnlyList<LedgerEntry>> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        Flush();
        write([]);
    }

    /// <summary>
    /// <see cref="CommitWith"/> for the buffered entries up to and including journal <paramref name="position"/> only:
    /// the rows of the moves whose bags the caller is writing. A later move's row stays buffered for the transaction
    /// that carries its own bag, so a crash between the two never leaves a journal row without its bag (#955).
    /// A journal that stores entries on <see cref="Append"/> has nothing to hold back.
    /// </summary>
    void CommitThrough(long position, Action<IReadOnlyList<LedgerEntry>> write) => CommitWith(write);
}

public class MemoryLedgerJournal : ILedgerJournal
{
    readonly List<LedgerEntry> _entries = [];

    public IReadOnlyList<LedgerEntry> Entries => _entries;

    public IReadOnlyList<LedgerEntry> ReadAll() => _entries.ToList();

    public virtual void Append(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Add(entry);
    }

    public virtual void Flush()
    {
    }

    public virtual int PendingFlushCount => 0;
}

/// <summary>
/// Canonical encoding and the hash chain: entry <c>n</c> hashes entry <c>n-1</c>'s hash with its own transaction, so
/// editing, dropping or reordering a stored entry breaks every hash after it.
/// </summary>
public static class LedgerChain
{
    public static readonly string Genesis = new('0', 64);

    public static string Hash(string previous, LedgerTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(previous);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Convert.FromHexString(previous));
            writer.Write(Encode(tx));
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public static byte[] Encode(LedgerTransaction tx)
    {
        ArgumentNullException.ThrowIfNull(tx);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(tx.Id.Tick);
            writer.Write(tx.Id.Sequence);
            writer.Write((int)tx.Kind);
            writer.Write(tx.CausedBy);
            writer.Write(tx.Reference);
            writer.Write(tx.WallUnixMs);
            writer.Write(tx.Postings.Count);
            foreach (var posting in tx.Postings)
            {
                writer.Write((byte)posting.Account.Owner);
                writer.Write(posting.Account.OwnerId);
                writer.Write((byte)posting.Account.Container);
                writer.Write(posting.ItemId);
                writer.Write(posting.Amount);
                writer.Write(posting.InstanceIds.Count);
                foreach (int instance in posting.InstanceIds)
                    writer.Write(instance);
            }
        }

        return stream.ToArray();
    }

    /// <summary>Position of the first entry whose position or hash does not follow from the one before it, or null.</summary>
    public static long? FirstBroken(IReadOnlyList<LedgerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        string previous = Genesis;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Position != i + 1 || entry.Hash != Hash(previous, entry.Transaction))
                return entry.Position;
            previous = entry.Hash;
        }

        return null;
    }
}
