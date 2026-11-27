namespace Fineage.Zone.Security;

/// <summary>
/// Explicit admin hierarchy. Ordering is the authorization order: a command that needs
/// <see cref="Gm"/> is also allowed for <see cref="Admin"/>, never the other way around.
/// </summary>
public enum AdminRole
{
    Player = 0,
    Helper = 1,
    Gm = 2,
    Admin = 3
}

/// <summary>Boot-time deployment class. It decides whether a wildcard admin allowlist is acceptable.</summary>
public enum DeploymentEnvironment
{
    Development,
    Production
}

/// <summary>
/// Parsed <c>data/admin-characters.json</c> policy. The legacy <c>names</c> list is still accepted and
/// keeps its old meaning (an explicit name is an admin). A <c>roles</c> object can grant helper/gm/admin
/// explicitly. Parsing never falls back to a default name: a missing / empty / invalid policy denies
/// every character and carries a concrete error for the caller to log.
/// </summary>
public sealed class AdminPolicy
{
    public const string AnyCharacter = "*";

    readonly Dictionary<string, AdminRole> _roles;
    readonly AdminRole? _wildcardRole;

    AdminPolicy(
        DeploymentEnvironment environment,
        Dictionary<string, AdminRole> roles,
        AdminRole? wildcardRole,
        string? error)
    {
        Environment = environment;
        _roles = roles;
        _wildcardRole = wildcardRole;
        Error = error;
    }

    public DeploymentEnvironment Environment { get; }

    /// <summary>Why the policy is not usable as written. A non-null value means it was applied fail-closed.</summary>
    public string? Error { get; }

    public int ExplicitNameCount => _roles.Count;

    public bool HasWildcard => _wildcardRole != null;

    public AdminRole RoleOf(string? characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
            return AdminRole.Player;
        string name = characterName.Trim();
        if (_roles.TryGetValue(name, out var explicitRole))
            return explicitRole;
        return _wildcardRole ?? AdminRole.Player;
    }

    /// <summary>
    /// True for a name written out in the policy. A development wildcard deliberately does not count:
    /// peace-zone attacks (Mobius <c>allowPeaceAttack</c>) need an explicit grant.
    /// </summary>
    public bool HasExplicitName(string? characterName) =>
        !string.IsNullOrWhiteSpace(characterName) && _roles.ContainsKey(characterName.Trim());

    /// <summary>
    /// Parses the file's two optional collections. <paramref name="names"/> are legacy admin grants;
    /// <paramref name="rolesByName"/> overrides a name's role. Unknown role words and wildcard use in
    /// production are rejected, not silently downgraded.
    /// </summary>
    public static AdminPolicy Parse(
        IEnumerable<string>? names,
        IReadOnlyDictionary<string, string>? rolesByName,
        DeploymentEnvironment environment)
    {
        var roles = new Dictionary<string, AdminRole>(StringComparer.OrdinalIgnoreCase);
        AdminRole? wildcard = null;
        var errors = new List<string>();

        if (names != null)
        {
            foreach (string raw in names)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                string name = raw.Trim();
                if (name == AnyCharacter)
                {
                    if (environment == DeploymentEnvironment.Production)
                        errors.Add($"{AnyCharacter} is not allowed in production; list explicit character names");
                    else
                        wildcard = AdminRole.Admin;
                    continue;
                }

                roles[name] = AdminRole.Admin;
            }
        }

        if (rolesByName != null)
        {
            foreach (var pair in rolesByName)
            {
                string name = pair.Key?.Trim() ?? "";
                if (name.Length == 0)
                    continue;

                if (!TryParseRole(pair.Value, out AdminRole role))
                {
                    // An invalid role must not leave the legacy names entry at Admin.
                    roles.Remove(name);
                    if (name == AnyCharacter)
                        wildcard = null;
                    errors.Add($"unknown role '{pair.Value}' for '{name}'");
                    continue;
                }

                if (name == AnyCharacter)
                {
                    if (environment == DeploymentEnvironment.Production)
                    {
                        wildcard = null;
                        errors.Add($"{AnyCharacter} is not allowed in production; list explicit character names");
                    }
                    else
                    {
                        wildcard = role;
                    }
                    continue;
                }

                roles[name] = role;
            }
        }

        if (roles.Count == 0 && wildcard == null && errors.Count == 0)
            errors.Add("no explicit admin names or roles");

        return new AdminPolicy(environment, roles, wildcard, errors.Count == 0 ? null : string.Join("; ", errors));
    }

    public static bool TryParseRole(string? text, out AdminRole role)
    {
        role = AdminRole.Player;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        switch (text.Trim().ToLowerInvariant())
        {
            case "player":
                role = AdminRole.Player;
                return true;
            case "helper":
                role = AdminRole.Helper;
                return true;
            case "gm":
            case "game-master":
            case "gamemaster":
                role = AdminRole.Gm;
                return true;
            case "admin":
            case "administrator":
                role = AdminRole.Admin;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Reads <c>FINEAGE_ENV</c> / host environment variables. Unknown values fail loudly.</summary>
    public static DeploymentEnvironment ResolveEnvironment()
    {
        string? text = System.Environment.GetEnvironmentVariable("FINEAGE_ENV");
        if (string.IsNullOrWhiteSpace(text))
            text = System.Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(text))
            text = System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(text))
            return DeploymentEnvironment.Development;

        switch (text.Trim().ToLowerInvariant())
        {
            case "prod":
            case "production":
            case "live":
                return DeploymentEnvironment.Production;
            case "dev":
            case "development":
            case "local":
            case "test":
            case "testing":
                return DeploymentEnvironment.Development;
            default:
                throw new InvalidOperationException(
                    $"unknown environment '{text}' in FINEAGE_ENV/DOTNET_ENVIRONMENT/ASPNETCORE_ENVIRONMENT; " +
                    "use production|development");
        }
    }
}
