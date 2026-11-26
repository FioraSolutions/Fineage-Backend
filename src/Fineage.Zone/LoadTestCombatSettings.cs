namespace Fineage.Zone;

/// <summary>
/// When enabled, load-test accounts entering the world receive a Warlord combat profile (pole, AoE skills)
/// so perf scenarios exercise real melee/skill/death/drop fan-out (#951).
/// </summary>
public static class LoadTestCombatSettings
{
    public const string EnvVar = "FINEAGE_ZONE_LOADTEST_COMBAT";
    public const string AccountPrefixVar = "FINEAGE_LOGIN_LOADTEST_ACCOUNT_PREFIX";

    /// <summary>Interlude Warlord (Human Fighter → Warrior → Warlord). 48 is Tyrant and learns no pole AoE.</summary>
    public const int WarlordClassId = 3;
    public const int CombatLevel = 77;
    public const int PolearmItemId = 16; // Great Spear

    /// <summary>Whirlwind (36) and Thunder Storm (48): the pole AoE rotation the perf scenarios cast.</summary>
    public static readonly IReadOnlyList<int> RequiredSkillIds = [36, 48];

    /// <summary>
    /// Adena the profile grants when the bag has none. A levelled load bot must be able to pay a gatekeeper fee,
    /// buy from a shop and post a trade, or every economy flow is refused for funds and the run measures refusal
    /// paths instead of the pipeline. Matches what a real character of this level carries, not a blank purse.
    /// </summary>
    public const int StartingAdena = 10_000_000;
    public static bool Enabled => ZoneAdmin.IsEnabled(Environment.GetEnvironmentVariable(EnvVar));

    /// <summary>
    /// <c>FINEAGE_ZONE_LOADTEST_TELEPORT=1</c> lets load-test accounts (and only those) send
    /// <c>TeleportToLocation</c> so harness flows can stand at a hub, a manor or an arena corner.
    /// Off by default; every other session still gets <c>unknown_opcode</c>.
    /// </summary>
    public const string TeleportEnvVar = "FINEAGE_ZONE_LOADTEST_TELEPORT";

    public static bool TeleportEnabled => ZoneAdmin.IsEnabled(Environment.GetEnvironmentVariable(TeleportEnvVar));

    public static bool AppliesToAccount(string? account) => Enabled && IsLoadTestAccount(account);

    public static bool AllowsTeleport(string? account) => TeleportEnabled && IsLoadTestAccount(account);

    static bool IsLoadTestAccount(string? account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return false;
        string? prefix = Environment.GetEnvironmentVariable(AccountPrefixVar)?.Trim();
        return prefix is { Length: > 0 } && account.StartsWith(prefix, StringComparison.Ordinal);
    }
}