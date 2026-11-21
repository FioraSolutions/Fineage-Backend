using Fineage.Game.L10n;
using Npgsql;

namespace Fineage.Accounts;

public sealed partial class PostgresAccountStore
{
    public string GetLanguage(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return PlayerLocale.Default;

        lock (_persistGate)
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                SELECT language FROM accounts
                WHERE lower(name) = lower(@name)
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("name", account.Trim());
            object? value = cmd.ExecuteScalar();
            if (value is string text)
                return PlayerLocale.Normalize(text);
        }

        return _inner.GetLanguage(account);
    }

    public bool TrySetLanguage(string account, string language)
    {
        if (string.IsNullOrWhiteSpace(account))
            return false;

        string normalized = PlayerLocale.Normalize(language);
        // The zone's in-memory snapshot can be older than Login's row. The column is the source of truth
        // across the two processes, so don't rewrite the backup JSON from this process.
        bool previous = _inner.EmitSnapshots;
        _inner.EmitSnapshots = false;
        try
        {
            _inner.TrySetLanguage(account, normalized);
        }
        finally
        {
            _inner.EmitSnapshots = previous;
        }

        lock (_persistGate)
        {
            using var conn = _dataSource.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                UPDATE accounts SET language = @language
                WHERE lower(name) = lower(@name)
                """;
            cmd.Parameters.AddWithValue("language", normalized);
            cmd.Parameters.AddWithValue("name", account.Trim());
            return cmd.ExecuteNonQuery() > 0;
        }
    }
}
