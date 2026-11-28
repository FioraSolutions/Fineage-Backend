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
    readonly object _snapshotGate = new();
    readonly object _snapshotWriteGate = new();
    StoreFile _data = new();
    Action<string>? _afterSaveSnapshot;
    string? _pendingSnapshot;
    long _pendingSnapshotSequence;
    long _lastWrittenSnapshotSequence;

    MemoryAccountStore()
    {
    }

    /// <summary>
    /// Invoked after the store lock has been released, with the versioned snapshot JSON captured under that lock.
    /// <para>
    /// The callback must never run under <see cref="_gate"/>. Durable writers (<see cref="PostgresAccountStore"/>)
    /// take their persistence lock inside this callback, while read paths such as <c>ReserveItemObjectIds</c> take
    /// the persistence lock first and the memory lock second. Running the callback under <see cref="_gate"/> closes
    /// that cycle and deadlocks a concurrent registration and first-character creation.
    /// </para>
    /// Handlers must still not call back into the store (that would recursively flush the pending snapshot).
    /// </summary>
    public Action<string>? AfterSaveSnapshot
    {
        get => _afterSaveSnapshot;
        set => _afterSaveSnapshot = value;
    }

    /// <summary>The memory mutation monitor. Durable adapters must take their persistence gate before it.</summary>
    internal object SyncRoot => _gate;

    [ThreadStatic] static HashSet<MemoryAccountStore>? t_dirtyStores;

    static void MarkSnapshotDirty(MemoryAccountStore store) =>
        (t_dirtyStores ??= new HashSet<MemoryAccountStore>(ReferenceEqualityComparer.Instance)).Add(store);

    static bool TakeSnapshotDirty(MemoryAccountStore store) =>
        t_dirtyStores?.Remove(store) == true;

    /// <summary>
    /// Clears the memory lock and only then runs the pending snapshot callback. Nested store calls (re-entrant
    /// locks) never flush on their own; the outermost guarded call flushes once after releasing the lock.
    /// </summary>
    readonly struct StoreGuard : IDisposable
    {
        readonly MemoryAccountStore _store;
        readonly bool _outermost;

        internal StoreGuard(MemoryAccountStore store, bool outermost)
        {
            _store = store;
            _outermost = outermost;
        }

        public void Dispose()
        {
            Monitor.Exit(_store._gate);
            // Read-only guarded calls must not touch the snapshot writer: a reader that blocks on it while a
            // durable writer waits for the memory lock would recreate the register/create deadlock.
            if (_outermost && TakeSnapshotDirty(_store))
                _store.FlushPendingSnapshot();
        }
    }

    StoreGuard EnterStore()
    {
        bool outermost = !Monitor.IsEntered(_gate);
        Monitor.Enter(_gate);
        return new StoreGuard(this, outermost);
    }

    /// <summary>
    /// When false, mutations on the current thread skip <see cref="AfterSaveSnapshot"/> and do not serialize the
    /// store at all (Postgres hot paths). Per thread: the flag used to be one shared field flipped outside the store
    /// lock, so one thread's silent mutation could silence or un-silence another thread's.
    /// </summary>
    public bool EmitSnapshots
    {
        get => !t_silent;
        set => t_silent = !value;
    }

    [ThreadStatic] static bool t_silent;

    public static MemoryAccountStore InMemory() => new();

    public int NextItemObjectId
    {
        get
        {
            using (EnterStore())
                return _data.NextItemObjectId;
        }
    }

    public int ReserveItemObjectIds(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        using (EnterStore())
        {
            if (_data.NextItemObjectId < PlayerInventory.FirstObjectId)
                _data.NextItemObjectId = PlayerInventory.FirstObjectId;
            int first = _data.NextItemObjectId;
            _data.NextItemObjectId = checked(first + count);
            return first;
        }
    }

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
        using (EnterStore())
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
        using (EnterStore())
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
            foreach (var item in (character.Items ?? []).Concat(character.Warehouse ?? []).Concat(character.Freight ?? []))
            {
                if (item.ObjectId >= _data.NextItemObjectId)
                    _data.NextItemObjectId = item.ObjectId + 1;
            }
        }
    }

    /// <summary>Raises in-memory allocation counters to match durable SQL (<c>store_meta</c> / max ids).</summary>
    public void SyncAllocationCounters(int nextCharacterId, int nextClanId, long nextItemObjectId)
    {
        using (EnterStore())
        {
            if (nextCharacterId > _data.NextCharacterId)
                _data.NextCharacterId = nextCharacterId;
            if (nextClanId > _data.NextClanId)
                _data.NextClanId = nextClanId;
            int itemNext = nextItemObjectId < PlayerInventory.FirstObjectId
                ? PlayerInventory.FirstObjectId
                : nextItemObjectId > int.MaxValue
                    ? int.MaxValue
                    : (int)nextItemObjectId;
            if (itemNext > _data.NextItemObjectId)
                _data.NextItemObjectId = itemNext;
        }
    }

    public (int NextCharacterId, int NextClanId, int NextItemObjectId) AllocationCounters()
    {
        using (EnterStore())
            return (_data.NextCharacterId, _data.NextClanId, _data.NextItemObjectId);
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
            MaxCp = character.MaxCp,
            Cp = character.Cp,
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
                ObjectId = item.ObjectId,
                CustomType1 = item.CustomType1,
                CustomType2 = item.CustomType2,
                Option1 = item.Option1,
                Option2 = item.Option2
            }).ToList(),
            Warehouse = character.Warehouse?.Select(item => new StoredItemDto
            {
                ItemId = item.ItemId,
                Count = item.Count,
                EquippedSlot = -1,
                EnchantLevel = item.EnchantLevel,
                ObjectId = item.ObjectId,
                CustomType1 = item.CustomType1,
                CustomType2 = item.CustomType2,
                Option1 = item.Option1,
                Option2 = item.Option2
            }).ToList(),
            Freight = character.Freight?.Select(item => new StoredItemDto
            {
                ItemId = item.ItemId,
                Count = item.Count,
                EquippedSlot = -1,
                EnchantLevel = item.EnchantLevel,
                ObjectId = item.ObjectId,
                CustomType1 = item.CustomType1,
                CustomType2 = item.CustomType2,
                Option1 = item.Option1,
                Option2 = item.Option2
            }).ToList(),
            Quests = character.Quests?.Select(CopyQuest).ToList(),
            Shortcuts = character.Shortcuts?.Select(CopyShortcut).ToList(),
            Macros = character.Macros?.Select(CopyMacroDto).ToList(),
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
                RemainingSeconds = e.RemainingSeconds,
                ReuseDelayMs = e.ReuseDelayMs,
                SystimeMs = e.SystimeMs,
                RestoreType = e.RestoreType
            }).ToList(),
            ClassState = character.ClassState?.Clone()
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
        using (EnterStore())
            return JsonSerializer.Serialize(_data, JsonOptions);
    }

    public bool AccountExists(string account)
    {
        using (EnterStore())
            return FindAccount(account) != null;
    }

    public AddAccountStatus TryAddAccount(string name, string passwordHash, string passwordSalt, int iterations, string? email)
    {
        name = name.Trim();
        string? canonicalEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        using (EnterStore())
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
        using (EnterStore())
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
        using (EnterStore())
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

    public string GetLanguage(string account)
    {
        using (EnterStore())
            return Fineage.Game.L10n.PlayerLocale.Normalize(FindAccount(account)?.Language);
    }

    public bool TrySetLanguage(string account, string language)
    {
        string normalized = Fineage.Game.L10n.PlayerLocale.Normalize(language);
        using (EnterStore())
        {
            var row = FindAccount(account);
            if (row == null)
                return false;
            if (string.Equals(row.Language, normalized, StringComparison.Ordinal))
                return true;
            row.Language = normalized;
            Save();
            return true;
        }
    }
}
