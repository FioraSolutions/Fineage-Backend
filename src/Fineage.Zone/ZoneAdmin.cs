using System.Text.Json;
using Fineage.Game;
using Fineage.Zone.Security;

namespace Fineage.Zone;

/// <summary>
/// Debug/admin gate: <c>FINEAGE_ZONE_ADMIN</c> must be on, and the character must have a role in
/// <c>data/admin-characters.json</c>. The legacy <c>{"names":["KuK"]}</c> file still grants Admin; a new
/// optional <c>{"roles":{"Helper":"helper","Gm":"gm","KuK":"admin"}}</c> map grants the explicit hierarchy.
/// A wildcard <c>*</c> is accepted only outside production; production logs a concrete error and denies
/// every wildcard character. A missing / malformed allowlist is fail-closed (no default KuK name).
/// </summary>
public static class ZoneAdmin
{
    public const string EnvVar = "FINEAGE_ZONE_ADMIN";
    public const string CharactersFileName = "admin-characters.json";
    public const string AnyCharacter = AdminPolicy.AnyCharacter;

    static readonly object Gate = new();
    static AdminPolicy? _policy;
    static string? _loadedFrom;

    public static bool Enabled => IsEnabled(System.Environment.GetEnvironmentVariable(EnvVar));

    public static bool IsEnabled(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var token = text.Trim();
        return token == "1"
            || token.Equals("true", StringComparison.OrdinalIgnoreCase)
            || token.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public static DeploymentEnvironment Environment => AdminPolicy.ResolveEnvironment();

    /// <summary>Resolved role for a character. Disabled admin env or no policy entry means Player.</summary>
    public static AdminRole RoleOf(string? characterName)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(characterName))
            return AdminRole.Player;
        EnsurePolicy();
        return _policy!.RoleOf(characterName);
    }

    /// <summary>Admin role (the top of the hierarchy).</summary>
    public static bool IsAdminCharacter(string? characterName) => RoleOf(characterName) >= AdminRole.Admin;

    /// <summary>GM or Admin. Used for server-authoritative gameplay bypasses (jail guard, unstuck).</summary>
    public static bool IsGmCharacter(string? characterName) => RoleOf(characterName) >= AdminRole.Gm;

    /// <summary>
    /// Mobius <c>AccessLevel.allowPeaceAttack</c>. An explicit Gm/Admin grant opens it; a development
    /// wildcard deliberately does not.
    /// </summary>
    public static bool AllowPeaceAttack(string? characterName)
    {
        if (!Enabled || string.IsNullOrWhiteSpace(characterName))
            return false;
        EnsurePolicy();
        return _policy!.HasExplicitName(characterName) && RoleOf(characterName) >= AdminRole.Gm;
    }

    /// <summary>Concrete description for boot logs and the status endpoint; never contains a secret.</summary>
    public static string DescribePolicy()
    {
        EnsurePolicy();
        AdminPolicy policy = _policy!;
        return $"environment={policy.Environment} explicit={policy.ExplicitNameCount} wildcard={policy.HasWildcard}"
            + (policy.Error == null ? "" : $" error=\"{policy.Error}\"");
    }

    /// <summary>Test seam: replace the allowlist with explicit admin names (null restores file load).</summary>
    internal static void SetNamesForTest(IEnumerable<string>? names)
    {
        SetPolicyForTest(names);
    }

    /// <summary>Test seam: replace the policy, including explicit helper/gm/admin grants.</summary>
    internal static void SetPolicyForTest(
        IEnumerable<string>? names,
        IReadOnlyDictionary<string, string>? roles = null,
        DeploymentEnvironment environment = DeploymentEnvironment.Development)
    {
        lock (Gate)
        {
            if (names == null && roles == null)
            {
                _policy = null;
                _loadedFrom = null;
                return;
            }

            _policy = AdminPolicy.Parse(names, roles, environment);
            _loadedFrom = "<test>";
        }
    }

    static void EnsurePolicy()
    {
        lock (Gate)
        {
            if (_policy != null)
                return;

            DeploymentEnvironment environment = AdminPolicy.ResolveEnvironment();
            string? dataDir = GameData.FindDataDirectory(AppContext.BaseDirectory)
                ?? GameData.FindDataDirectory(Directory.GetCurrentDirectory());
            if (dataDir == null)
            {
                string error = "data directory not found; admin commands are disabled";
                _policy = AdminPolicy.Parse([], null, environment);
                ZoneLog.Error($"[admin] {CharactersFileName}: {error}");
                return;
            }

            string path = Path.Combine(dataDir, CharactersFileName);
            if (!File.Exists(path))
            {
                _policy = AdminPolicy.Parse([], null, environment);
                ZoneLog.Error($"[admin] allowlist file is missing path={path}; admin commands are disabled");
                return;
            }

            FileDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize<FileDto>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
            }
            catch (Exception ex)
            {
                _policy = AdminPolicy.Parse([], null, environment);
                ZoneLog.Error($"[admin] allowlist is unreadable path={path} error={ex.GetType().Name}: {ex.Message}");
                return;
            }

            _policy = AdminPolicy.Parse(dto?.Names, dto?.Roles, environment);
            _loadedFrom = path;
            if (_policy.Error != null)
                ZoneLog.Error($"[admin] allowlist rejected path={path} error=\"{_policy.Error}\"; denied characters are Player");
        }
    }

    sealed class FileDto
    {
        public List<string>? Names { get; set; }
        public Dictionary<string, string>? Roles { get; set; }
    }
}
