using System.Text.RegularExpressions;
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
    readonly CharacterCreateOptions _create;
    readonly Action<string> _warn;
    readonly ClassCatalog _classes;
    readonly IReadOnlySet<int> _allowedClasses;
    readonly IReadOnlySet<int> _allowedRaces;

    public LoginService(
        IAccountStore store,
        HandoffTokens tokens,
        Func<DateTimeOffset>? clock = null,
        GameData? game = null,
        int? deleteAfterDays = null,
        CharacterCreateOptions? create = null,
        Action<string>? warn = null)
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
        _classes.EnsureCreatable(_allowedClasses);
        foreach (int race in _allowedRaces)
        {
            if (!_classes.BaseRaceSet.Contains(race))
                throw new InvalidDataException($"Race {race} is not a creatable race.");
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

    public LoginResult<CharacterRecord> CreateHuman(
        string account,
        string name,
        int classId,
        int race = 0,
        int sex = 0,
        int hairStyle = 0,
        int hairColor = 0,
        int face = 0)
    {
        if (!_store.AccountExists(account))
            return LoginResult<CharacterRecord>.Fail("auth_required", "Login required.");
        if (!_create.CreateAllowed)
            return LoginResult<CharacterRecord>.Fail(LobbyProtocol.CreateNotAllowed, "Character creation is turned off.");

        if (ClassifyName(name) is { } nameCode)
            return LoginResult<CharacterRecord>.Fail(nameCode, NameMessage(nameCode));
        if (sex is not (HumanClasses.SexMale or HumanClasses.SexFemale))
            return LoginResult<CharacterRecord>.Fail(LobbyProtocol.SexNotSupported, "Sex must be male or female.");
        if (!AppearanceOk(sex, hairStyle, hairColor, face))
        {
            _warn($"create appearance rejected account={account} sex={sex} hairStyle={hairStyle} hairColor={hairColor} face={face}");
            return LoginResult<CharacterRecord>.Fail(LobbyProtocol.CreationFailed, "Character appearance was rejected.");
        }

        if (!_classes.TryGet(classId, out var template) || template == null)
            return LoginResult<CharacterRecord>.Fail(LobbyProtocol.ClassNotSupported, "That class cannot be created.");
        if (!template.IsBase)
            return LoginResult<CharacterRecord>.Fail(LobbyProtocol.CreationFailed, "Only a base class can be created.");
        if (!_allowedClasses.Contains(classId))
            return LoginResult<CharacterRecord>.Fail(LobbyProtocol.ClassNotSupported, "That class cannot be created.");
        if (race != template.Race)
            return LoginResult<CharacterRecord>.Fail(LobbyProtocol.RaceNotSupported, "Race does not match the class.");
        if (!_allowedRaces.Contains(template.Race))
            return LoginResult<CharacterRecord>.Fail(LobbyProtocol.CreationFailed, "That race cannot be created.");

        var inventory = _game.CreateStarterInventory(classId);
        long adena = Math.Max(0, _create.StartingAdena);
        if (adena > 0)
            inventory.Add(57, adena > int.MaxValue ? int.MaxValue : (int)adena);
        var kit = CharacterInventory.Rows(inventory);
        var now = _clock();
        int level = Math.Max(1, _create.StartingLevel);
        int sp = Math.Max(0, _create.StartingSp);
        var status = _store.TryAddCharacter(
            account,
            name,
            template.ClassId,
            template.Race,
            sex,
            out var created,
            kit,
            createdAt: now,
            now: now,
            hairStyle: hairStyle,
            hairColor: hairColor,
            face: face,
            hasPosition: true,
            x: template.Spawns[0].X,
            y: template.Spawns[0].Y,
            z: template.Spawns[0].Z,
            level: level,
            maxHp: template.ModifiedHp(level),
            hp: template.ModifiedHp(level),
            maxMp: template.ModifiedMp(level),
            mp: template.ModifiedMp(level),
            sp: sp,
            adena: adena,
            title: "",
            maxCharacters: _create.CharMaxNumber);
        return status switch
        {
            CreateCharacterStatus.Ok when created != null => LoginResult<CharacterRecord>.Success(created),
            CreateCharacterStatus.NameTaken => LoginResult<CharacterRecord>.Fail(LobbyProtocol.NameTaken, "That character name is already used."),
            CreateCharacterStatus.TooMany => LoginResult<CharacterRecord>.Fail(
                LobbyProtocol.CharacterLimit,
                _create.CharMaxNumber > 0
                    ? $"An account can hold {_create.CharMaxNumber} characters."
                    : "Character limit reached."),
            CreateCharacterStatus.AccountMissing => LoginResult<CharacterRecord>.Fail("auth_required", "Login required."),
            _ => LoginResult<CharacterRecord>.Fail(LobbyProtocol.InvalidName, "Character name must be letters or digits.")
        };
    }

    string? ClassifyName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return LobbyProtocol.NameTooLong;
        name = name.Trim();
        if (name.Length > 16)
            return LobbyProtocol.NameTooLong;
        if (!IsAsciiAlnum(name))
            return LobbyProtocol.InvalidName;
        foreach (string forbidden in _create.ForbiddenNames)
        {
            if (forbidden.Length > 0 && name.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                return LobbyProtocol.InvalidName;
        }

        try
        {
            if (!_create.NameTemplate.IsMatch(name))
                return LobbyProtocol.InvalidName;
        }
        catch (RegexMatchTimeoutException)
        {
            return LobbyProtocol.InvalidName;
        }

        return null;
    }

    static bool IsAsciiAlnum(string name)
    {
        foreach (char ch in name)
        {
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
                continue;
            return false;
        }

        return name.Length > 0;
    }

    static bool AppearanceOk(int sex, int hairStyle, int hairColor, int face)
    {
        if (face is < 0 or > 2 || hairColor is < 0 or > 3)
            return false;
        int maxStyle = sex == HumanClasses.SexFemale ? 6 : 4;
        return hairStyle >= 0 && hairStyle <= maxStyle;
    }

    static string NameMessage(string code) =>
        code == LobbyProtocol.NameTooLong
            ? "Character name must be 1-16 characters."
            : "Character name must be letters or digits.";

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
    /// Mobius refuses a clan member (reason 2) and a clan leader (reason 3).
    /// Null allows the delete.
    /// </summary>
    string? ClanDeleteBlock(CharacterRecord character)
    {
        if (character.ClanId == 0)
            return null;
        var clan = _store.FindClan(character.ClanId);
        if (clan != null && clan.LeaderId == character.Id)
            return "A clan leader cannot be deleted.";
        return "A clan member cannot be deleted.";
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
