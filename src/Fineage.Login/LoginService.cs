namespace Fineage.Login;

public sealed class LoginResult<T>
{
    public bool Ok { get; private init; }
    public T? Value { get; private init; }
    public string Code { get; private init; } = "";
    public string Message { get; private init; } = "";

    public static LoginResult<T> Success(T value) => new() { Ok = true, Value = value };

    public static LoginResult<T> Fail(string code, string message) => new() { Code = code, Message = message };

    public LoginResult<TOut> Forward<TOut>() => LoginResult<TOut>.Fail(Code, Message);
}

public sealed class LoginService
{
    readonly IAccountStore _store;
    readonly HandoffTokens _tokens;
    readonly Func<DateTimeOffset> _clock;

    public LoginService(IAccountStore store, HandoffTokens tokens, Func<DateTimeOffset>? clock = null)
    {
        _store = store;
        _tokens = tokens;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public bool AccountExists(string account) => _store.AccountExists(account);

    public LoginResult<string> Register(string account, string password)
    {
        if (!NameRules.IsAccountName(account))
            return LoginResult<string>.Fail("invalid_name", "Account name must be 3-16 letters or digits.");
        if (!NameRules.IsPassword(password))
            return LoginResult<string>.Fail("invalid_password", "Password must be 4-64 characters.");

        var (hash, salt, iterations) = PasswordHasher.Hash(password);
        if (!_store.TryAddAccount(account.Trim(), hash, salt, iterations))
            return LoginResult<string>.Fail("account_exists", "That account is already registered.");
        if (!_store.TryGetCredential(account, out var cred))
            return LoginResult<string>.Fail("store", "Account was not stored.");
        return LoginResult<string>.Success(cred.AccountName);
    }

    public LoginResult<string> Login(string account, string password)
    {
        if (!_store.TryGetCredential(account, out var cred) ||
            !PasswordHasher.Verify(password, cred.PasswordHash, cred.PasswordSalt, cred.Iterations))
            return LoginResult<string>.Fail("invalid_credentials", "Unknown account or password.");
        return LoginResult<string>.Success(cred.AccountName);
    }

    public LoginResult<CharacterRecord> CreateHuman(string account, string name, int classId, int race = 0, int sex = 0)
    {
        if (!_store.AccountExists(account))
            return LoginResult<CharacterRecord>.Fail("auth_required", "Login required.");
        if (!NameRules.IsCharacterName(name))
            return LoginResult<CharacterRecord>.Fail("invalid_name", "Character name must be 1-16 letters or digits.");
        if (race != HumanClasses.RaceHuman)
            return LoginResult<CharacterRecord>.Fail("race_not_supported", "Only Human is available.");
        if (sex != HumanClasses.SexMale)
            return LoginResult<CharacterRecord>.Fail("sex_not_supported", "Only male characters are available.");
        if (classId is not (HumanClasses.Fighter or HumanClasses.Mystic))
            return LoginResult<CharacterRecord>.Fail("class_not_supported", "Choose Human fighter or mystic.");

        var status = _store.TryAddCharacter(account, name, classId, race, sex, out var created);
        return status switch
        {
            CreateCharacterStatus.Ok when created != null => LoginResult<CharacterRecord>.Success(created),
            CreateCharacterStatus.NameTaken => LoginResult<CharacterRecord>.Fail("name_taken", "That character name is already used."),
            CreateCharacterStatus.TooMany => LoginResult<CharacterRecord>.Fail("character_limit", "An account can hold 7 characters."),
            CreateCharacterStatus.AccountMissing => LoginResult<CharacterRecord>.Fail("auth_required", "Login required."),
            _ => LoginResult<CharacterRecord>.Fail("invalid_name", "Character name must be 1-16 letters or digits.")
        };
    }

    public LoginResult<IReadOnlyList<CharacterRecord>> List(string account)
    {
        if (!_store.AccountExists(account))
            return LoginResult<IReadOnlyList<CharacterRecord>>.Fail("auth_required", "Login required.");
        return LoginResult<IReadOnlyList<CharacterRecord>>.Success(_store.List(account));
    }

    public LoginResult<HandoffTicket> Select(string account, int characterId, string? name = null)
    {
        if (!_store.AccountExists(account))
            return LoginResult<HandoffTicket>.Fail("auth_required", "Login required.");

        CharacterRecord? character = null;
        if (characterId > 0)
            character = _store.FindById(account, characterId);
        else if (!string.IsNullOrWhiteSpace(name))
            character = _store.FindByName(account, name);
        else
            return LoginResult<HandoffTicket>.Fail("bad_request", "Character id or name is required.");

        if (character == null)
            return LoginResult<HandoffTicket>.Fail("not_found", "Character was not found on this account.");

        return LoginResult<HandoffTicket>.Success(_tokens.Issue(character, _clock()));
    }

    /// <summary>Register when the account is new, create the Human when missing, then select.</summary>
    public LoginResult<HandoffTicket> Play(string account, string password, string name, int classId)
    {
        LoginResult<string> session = _store.AccountExists(account)
            ? Login(account, password)
            : Register(account, password);
        if (!session.Ok || session.Value == null)
            return session.Forward<HandoffTicket>();

        string canonical = session.Value;
        var existing = _store.FindByName(canonical, name);
        if (existing == null)
        {
            var created = CreateHuman(canonical, name, classId);
            if (!created.Ok || created.Value == null)
                return created.Forward<HandoffTicket>();
            existing = created.Value;
        }
        else if (existing.ClassId != classId)
        {
            return LoginResult<HandoffTicket>.Fail(
                "class_mismatch",
                "That character already exists with a different class.");
        }

        return Select(canonical, existing.Id);
    }
}
