namespace Fineage.Accounts;

public sealed partial class PostgresAccountStore
{
    public IReadOnlyList<CursedWeaponRecord> ListCursedWeapons() => _inner.ListCursedWeapons();

    public bool SaveCursedWeapons(IReadOnlyList<CursedWeaponRecord> rows) =>
        RunSilent(
            () => _inner.SaveCursedWeapons(rows),
            conn =>
            {
                using var tx = conn.BeginTransaction();
                NormalizedStoreWriter.SaveCursedWeapons(conn, tx, rows);
                tx.Commit();
            });

    void HydrateCursedWeaponsFromNormalized()
    {
        lock (_persistGate)
        {
            using var conn = _dataSource.OpenConnection();
            try
            {
                var rows = NormalizedStoreWriter.LoadCursedWeapons(conn);
                if (rows.Count == 0)
                    return;
                bool previous = _inner.EmitSnapshots;
                _inner.EmitSnapshots = false;
                try
                {
                    _inner.SaveCursedWeapons(rows);
                }
                finally
                {
                    _inner.EmitSnapshots = previous;
                }
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == Npgsql.PostgresErrorCodes.UndefinedTable)
            {
            }
        }
    }
}