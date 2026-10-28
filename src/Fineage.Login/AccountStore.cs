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
    IReadOnlyList<StoredItem>? Items = null,
    int HairStyle = 0,
    int HairColor = 0,
    int Face = 0,
    bool HasPosition = false,
    int X = 0,
    int Y = 0,
    int Z = 0,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset LastAccess = default,
    DateTimeOffset? DeleteAt = null,
    int MaxHp = 0,
    int Hp = 0,
    int MaxMp = 0,
    int Mp = 0,
    int Sp = 0,
    long Adena = 0,
    string Title = "",
    int ClanId = 0,
    long ClanJoinExpiryUnixMs = 0,
    long ClanCreateExpiryUnixMs = 0)
{
    /// <summary>Remaining whole seconds until hard delete. Zero when the character is not pending.</summary>
    public int DeleteSecondsLeft(DateTimeOffset now)
    {
        if (DeleteAt is not { } at)
            return 0;
        long remainingMs = at.ToUnixTimeMilliseconds() - now.ToUnixTimeMilliseconds();
        if (remainingMs <= 0)
            return 0;
        return (int)(remainingMs / 1000);
    }

    public bool IsPendingDelete => DeleteAt != null;
}

/// <summary>One persisted clan member. <see cref="CharacterId"/> is the zone object id.</summary>
public sealed record ClanMemberRecord(
    int CharacterId,
    string Account,
    string Name,
    int ClassId,
    int Level,
    int PledgeType);

/// <summary>Persisted clan. Crest id stays 0 until crests exist. Times are unix milliseconds; 0 means none.</summary>
public sealed record ClanRecord(
    int Id,
    string Name,
    int Level,
    int LeaderId,
    int CrestId,
    long DissolvingExpiryUnixMs,
    long CharPenaltyExpiryUnixMs,
    IReadOnlyList<ClanMemberRecord> Members);

