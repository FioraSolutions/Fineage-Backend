using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game;
using Fineage.Game.Quests;

namespace Fineage.Accounts;

/// <summary>
/// In-memory working set behind <see cref="PostgresAccountStore"/> and the unit tests.
/// There is no file-backed mode: PostgreSQL owns durability (<c>characters</c>, <c>character_items</c>,
/// <c>character_quests</c>, <c>character_skills</c>, <c>character_shortcuts</c>).
/// </summary>
public sealed partial class MemoryAccountStore : IAccountStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    readonly object _gate = new();
    StoreFile _data = new();
    Action<string>? _afterSaveSnapshot;

    MemoryAccountStore()
    {
    }

    /// <summary>
    /// Invoked under the store lock after every successful mutation with the versioned snapshot JSON.
    /// Handlers must not re-enter the store.
    /// </summary>
    public Action<string>? AfterSaveSnapshot
    {
        get => _afterSaveSnapshot;
        set => _afterSaveSnapshot = value;
    }

    /// <summary>When false, mutations skip <see cref="AfterSaveSnapshot"/> (Postgres hot paths).</summary>
    public bool EmitSnapshots { get; set; } = true;

    public static MemoryAccountStore InMemory() => new();

    /// <summary>In-memory store seeded from a versioned snapshot JSON (the <c>store_snapshot</c> payload).</summary>
    public static MemoryAccountStore FromSnapshot(string json)
    {
        var store = new MemoryAccountStore();
        store.LoadFromJson(json);
        return store;
    }

    /// <summary>
    /// Replace in-memory state from a durable snapshot (import / recovery). Does not emit <see cref="AfterSaveSnapshot"/>.
    /// </summary>
    public void ReloadFromSnapshot(string json)
    {
        lock (_gate)
            LoadFromJson(json);
    }

    /// <summary>
    /// Merge a normalized PG character into the in-memory mirror so subsequent hot writes hit the same row.
    /// Does not emit <see cref="AfterSaveSnapshot"/> (caller owns durable write).
    /// </summary>
    public void HydrateCharacter(
        CharacterRecord character,
        string? passwordHash = null,
        string? passwordSalt = null,
        int iterations = 0,
        string? email = null)
    {
        ArgumentNullException.ThrowIfNull(character);
        lock (_gate)
        {
            var account = FindAccount(character.Account);
            if (account == null)
            {
                account = new AccountDto
                {
                    Name = character.Account,
                    Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant(),
                    PasswordHash = passwordHash ?? "",
                    PasswordSalt = passwordSalt ?? "",
                    Iterations = iterations
                };
                _data.Accounts.Add(account);
            }
            else if (!string.IsNullOrEmpty(passwordHash))
            {
                account.PasswordHash = passwordHash;
                account.PasswordSalt = passwordSalt ?? account.PasswordSalt;
                account.Iterations = iterations > 0 ? iterations : account.Iterations;
                if (!string.IsNullOrWhiteSpace(email))
                    account.Email = email.Trim().ToLowerInvariant();
            }

            var dto = ToDto(character);
            int index = account.Characters.FindIndex(c => c.Id == character.Id);
            if (index >= 0)
                account.Characters[index] = dto;
            else
                account.Characters.Add(dto);

            if (character.Id >= _data.NextCharacterId)
                _data.NextCharacterId = character.Id + 1;
            foreach (var item in (character.Items ?? []).Concat(character.Warehouse ?? []))
            {
                if (item.ObjectId >= _data.NextItemObjectId)
                    _data.NextItemObjectId = item.ObjectId + 1;
            }
        }
    }

    static CharacterDto ToDto(CharacterRecord character) =>
        new()
        {
            Id = character.Id,
            Name = character.Name,
            ClassId = character.ClassId,
            Race = character.Race,
            Sex = character.Sex,
            Level = character.Level <= 0 ? 1 : character.Level,
            HairStyle = character.HairStyle,
            HairColor = character.HairColor,
            Face = character.Face,
            HasPosition = character.HasPosition,
            X = character.X,
            Y = character.Y,
            Z = character.Z,
            CreatedAt = character.CreatedAt,
            LastAccess = character.LastAccess,
            DeleteAt = character.DeleteAt,
            MaxHp = character.MaxHp,
            Hp = character.Hp,
            MaxMp = character.MaxMp,
            Mp = character.Mp,
            Sp = character.Sp,
            Adena = character.Adena,
            Title = character.Title ?? "",
            ClanId = character.ClanId,
            ClanJoinExpiryUnixMs = character.ClanJoinExpiryUnixMs,
            ClanCreateExpiryUnixMs = character.ClanCreateExpiryUnixMs,
            Exp = character.Exp,
            Newbie = character.Newbie,
            Items = character.Items?.Select(item => new StoredItemDto
            {
                ItemId = item.ItemId,
                Count = item.Count,
                EquippedSlot = item.EquippedSlot,
                EnchantLevel = item.EnchantLevel,
                ObjectId = item.ObjectId
            }).ToList(),
            Warehouse = character.Warehouse?.Select(item => new StoredItemDto
            {
                ItemId = item.ItemId,
                Count = item.Count,
                EquippedSlot = -1,
                EnchantLevel = item.EnchantLevel,
                ObjectId = item.ObjectId
            }).ToList(),
            Quests = character.Quests?.Select(CopyQuest).ToList(),
            Shortcuts = character.Shortcuts?.Select(CopyShortcut).ToList(),
            LearnedSkills = character.LearnedSkills?.Select(s => new StoredSkillDto { SkillId = s.SkillId, Level = s.Level }).ToList(),
            DeathPenaltyLevel = character.DeathPenaltyLevel,
            Karma = character.Karma,
            PkKills = character.PkKills,
            PvpKills = character.PvpKills,
            KnownRecipes = character.KnownRecipes?.ToList(),
            Friends = character.Friends?.Select(f => new StoredSocialDto { ObjectId = f.ObjectId, Name = f.Name }).ToList(),
            Blocked = character.Blocked?.Select(f => new StoredSocialDto { ObjectId = f.ObjectId, Name = f.Name }).ToList(),
            BlockAll = character.BlockAll,
            HennaDye1 = character.HennaDye1,
            HennaDye2 = character.HennaDye2,
            HennaDye3 = character.HennaDye3,
            Effects = character.Effects?.Select(e => new StoredEffectDto
            {
                SkillId = e.SkillId,
                Level = e.Level,
                RemainingSeconds = e.RemainingSeconds
            }).ToList()
        };

    static StoredShortcutDto CopyShortcut(StoredShortcut s) => new()
    {
        Slot = s.Slot,
        Page = s.Page,
        Type = s.Type,
        Id = s.Id,
        Level = s.Level
    };

    public string ExportSnapshot()
    {
        lock (_gate)
            return JsonSerializer.Serialize(_data, JsonOptions);
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
}
