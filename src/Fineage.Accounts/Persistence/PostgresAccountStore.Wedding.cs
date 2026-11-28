using Npgsql;

namespace Fineage.Accounts;

public sealed partial class PostgresAccountStore
{
    public int NextWeddingCoupleId => _inner.NextWeddingCoupleId;

    public IReadOnlyList<WeddingCoupleRecord> ListWeddingCouples() => _inner.ListWeddingCouples();

    public bool SaveWeddingCouples(IReadOnlyList<WeddingCoupleRecord> couples, int nextCoupleId) =>
        RunSilent(
            () => _inner.SaveWeddingCouples(couples, nextCoupleId),
            conn =>
            {
                using var tx = conn.BeginTransaction();
                NormalizedStoreWriter.SaveWeddingCouples(conn, tx, couples, nextCoupleId);
                tx.Commit();
            });

    public bool PurgeWeddingCouplesForCharacter(int characterId) =>
        RunSilent(
            () => _inner.PurgeWeddingCouplesForCharacter(characterId),
            conn => NormalizedStoreWriter.PurgeWeddingCouplesForCharacter(conn, null, characterId));

    void HydrateWeddingFromNormalized()
    {
        using (EnterPersist())
        {
            using var conn = _dataSource.OpenConnection();
            var rows = NormalizedStoreWriter.LoadWeddingCouples(conn);
            if (rows.Count == 0 && !NormalizedStoreWriter.HasWeddingMeta(conn))
                return;
            int next = NormalizedStoreWriter.ReadNextWeddingCoupleId(conn);
            bool previous = _inner.EmitSnapshots;
            _inner.EmitSnapshots = false;
            try
            {
                _inner.SaveWeddingCouples(rows, next);
            }
            finally
            {
                _inner.EmitSnapshots = previous;
            }
        }
    }
}