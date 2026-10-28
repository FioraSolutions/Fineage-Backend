using Fineage.Game;
using Fineage.Protocol;

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
    readonly GameData _game;
    readonly Func<DateTimeOffset> _clock;
    readonly int _deleteAfterDays;

    public LoginService(
        IAccountStore store,
        HandoffTokens tokens,
        Func<DateTimeOffset>? clock = null,
        GameData? game = null,
        int? deleteAfterDays = null)
    {
        _store = store;
        _tokens = tokens;
        _game = game ?? GameData.LoadDefault();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _deleteAfterDays = deleteAfterDays ?? CharacterRules.DefaultDeleteAfterDays;
        if (_deleteAfterDays < 0)
            throw new ArgumentOutOfRangeException(nameof(deleteAfterDays));
    }

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

    /// <summary>Landing HTTP register. Username and email are stored in canonical form.</summary>
    public LoginResult<string> RegisterLanding(string? username, string? email, string? password)
    {
        if (LandingAuth.ValidateUsername(username, out string user) is { } userError)
            return LoginResult<string>.Fail(userError, "Username was rejected.");
        if (LandingAuth.ValidateEmail(email, out string mail) is { } mailError)
            return LoginResult<string>.Fail(mailError, "Email was rejected.");
        if (LandingAuth.RegisterPasswordError(password, user, mail) is { } passwordError)
            return LoginResult<string>.Fail(passwordError, "Password was rejected.");

        var (hash, salt, iterations) = PasswordHasher.Hash(password!);
        var status = _store.TryAddAccount(user, hash, salt, iterations, mail);
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

        bool found = _store.TryGetCredential(account, out var cred) ||
                     _store.TryGetCredentialByEmail(account, out cred);
        if (!found || !PasswordHasher.Verify(password, cred.PasswordHash, cred.PasswordSalt, cred.Iterations))
            return LoginResult<string>.Fail("invalid_credentials", "Unknown account or password.");
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

        var kit = CharacterInventory.Rows(_game.CreateStarterInventory(classId));
        var now = _clock();
        var status = _store.TryAddCharacter(account, name, classId, race, sex, out var created, kit, createdAt: now, now: now);
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
        return LoginResult<IReadOnlyList<CharacterRecord>>.Success(_store.List(account, _clock()));
    }

    /// <summary>Id when <paramref name="characterId"/> is positive, otherwise name, otherwise slot. Slot −1 means unspecified.</summary>
    public LoginResult<HandoffTicket> Select(string account, int characterId, string? name = null, int slot = -1)
    {
        if (!_store.AccountExists(account))
            return LoginResult<HandoffTicket>.Fail("auth_required", "Login required.");
        if (characterId <= 0 && string.IsNullOrWhiteSpace(name) && slot < 0)
            return LoginResult<HandoffTicket>.Fail("bad_request", "Character id, name, or slot is required.");

        var now = _clock();
        var character = Resolve(account, characterId, name, slot, now);
        if (character == null)
            return LoginResult<HandoffTicket>.Fail(LobbyProtocol.CharacterNotFound, "Character was not found on this account.");
        if (character.IsPendingDelete)
            return LoginResult<HandoffTicket>.Fail(LobbyProtocol.CharacterPendingDelete, "That character is pending deletion.");

        if (!_store.TrySetLastAccess(account, character.Id, now))
            return LoginResult<HandoffTicket>.Fail(LobbyProtocol.CharacterNotFound, "Character was not found on this account.");

        return LoginResult<HandoffTicket>.Success(_tokens.Issue(character, now));
    }

    public LoginResult<CharacterDeleteOutcome> Delete(string account, int slot = -1, int characterId = 0)
    {
        if (!_store.AccountExists(account))
            return LoginResult<CharacterDeleteOutcome>.Fail("auth_required", "Login required.");

        var now = _clock();
        var character = Resolve(account, characterId, name: null, slot, now);
        if (character == null)
            return LoginResult<CharacterDeleteOutcome>.Fail(LobbyProtocol.CharacterNotFound, "Character was not found on this account.");

        if (ClanDeleteBlock(character) is { } clanBlock)
            return LoginResult<CharacterDeleteOutcome>.Fail(LobbyProtocol.DeleteFailed, clanBlock);

        int resolvedSlot = SlotOf(account, character.Id, now);
        bool hard = _deleteAfterDays == 0;
        bool stored = hard
            ? _store.TryHardDelete(account, character.Id)
            : _store.TrySetDeleteAt(account, character.Id, now.AddDays(_deleteAfterDays));
        if (!stored)
            return LoginResult<CharacterDeleteOutcome>.Fail(LobbyProtocol.DeleteFailed, "Character could not be deleted.");

        return LoginResult<CharacterDeleteOutcome>.Success(new CharacterDeleteOutcome(
            resolvedSlot,
            character.Id,
            hard,
            _store.List(account, now)));
    }

    /// <summary>Clears a delete timer. An unknown slot or id fails with <see cref="LobbyProtocol.CharacterNotFound"/>; the login session does not send that on the wire.</summary>
    public LoginResult<IReadOnlyList<CharacterRecord>> Restore(string account, int slot = -1, int characterId = 0)
    {
        if (!_store.AccountExists(account))
            return LoginResult<IReadOnlyList<CharacterRecord>>.Fail("auth_required", "Login required.");

        var now = _clock();
        var character = Resolve(account, characterId, name: null, slot, now);
        if (character == null)
            return LoginResult<IReadOnlyList<CharacterRecord>>.Fail(LobbyProtocol.CharacterNotFound, "Character was not found on this account.");

        if (character.IsPendingDelete && !_store.TryClearDeletion(account, character.Id))
            return LoginResult<IReadOnlyList<CharacterRecord>>.Fail(LobbyProtocol.DeleteFailed, "Character could not be restored.");

        return LoginResult<IReadOnlyList<CharacterRecord>>.Success(_store.List(account, now));
    }

    /// <summary>Character id with the greatest <see cref="CharacterRecord.LastAccess"/>, or 0 when nobody has played.</summary>
    public static int LastPlayedId(IReadOnlyList<CharacterRecord> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        CharacterRecord? best = null;
        foreach (var character in characters)
        {
            if (character.LastAccess == default)
                continue;
            if (best == null || character.LastAccess >= best.LastAccess)
                best = character;
        }

        return best?.Id ?? 0;
    }

    CharacterRecord? Resolve(string account, int characterId, string? name, int slot, DateTimeOffset now)
    {
        var roster = _store.List(account, now);
        if (characterId > 0)
            return roster.FirstOrDefault(c => c.Id == characterId);
        if (!string.IsNullOrWhiteSpace(name))
            return roster.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (slot >= 0 && slot < roster.Count)
            return roster[slot];
        return null;
    }

    int SlotOf(string account, int characterId, DateTimeOffset now)
    {
        var roster = _store.List(account, now);
        for (int i = 0; i < roster.Count; i++)
        {
            if (roster[i].Id == characterId)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Clan membership is not implemented. Mobius refuses a clan member (reason 2) and a clan leader (reason 3).
    /// Return a player-facing sentence when that data exists; null allows the delete.
    /// </summary>
    static string? ClanDeleteBlock(CharacterRecord character)
    {
        if (string.IsNullOrEmpty(character.Account))
            return "Character could not be deleted.";
        return null;
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
