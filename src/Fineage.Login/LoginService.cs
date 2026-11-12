using System.Text.RegularExpressions;
using Fineage.Accounts;
using Fineage.Game;
using Fineage.Protocol;

namespace Fineage.Login;

public sealed partial class LoginService
{
    readonly IAccountStore _store;
    readonly HandoffTokens _tokens;
    readonly GameData _game;
    readonly Func<DateTimeOffset> _clock;
    readonly int _deleteAfterDays;
    readonly CharacterCreateOptions _create;
    readonly Action<string> _warn;
    readonly ClassCatalog _classes;
    readonly IReadOnlySet<int> _allowedClasses;
    readonly IReadOnlySet<int> _allowedRaces;
    readonly AuthAttemptLimiter _authAttempts;

    public LoginService(
        IAccountStore store,
        HandoffTokens tokens,
        Func<DateTimeOffset>? clock = null,
        GameData? game = null,
        int? deleteAfterDays = null,
        CharacterCreateOptions? create = null,
        Action<string>? warn = null,
        AuthAttemptLimiter? authAttempts = null)
    {
        _store = store;
        _tokens = tokens;
        _game = game ?? GameData.LoadDefault();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _deleteAfterDays = deleteAfterDays ?? CharacterRules.DefaultDeleteAfterDays;
        if (_deleteAfterDays < 0)
            throw new ArgumentOutOfRangeException(nameof(deleteAfterDays));
        _create = create ?? CharacterCreateOptions.Default;
        _warn = warn ?? (message => Console.WriteLine("[LOGIN] warning: " + message));
        _classes = _game.Classes ?? throw new InvalidOperationException("Class templates were not loaded.");
        _allowedClasses = _create.AllowedClasses ?? _classes.BaseClassIdSet;
        _allowedRaces = _create.AllowedRaces ?? _classes.BaseRaceSet;
        _authAttempts = authAttempts ?? new AuthAttemptLimiter(clock: _clock);
        _classes.EnsureCreatable(_allowedClasses);
        foreach (int race in _allowedRaces)
        {
            if (!_classes.BaseRaceSet.Contains(race))
                throw new InvalidDataException($"Race {race} is not a creatable race.");
        }

        EnsurePlaytestTemplates();
    }

    /// <summary>
    /// The nine Interlude starters must exist even when an allow-list is narrower.
    /// A missing template or starter kit is a datapack error, not a silent Human-only create.
    /// </summary>
    void EnsurePlaytestTemplates()
    {
        foreach (var (classId, race) in CharacterCreateOptions.PlaytestStarters)
        {
            if (!_classes.TryGet(classId, out var template) || template == null || !template.IsBase || template.Race != race)
                throw new InvalidDataException($"Interlude starter class {classId} (race {race}) is missing from the datapack.");
            if (template.Spawns.Count == 0 || template.MaxHp <= 0 || template.MaxMp <= 0)
                throw new InvalidDataException($"Interlude starter class {classId} has no level-1 stats or creation point.");
            if (_game.StarterKits.ForClass(classId).IsEmpty)
                throw new InvalidDataException($"Interlude starter class {classId} has no starter kit.");
        }
    }

    /// <summary>Class ids this service will create. Defaults to every datapack base class.</summary>
    public IReadOnlySet<int> CreatableClassIds => _allowedClasses;

    public bool AccountExists(string account) => _store.AccountExists(account);

    public LoginResult<string> Register(string account, string password)
    {
        if (!NameRules.IsAccountName(account))
            return LoginResult<string>.Fail("invalid_name", "Account name must be 3-16 letters or digits.");
        if (!NameRules.IsPassword(password))
            return LoginResult<string>.Fail("invalid_password", "Password must be 4-64 characters.");

        var (hash, salt, iterations) = PasswordHasher.Hash(password);
        if (_store.TryAddAccount(account.Trim(), hash, salt, iterations, email: null) != AddAccountStatus.Created)
            return LoginResult<string>.Fail("account_exists", "That account is already registered.");
        if (!_store.TryGetCredential(account, out var cred))
            return LoginResult<string>.Fail("store", "Account was not stored.");
        return LoginResult<string>.Success(cred.AccountName);
    }

    /// <summary>
    /// Landing HTTP register. Username is required. A supplied email is stored in canonical form;
    /// a missing or blank email is stored as null.
    /// </summary>
    public LoginResult<string> RegisterLanding(string? username, string? email, string? password)
    {
        if (LandingAuth.ValidateUsername(username, out string user) is { } userError)
            return LoginResult<string>.Fail(userError, "Username was rejected.");
        if (LandingAuth.ValidateEmail(email, out string mail) is { } mailError)
            return LoginResult<string>.Fail(mailError, "Email was rejected.");
        if (LandingAuth.RegisterPasswordError(password, user, mail) is { } passwordError)
            return LoginResult<string>.Fail(passwordError, "Password was rejected.");

        var (hash, salt, iterations) = PasswordHasher.Hash(password!);
        string? storedEmail = mail.Length == 0 ? null : mail;
        var status = _store.TryAddAccount(user, hash, salt, iterations, storedEmail);
        return status switch
        {
            AddAccountStatus.Created => LoginResult<string>.Success(user),
            AddAccountStatus.NameTaken => LoginResult<string>.Fail("username_taken", "That username is already registered."),
            AddAccountStatus.EmailTaken => LoginResult<string>.Fail("email_taken", "That email is already registered."),
            _ => LoginResult<string>.Fail("store", "Account was not stored.")
        };
    }

    public LoginResult<string> Login(string account, string password)
    {
        if (string.IsNullOrWhiteSpace(account))
            return LoginResult<string>.Fail("invalid_credentials", "Unknown account or password.");

        if (_authAttempts.IsLocked(account, out _))
            return LoginResult<string>.Fail("rate_limited", "Too many failed logins. Try again later.");

        bool found = _store.TryGetCredential(account, out var cred) ||
                     _store.TryGetCredentialByEmail(account, out cred);
        if (!found || !PasswordHasher.Verify(password, cred.PasswordHash, cred.PasswordSalt, cred.Iterations))
        {
            _authAttempts.RegisterFailure(account);
            return LoginResult<string>.Fail("invalid_credentials", "Unknown account or password.");
        }

        _authAttempts.RegisterSuccess(cred.AccountName);
        return LoginResult<string>.Success(cred.AccountName);
    }

    /// <summary>Landing HTTP login. The identifier is a username or an email address.</summary>
    public LoginResult<string> LoginLanding(string? usernameOrEmail, string? password)
    {
        if (LandingAuth.LoginIdError(usernameOrEmail, out string id) is { } idError)
            return LoginResult<string>.Fail(idError, "Username or email was rejected.");
        if (LandingAuth.LoginPasswordError(password) is { } passwordError)
            return LoginResult<string>.Fail(passwordError, "Password was rejected.");
        return Login(id, password!);
    }
}
