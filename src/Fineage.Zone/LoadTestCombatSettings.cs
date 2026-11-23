namespace Fineage.Zone;

/// <summary>
/// When enabled, load-test accounts entering the world receive a Warlord combat profile (pole, AoE skills)
/// so perf scenarios exercise real melee/skill/death/drop fan-out (#951).
/// </summary>
public static class LoadTestCombatSettings
{
    public const string EnvVar = "FINEAGE_ZONE_LOADTEST_COMBAT";
    public const string AccountPrefixVar = "FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX";

    public const int WarlordClassId = 48;
    public const int CombatLevel = 77;
    public const int PolearmItemId = 16; // Great Spear
    public static bool Enabled => ZoneAdmin.IsEnabled(Environment.GetEnvironmentVariable(EnvVar));

    public static bool AppliesToAccount(string? account)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(account))
            return false;
        string? prefix = Environment.GetEnvironmentVariable(AccountPrefixVar)?.Trim();
        return prefix is { Length: > 0 } && account.StartsWith(prefix, StringComparison.Ordinal);
    }
}