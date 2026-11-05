namespace Fineage.Login;

/// <summary>
/// Opens the account store. PostgreSQL is the only store: characters, items, quests, skills, and
/// shortcuts live in normalized tables. There is no file-backed mode and no <c>FINEAGE_STORE</c> switch.
/// </summary>
public static class AccountStoreFactory
{
    public const string DatabaseUrlEnv = "FINEAGE_DATABASE_URL";
    public const string DefaultPostgresUrl =
        "Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage";

    /// <summary>Connection string to use: the argument, then <see cref="DatabaseUrlEnv"/>, then <see cref="DefaultPostgresUrl"/>.</summary>
    public static string ResolveConnectionString(string? explicitUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrl))
            return explicitUrl.Trim();
        string? url = Environment.GetEnvironmentVariable(DatabaseUrlEnv);
        return string.IsNullOrWhiteSpace(url) ? DefaultPostgresUrl : url.Trim();
    }

    /// <summary>
    /// Opens PostgreSQL and applies pending migrations. Throws <see cref="InvalidOperationException"/>
    /// with a readable message when the database is unreachable, so a boot failure says what to fix.
    /// </summary>
    public static PostgresAccountStore Open(string? connectionString = null)
    {
        string url = ResolveConnectionString(connectionString);
        try
        {
            return PostgresAccountStore.Open(url);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"PostgreSQL account store unavailable ({Describe(url)}). " +
                $"Start it with `docker compose up -d postgres` or point {DatabaseUrlEnv} at a reachable database. " +
                $"Underlying error: {ex.Message}",
                ex);
        }
    }

    /// <summary>Connection string with the password removed, safe for logs.</summary>
    static string Describe(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
                continue;
            string key = part[..eq].Trim();
            if (key.Equals("Password", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("Pwd", StringComparison.OrdinalIgnoreCase))
                continue;
            kept.Add(part);
        }

        return string.Join(';', kept);
    }
}
