using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Login;

public enum CreateCharacterStatus
{
    Ok,
    AccountMissing,
    NameTaken,
    TooMany,
    InvalidName
}

public enum AddAccountStatus
{
    Created,
    NameTaken,
    EmailTaken
}

public sealed record AccountCredential(string AccountName, string PasswordHash, string PasswordSalt, int Iterations);

/// <summary>One saved stack. <see cref="EquippedSlot"/> is a paperdoll index, or -1 in the bag. Null <c>Items</c> on a character means the kit was never granted.</summary>
public sealed record StoredItem(int ItemId, int Count, int EquippedSlot);

public sealed record CharacterRecord(
    int Id,
    string Name,
    string Account,
    int ClassId,
    int Race,
    int Sex,
    int Level,
    IReadOnlyList<StoredItem>? Items = null);

public interface IAccountStore
{
    bool AccountExists(string account);

    AddAccountStatus TryAddAccount(string name, string passwordHash, string passwordSalt, int iterations, string? email);

    bool TryGetCredentialByEmail(string email, out AccountCredential credential);

    bool TryGetCredential(string account, out AccountCredential credential);

    CreateCharacterStatus TryAddCharacter(
        string account,
        string name,
        int classId,
        int race,
        int sex,
        out CharacterRecord? created,
        IReadOnlyList<StoredItem>? items = null);

    /// <summary>Replaces the saved bag. An empty list is a real save; it does not mean "grant the kit".</summary>
    bool TryReplaceItems(string account, int characterId, IReadOnlyList<StoredItem> items);

    CharacterRecord? FindById(string account, int characterId);

    CharacterRecord? FindByName(string account, string name);

    IReadOnlyList<CharacterRecord> List(string account);
}

