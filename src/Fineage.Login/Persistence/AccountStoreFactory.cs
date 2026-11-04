namespace Fineage.Login;

/// <summary>
/// Opens the durable account store. Default is JSON file; set <c>FINEAGE_STORE=postgres</c>
/// and <c>FINEAGE_DATABASE_URL</c> for PostgreSQL (Net foundation).
/// </summary>
public static class AccountStoreFactory
{
    public const string StoreEnv = "FINEAGE_STORE";
    public const string DatabaseUrlEnv = "FINEAGE_DATABASE_URL";
    public const string DefaultPostgresUrl =
        "Host=127.0.0.1;Port=5432;Database=fineage;Username=fineage;Password=fineage";

    public static IAccountStore Open(string? jsonStorePath = null)
    {
        string mode = Environment.GetEnvironmentVariable(StoreEnv)?.Trim() ?? "json";
        if (string.Equals(mode, "postgres", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mode, "pg", StringComparison.OrdinalIgnoreCase))
        {
            string? url = Environment.GetEnvironmentVariable(DatabaseUrlEnv);
            if (string.IsNullOrWhiteSpace(url))
                url = DefaultPostgresUrl;
            return PostgresAccountStore.Open(url!);
        }

        string path = jsonStorePath
            ?? Environment.GetEnvironmentVariable("FINEAGE_LOGIN_STORE")
            ?? Path.Combine("data", "accounts.json");
        return JsonAccountStore.Open(path);
    }
}