/// <summary>Character clan id and penalty expiries written with a clan mutation.</summary>
public readonly record struct CharacterClanState(
    string Account,
    int CharacterId,
    int ClanId,
    long JoinExpiryUnixMs,
    long CreateExpiryUnixMs);

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
        IReadOnlyList<StoredItem>? items = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? now = null,
        int hairStyle = 0,
        int hairColor = 0,
        int face = 0,
        bool hasPosition = false,
        int x = 0,
        int y = 0,
        int z = 0,
        int level = 1,
        int maxHp = 0,
        int hp = 0,
        int maxMp = 0,
        int mp = 0,
        int sp = 0,
        long adena = 0,
        string title = "",
        int? maxCharacters = null);

    /// <summary>Replaces the saved bag. An empty list is a real save; it does not mean "grant the kit".</summary>
    bool TryReplaceItems(string account, int characterId, IReadOnlyList<StoredItem> items);

    /// <summary>Writes the last world position. When <paramref name="lastAccess"/> is set and later than the stored value, updates last played.</summary>
    bool TrySavePresence(string account, int characterId, int x, int y, int z, DateTimeOffset? lastAccess);

    /// <summary>Writes current HP/MP after zone play. Maxima are unchanged.</summary>
    bool TrySaveVitals(string account, int characterId, int hp, int mp);

    bool TrySetLastAccess(string account, int characterId, DateTimeOffset when);

    bool TrySetDeleteAt(string account, int characterId, DateTimeOffset deleteAt);

    bool TryClearDeletion(string account, int characterId);

    /// <summary>Removes the character and the items stored on it. The name can be used again.</summary>
    bool TryHardDelete(string account, int characterId);

    CharacterRecord? FindById(string account, int characterId);

    CharacterRecord? FindByName(string account, string name);

    /// <summary>Creation order, after hard-deleting characters whose delete time is already in the past.</summary>
    IReadOnlyList<CharacterRecord> List(string account, DateTimeOffset now);

    IReadOnlyList<CharacterRecord> List(string account);

    IReadOnlyList<ClanRecord> ListClans();

    ClanRecord? FindClan(int clanId);

    ClanRecord? FindClanByName(string name);

    /// <summary>Assigns a new id. Sets <see cref="CharacterRecord.ClanId"/> on every member. Leader penalties come from <paramref name="leader"/>.</summary>
    ClanRecord? InsertClan(ClanRecord draft, CharacterClanState leader);

    /// <summary>Replaces the clan row and writes <paramref name="characters"/>.</summary>
    bool UpdateClan(ClanRecord clan, IReadOnlyList<CharacterClanState> characters);

    bool DeleteClan(int clanId, IReadOnlyList<CharacterClanState> characters);

    bool TrySetCharacterClanState(CharacterClanState state);
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
        IReadOnlyList<StoredItem>? items = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? now = null,
        int hairStyle = 0,
        int hairColor = 0,
        int face = 0,
        bool hasPosition = false,
        int x = 0,
        int y = 0,
        int z = 0,
        int level = 1,
        int maxHp = 0,
        int hp = 0,
        int maxMp = 0,
        int mp = 0,
        int sp = 0,
        long adena = 0,
        string title = "",
        int? maxCharacters = null)
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
            if (now is { } purgeAt && PurgeUnlocked(row, purgeAt) > 0)
                Save();
            int cap = maxCharacters ?? HumanClasses.MaxCharactersPerAccount;
            if (cap > 0 && row.Characters.Count >= cap)
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
                Level = level,
                HairStyle = hairStyle,
                HairColor = hairColor,
                Face = face,
                HasPosition = hasPosition,
                X = x,
                Y = y,
                Z = z,
                MaxHp = maxHp,
                Hp = hp,
                MaxMp = maxMp,
                Mp = mp,
                Sp = sp,
                Adena = adena,
                Title = title ?? "",
                Items = CopyItems(items),
                CreatedAt = createdAt ?? DateTimeOffset.UtcNow
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
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Items = CopyItems(items);
            Save();
            return true;
        }
    }

    public bool TrySavePresence(string account, int characterId, int x, int y, int z, DateTimeOffset? lastAccess)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.HasPosition = true;
            character.X = x;
            character.Y = y;
            character.Z = z;
            if (lastAccess is { } access && access > character.LastAccess)
                character.LastAccess = access;
            Save();
            return true;
        }
    }

    public bool TrySaveVitals(string account, int characterId, int hp, int mp)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Hp = hp;
            character.Mp = mp;
            Save();
            return true;
        }
    }

    public bool TrySetLastAccess(string account, int characterId, DateTimeOffset when)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.LastAccess = when;
            Save();
            return true;
        }
    }

    public bool TrySetDeleteAt(string account, int characterId, DateTimeOffset deleteAt)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.DeleteAt = deleteAt;
            Save();
            return true;
        }
    }

    public bool TryClearDeletion(string account, int characterId)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.DeleteAt = null;
            Save();
            return true;
        }
    }

    public IReadOnlyList<ClanRecord> ListClans()
    {
        lock (_gate)
            return _data.Clans.Select(ToClan).ToArray();
    }

    public ClanRecord? FindClan(int clanId)
    {
        lock (_gate)
        {
            var clan = _data.Clans.FirstOrDefault(c => c.Id == clanId);
            return clan == null ? null : ToClan(clan);
        }
    }

    public ClanRecord? FindClanByName(string name)
    {
        lock (_gate)
        {
            var clan = FindClanDto(name);
            return clan == null ? null : ToClan(clan);
        }
    }

    public ClanRecord? InsertClan(ClanRecord draft, CharacterClanState leader)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Members);
        if (draft.Members.Count == 0 || string.IsNullOrWhiteSpace(draft.Name))
            return null;

        lock (_gate)
        {
            if (FindClanDto(draft.Name) != null)
                return null;
            var rows = new List<CharacterDto>(draft.Members.Count);
            foreach (var member in draft.Members)
            {
                var character = FindCharacter(member.Account, member.CharacterId);
                if (character == null)
                    return null;
                rows.Add(character);
            }

            int id = _data.NextClanId++;
            var dto = ToDto(draft, id);
            _data.Clans.Add(dto);
            for (int i = 0; i < draft.Members.Count; i++)
            {
                rows[i].ClanId = id;
                if (draft.Members[i].CharacterId != leader.CharacterId)
                    continue;
                rows[i].ClanJoinExpiryUnixMs = leader.JoinExpiryUnixMs;
                rows[i].ClanCreateExpiryUnixMs = leader.CreateExpiryUnixMs;
            }

            Save();
            return ToClan(dto);
        }
    }

    public bool UpdateClan(ClanRecord clan, IReadOnlyList<CharacterClanState> characters)
    {
        ArgumentNullException.ThrowIfNull(clan);
        ArgumentNullException.ThrowIfNull(characters);
        lock (_gate)
        {
            int index = _data.Clans.FindIndex(c => c.Id == clan.Id);
            if (index < 0)
                return false;
            var rows = new List<CharacterDto>(characters.Count);
            foreach (var state in characters)
            {
                var character = FindCharacter(state.Account, state.CharacterId);
                if (character == null)
                    return false;
                rows.Add(character);
            }

            _data.Clans[index] = ToDto(clan, clan.Id);
            for (int i = 0; i < characters.Count; i++)
                ApplyClanState(rows[i], characters[i]);
            Save();
            return true;
        }
    }

    public bool DeleteClan(int clanId, IReadOnlyList<CharacterClanState> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        lock (_gate)
        {
            int index = _data.Clans.FindIndex(c => c.Id == clanId);
            if (index < 0)
                return false;
            var rows = new List<CharacterDto>(characters.Count);
            foreach (var state in characters)
            {
                var character = FindCharacter(state.Account, state.CharacterId);
                if (character == null)
                    return false;
                rows.Add(character);
            }

            _data.Clans.RemoveAt(index);
            for (int i = 0; i < characters.Count; i++)
                ApplyClanState(rows[i], characters[i]);
            Save();
            return true;
        }
    }

    public bool TrySetCharacterClanState(CharacterClanState state)
    {
        lock (_gate)
        {
            var character = FindCharacter(state.Account, state.CharacterId);
            if (character == null)
                return false;
            ApplyClanState(character, state);
            Save();
            return true;
        }
    }

    public bool TryHardDelete(string account, int characterId)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            var character = row?.Characters.FirstOrDefault(c => c.Id == characterId);
            if (row == null || character == null || character.ClanId != 0)
                return false;
            if (_data.Clans.Any(c => c.Members.Any(m => m.CharacterId == characterId)))
                return false;
            row.Characters.Remove(character);
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

    public IReadOnlyList<CharacterRecord> List(string account) => List(account, DateTimeOffset.UtcNow);

    public IReadOnlyList<CharacterRecord> List(string account, DateTimeOffset now)
    {
        lock (_gate)
        {
            var row = FindAccount(account);
            if (row == null)
                return [];
            if (PurgeUnlocked(row, now) > 0)
                Save();
            return Ordered(row);
        }
    }

    CharacterDto? FindCharacter(string account, int characterId) =>
        FindAccount(account)?.Characters.FirstOrDefault(c => c.Id == characterId);

    static int PurgeUnlocked(AccountDto row, DateTimeOffset now) =>
        row.Characters.RemoveAll(c => c.ClanId == 0 && c.DeleteAt is { } at && at < now);

    static List<CharacterRecord> Ordered(AccountDto row) =>
        row.Characters
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .Select(c => ToRecord(row.Name, c))
            .ToList();

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

    ClanDto? FindClanDto(string name) =>
        _data.Clans.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    static void ApplyClanState(CharacterDto character, CharacterClanState state)
    {
        character.ClanId = state.ClanId;
        character.ClanJoinExpiryUnixMs = state.JoinExpiryUnixMs;
        character.ClanCreateExpiryUnixMs = state.CreateExpiryUnixMs;
    }

    static ClanDto ToDto(ClanRecord clan, int id) => new()
    {
        Id = id,
        Name = clan.Name,
        Level = clan.Level,
        LeaderId = clan.LeaderId,
        CrestId = clan.CrestId,
        DissolvingExpiryUnixMs = clan.DissolvingExpiryUnixMs,
        CharPenaltyExpiryUnixMs = clan.CharPenaltyExpiryUnixMs,
        Members = clan.Members.Select(member => new ClanMemberDto
        {
            CharacterId = member.CharacterId,
            Account = member.Account,
            Name = member.Name,
            ClassId = member.ClassId,
            Level = member.Level,
            PledgeType = member.PledgeType
        }).ToList()
    };

    static ClanRecord ToClan(ClanDto clan) => new(
        clan.Id,
        clan.Name,
        clan.Level,
        clan.LeaderId,
        clan.CrestId,
        clan.DissolvingExpiryUnixMs,
        clan.CharPenaltyExpiryUnixMs,
        clan.Members.Select(member => new ClanMemberRecord(
            member.CharacterId,
            member.Account,
            member.Name,
            member.ClassId,
            member.Level,
            member.PledgeType)).ToArray());

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

        data.Clans ??= [];
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

        var clanIds = new HashSet<int>();
        var clanNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var memberIds = new HashSet<int>();
        foreach (var clan in data.Clans)
        {
            clan.Members ??= [];
            if (clan.Id <= 0 || !clanIds.Add(clan.Id))
                throw new InvalidDataException($"duplicate clan id {clan.Id}");
            if (string.IsNullOrWhiteSpace(clan.Name) || !clanNames.Add(clan.Name))
                throw new InvalidDataException($"duplicate clan name {clan.Name}");
            foreach (var member in clan.Members)
            {
                if (!memberIds.Add(member.CharacterId))
                    throw new InvalidDataException($"character {member.CharacterId} is in two clans");
                if (!ids.Contains(member.CharacterId))
                    throw new InvalidDataException($"clan member {member.CharacterId} has no character");
            }
        }

        int maxId = ids.Count == 0 ? 0 : ids.Max();
        if (data.NextCharacterId <= maxId)
            data.NextCharacterId = maxId + 1;
        int maxClan = clanIds.Count == 0 ? 0 : clanIds.Max();
        if (data.NextClanId <= maxClan)
            data.NextClanId = maxClan + 1;
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
                : character.Items.Select(item => new StoredItem(item.ItemId, item.Count, item.EquippedSlot)).ToArray(),
            character.HairStyle,
            character.HairColor,
            character.Face,
            character.HasPosition,
            character.X,
            character.Y,
            character.Z,
            character.CreatedAt,
            character.LastAccess,
            character.DeleteAt,
            character.MaxHp,
            character.Hp,
            character.MaxMp,
            character.Mp,
            character.Sp,
            character.Adena,
            character.Title,
            character.ClanId,
            character.ClanJoinExpiryUnixMs,
            character.ClanCreateExpiryUnixMs);

    sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public int NextCharacterId { get; set; } = 1;
        public int NextClanId { get; set; } = 1;
        public List<AccountDto> Accounts { get; set; } = [];
        public List<ClanDto> Clans { get; set; } = [];
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
        public int HairStyle { get; set; }
        public int HairColor { get; set; }
        public int Face { get; set; }
        public bool HasPosition { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastAccess { get; set; }
        public DateTimeOffset? DeleteAt { get; set; }
        public int MaxHp { get; set; }
        public int Hp { get; set; }
        public int MaxMp { get; set; }
        public int Mp { get; set; }
        public int Sp { get; set; }
        public long Adena { get; set; }
        public string Title { get; set; } = "";
        public int ClanId { get; set; }
        public long ClanJoinExpiryUnixMs { get; set; }
        public long ClanCreateExpiryUnixMs { get; set; }
        public List<StoredItemDto>? Items { get; set; }
    }

    sealed class ClanDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int LeaderId { get; set; }
        public int CrestId { get; set; }
        public long DissolvingExpiryUnixMs { get; set; }
        public long CharPenaltyExpiryUnixMs { get; set; }
        public List<ClanMemberDto> Members { get; set; } = [];
    }

    sealed class ClanMemberDto
    {
        public int CharacterId { get; set; }
        public string Account { get; set; } = "";
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public int PledgeType { get; set; }
    }

    sealed class StoredItemDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EquippedSlot { get; set; } = -1;
    }
}