/// <summary>JSON file store. Pass a null path for process memory only.</summary>
public sealed class JsonAccountStore : IAccountStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    readonly string? _path;
    readonly object _gate = new();
    StoreFile _data = new();

    JsonAccountStore(string? path) => _path = path;

    public static JsonAccountStore InMemory() => new(null);

    public static JsonAccountStore Open(string path)
    {
        var store = new JsonAccountStore(path);
        store.Load();
        return store;
    }

    public bool AccountExists(string account)
    {
        lock (_gate)
            return FindAccount(account) != null;
    }

    public AddAccountStatus TryAddAccount(string name, string passwordHash, string passwordSalt, int iterations, string? email)
    {
        name = name.Trim();
        string? canonicalEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        lock (_gate)
        {
            if (FindAccount(name) != null)
                return AddAccountStatus.NameTaken;
            if (canonicalEmail != null && FindByEmail(canonicalEmail) != null)
                return AddAccountStatus.EmailTaken;
            _data.Accounts.Add(new AccountDto
            {
                Name = name,
                Email = canonicalEmail,
                PasswordHash = passwordHash,
                PasswordSalt = passwordSalt,
                Iterations = iterations
            });
            Save();
            return AddAccountStatus.Created;
        }
    }

    public bool TryGetCredentialByEmail(string email, out AccountCredential credential)
    {
        lock (_gate)
        {
            var row = FindByEmail(email);
            if (row == null)
            {
                credential = new AccountCredential("", "", "", 0);
                return false;
            }

            credential = new AccountCredential(row.Name, row.PasswordHash, row.PasswordSalt, row.Iterations);
            return true;
        }
    }

    public bool TryGetCredential(string account, out AccountCredential credential)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            if (row == null)
            {
                credential = new AccountCredential("", "", "", 0);
                return false;
            }

            credential = new AccountCredential(row.Name, row.PasswordHash, row.PasswordSalt, row.Iterations);
            return true;
        }
    }

    public CreateCharacterStatus TryAddCharacter(
        string account,
        string name,
        int classId,
        int race,
        int sex,
        out CharacterRecord? created,
        IReadOnlyList<StoredItem>? items = null)
    {
        created = null;
        if (!NameRules.IsCharacterName(name))
            return CreateCharacterStatus.InvalidName;
        name = name.Trim();
        if (items != null)
            CharacterInventory.Validate(items);

        lock (_gate)
        {
            var row = FindAccount(account);
            if (row == null)
                return CreateCharacterStatus.AccountMissing;
            if (row.Characters.Count >= HumanClasses.MaxCharactersPerAccount)
                return CreateCharacterStatus.TooMany;
            if (NameTaken(name))
                return CreateCharacterStatus.NameTaken;

            var character = new CharacterDto
            {
                Id = _data.NextCharacterId++,
                Name = name,
                ClassId = classId,
                Race = race,
                Sex = sex,
                Level = 1,
                Items = CopyItems(items)
            };
            row.Characters.Add(character);
            Save();
            created = ToRecord(row.Name, character);
            return CreateCharacterStatus.Ok;
        }
    }

    public bool TryReplaceItems(string account, int characterId, IReadOnlyList<StoredItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        CharacterInventory.Validate(items);
        lock (_gate)
        {
            var row = FindAccount(account);
            var character = row?.Characters.FirstOrDefault(c => c.Id == characterId);
            if (row == null || character == null)
                return false;
            character.Items = CopyItems(items);
            Save();
            return true;
        }
    }

    public CharacterRecord? FindById(string account, int characterId)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            var character = row?.Characters.FirstOrDefault(c => c.Id == characterId);
            return character == null || row == null ? null : ToRecord(row.Name, character);
        }
    }

    public CharacterRecord? FindByName(string account, string name)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            if (row == null)
                return null;
            var character = row.Characters.FirstOrDefault(c =>
                string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            return character == null ? null : ToRecord(row.Name, character);
        }
    }

    public IReadOnlyList<CharacterRecord> List(string account)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            if (row == null)
                return [];
            return row.Characters.Select(c => ToRecord(row.Name, c)).ToArray();
        }
    }

    AccountDto? FindAccount(string account)
    {
        if (string.IsNullOrWhiteSpace(account))
            return null;
        string key = account.Trim();
        return _data.Accounts.FirstOrDefault(a =>
            string.Equals(a.Name, key, StringComparison.OrdinalIgnoreCase));
    }

    AccountDto? FindByEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;
        string key = email.Trim();
        return _data.Accounts.FirstOrDefault(a =>
            !string.IsNullOrEmpty(a.Email) &&
            string.Equals(a.Email, key, StringComparison.OrdinalIgnoreCase));
    }

    bool NameTaken(string name) =>
        _data.Accounts.Any(a => a.Characters.Any(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)));

    void Load()
    {
        if (_path == null || !File.Exists(_path))
        {
            _data = new StoreFile();
            return;
        }

        string json = File.ReadAllText(_path);
        var data = JsonSerializer.Deserialize<StoreFile>(json, JsonOptions)
            ?? throw new InvalidDataException($"empty account store: {_path}");
        if (data.Version != 1)
            throw new InvalidDataException($"unsupported account store version {data.Version}");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<int>();
        foreach (var account in data.Accounts)
        {
            if (!names.Add(account.Name))
                throw new InvalidDataException($"duplicate account {account.Name}");
            foreach (var character in account.Characters)
            {
                if (!ids.Add(character.Id))
                    throw new InvalidDataException($"duplicate character id {character.Id}");
            }
        }

        int maxId = ids.Count == 0 ? 0 : ids.Max();
        if (data.NextCharacterId <= maxId)
            data.NextCharacterId = maxId + 1;
        _data = data;
    }

    void Save()
    {
        if (_path == null)
            return;
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, JsonOptions));
        File.Move(tmp, _path, overwrite: true);
    }

    static List<StoredItemDto>? CopyItems(IReadOnlyList<StoredItem>? items) =>
        items == null ? null : items.Select(item => new StoredItemDto
        {
            ItemId = item.ItemId,
            Count = item.Count,
            EquippedSlot = item.EquippedSlot
        }).ToList();

    static CharacterRecord ToRecord(string account, CharacterDto character) =>
        new(
            character.Id,
            character.Name,
            account,
            character.ClassId,
            character.Race,
            character.Sex,
            character.Level,
            character.Items == null
                ? null
                : character.Items.Select(item => new StoredItem(item.ItemId, item.Count, item.EquippedSlot)).ToArray());

    sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public int NextCharacterId { get; set; } = 1;
        public List<AccountDto> Accounts { get; set; } = [];
    }

    sealed class AccountDto
    {
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public int Iterations { get; set; }
        public List<CharacterDto> Characters { get; set; } = [];
    }

    sealed class CharacterDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int Level { get; set; } = 1;
        public List<StoredItemDto>? Items { get; set; }
    }

    sealed class StoredItemDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EquippedSlot { get; set; } = -1;
    }
}
