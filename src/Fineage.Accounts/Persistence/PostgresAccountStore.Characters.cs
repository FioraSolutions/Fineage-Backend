using System.Text;
using System.Text.Json;
using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Accounts;

/// <summary>
/// PostgreSQL-backed <see cref="IAccountStore"/>. In-process mutations reuse
/// <see cref="MemoryAccountStore"/>; normalized character/item/quest tables are the
/// durable character-domain source of truth. <c>store_snapshot</c> remains a backup export.
/// </summary>
public sealed partial class PostgresAccountStore
{
    public bool AccountExists(string account) => _inner.AccountExists(account);

    public AddAccountStatus TryAddAccount(string name, string passwordHash, string passwordSalt, int iterations, string? email) =>
        _inner.TryAddAccount(name, passwordHash, passwordSalt, iterations, email);

    public bool TryGetCredentialByEmail(string email, out AccountCredential credential) =>
        _inner.TryGetCredentialByEmail(email, out credential);

    public bool TryGetCredential(string account, out AccountCredential credential) =>
        _inner.TryGetCredential(account, out credential);

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
        // Memory create without AfterSaveSnapshot, then backup JSON + single-character SQL upsert.
        // Full UpsertFromSnapshot would let Login’s stale bag wipe Zone inventory SoT (B9).
        bool previous = _inner.EmitSnapshots;
        _inner.EmitSnapshots = false;
        CreateCharacterStatus status;
        try
        {
            status = _inner.TryAddCharacter(
                account, name, classId, race, sex, out created, items, createdAt, now,
                hairStyle, hairColor, face, hasPosition, x, y, z, level, maxHp, hp, maxMp, mp, sp, adena, title,
                maxCharacters);
        }
        finally
        {
            _inner.EmitSnapshots = previous;
        }

        if (status != CreateCharacterStatus.Ok || created == null)
            return status;

        WriteSnapshotBackup(_inner.ExportSnapshot());
        lock (_persistGate)
        {
            using var conn = _dataSource.OpenConnection();
            using var tx = conn.BeginTransaction();
            NormalizedStoreWriter.UpsertCharacterRecord(conn, tx, created);
            tx.Commit();
        }

        return status;
    }

    public bool TryReplaceItems(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        RunSilent(
            () => _inner.TryReplaceItems(account, characterId, items),
            conn =>
            {
                var record = _inner.FindById(account, characterId)
                    ?? throw new InvalidOperationException($"Character {characterId} missing after item replace.");
                NormalizedStoreWriter.ReplaceInventoryOnly(conn, characterId, record.Items ?? []);
            });

    public bool TryReplaceWarehouse(string account, int characterId, IReadOnlyList<StoredItem> items) =>
        RunSilent(
            () => _inner.TryReplaceWarehouse(account, characterId, items),
            conn =>
            {
                var record = _inner.FindById(account, characterId)
                    ?? throw new InvalidOperationException($"Character {characterId} missing after warehouse replace.");
                NormalizedStoreWriter.ReplaceWarehouseOnly(conn, characterId, record.Warehouse ?? []);
            });

    public bool TrySavePresence(string account, int characterId, int x, int y, int z, DateTimeOffset? lastAccess) =>
        RunSilent(
            () => _inner.TrySavePresence(account, characterId, x, y, z, lastAccess),
            conn => NormalizedStoreWriter.UpdatePresence(conn, characterId, x, y, z, lastAccess));

    public bool TrySaveVitals(string account, int characterId, int hp, int mp) =>
        RunSilent(
            () => _inner.TrySaveVitals(account, characterId, hp, mp),
            conn => NormalizedStoreWriter.UpdateVitals(conn, characterId, hp, mp));

    public bool TrySaveProgress(string account, int characterId, int level, long exp, int sp, int maxHp, int hp, int maxMp, int mp) =>
        RunSilent(
            () => _inner.TrySaveProgress(account, characterId, level, exp, sp, maxHp, hp, maxMp, mp),
            conn => NormalizedStoreWriter.UpdateProgress(conn, characterId, level, exp, sp, maxHp, hp, maxMp, mp));

    public bool TrySaveClass(string account, int characterId, int classId) =>
        RunSilent(
            () => _inner.TrySaveClass(account, characterId, classId),
            conn => NormalizedStoreWriter.UpdateClass(conn, characterId, classId));

    public bool TrySaveDeathPenalty(string account, int characterId, int level) =>
        RunSilent(
            () => _inner.TrySaveDeathPenalty(account, characterId, level),
            conn => NormalizedStoreWriter.UpdateDeathPenalty(conn, characterId, level));

    public bool TrySaveHenna(string account, int characterId, int dye1, int dye2, int dye3) =>
        RunSilent(
            () => _inner.TrySaveHenna(account, characterId, dye1, dye2, dye3),
            conn => NormalizedStoreWriter.UpdateHenna(conn, characterId, dye1, dye2, dye3));

    public bool TrySaveKarmaPk(string account, int characterId, int karma, int pkKills, int pvpKills) =>
        RunSilent(
            () => _inner.TrySaveKarmaPk(account, characterId, karma, pkKills, pvpKills),
            conn => NormalizedStoreWriter.UpdateKarmaPk(conn, characterId, karma, pkKills, pvpKills));

    public bool TryReplaceQuests(string account, int characterId, IReadOnlyList<QuestSaveRow> quests) =>
        RunSilent(
            () => _inner.TryReplaceQuests(account, characterId, quests),
            conn =>
            {
                using var tx = conn.BeginTransaction();
                NormalizedStoreWriter.ReplaceQuests(conn, tx, characterId, quests);
                tx.Commit();
            });

    public bool TryReplaceLearnedSkills(string account, int characterId, IReadOnlyList<StoredSkill> skills) =>
        RunSilent(
            () => _inner.TryReplaceLearnedSkills(account, characterId, skills),
            conn =>
            {
                using var tx = conn.BeginTransaction();
                NormalizedStoreWriter.ReplaceLearnedSkills(conn, tx, characterId, skills);
                tx.Commit();
            });

    public bool TryReplaceEffects(string account, int characterId, IReadOnlyList<StoredEffect> effects) =>
        RunSilent(
            () => _inner.TryReplaceEffects(account, characterId, effects),
            conn =>
            {
                using var tx = conn.BeginTransaction();
                NormalizedStoreWriter.ReplaceEffects(conn, tx, characterId, effects);
                tx.Commit();
            });

    public bool TryReplaceKnownRecipes(string account, int characterId, IReadOnlyList<int> recipeIds) =>
        // Recipe book rows: JSON mirror for now; normalized table follow-up (#281).
        _inner.TryReplaceKnownRecipes(account, characterId, recipeIds);

    public bool TryReplaceFriends(string account, int characterId, IReadOnlyList<StoredSocial> friends) =>
        // Friends: JSON mirror for now; normalized character_friends follow-up (#285).
        _inner.TryReplaceFriends(account, characterId, friends);

    public bool TryReplaceBlocked(string account, int characterId, IReadOnlyList<StoredSocial> blocked, bool blockAll) =>
        _inner.TryReplaceBlocked(account, characterId, blocked, blockAll);

    public CharacterRecord? FindByCharacterId(int characterId) =>
        _inner.FindByCharacterId(characterId);

    public CharacterRecord? FindByCharacterName(string name) =>
        _inner.FindByCharacterName(name);

    public bool TryReplaceShortcuts(string account, int characterId, IReadOnlyList<StoredShortcut> shortcuts) =>
        RunSilent(
            () => _inner.TryReplaceShortcuts(account, characterId, shortcuts),
            conn =>
            {
                using var tx = conn.BeginTransaction();
                NormalizedStoreWriter.ReplaceShortcuts(conn, tx, characterId, shortcuts);
                tx.Commit();
            });

    public bool TrySetLastAccess(string account, int characterId, DateTimeOffset when) =>
        _inner.TrySetLastAccess(account, characterId, when);

    public bool TrySetDeleteAt(string account, int characterId, DateTimeOffset deleteAt) =>
        _inner.TrySetDeleteAt(account, characterId, deleteAt);

    public bool TryClearDeletion(string account, int characterId) =>
        _inner.TryClearDeletion(account, characterId);

    public bool TryHardDelete(string account, int characterId) =>
        _inner.TryHardDelete(account, characterId);

    public CharacterRecord? FindById(string account, int characterId)
    {
        using var conn = _dataSource.OpenConnection();
        var record = NormalizedStoreReader.TryLoadById(conn, account, characterId);
        if (record == null)
            return null;
        Hydrate(conn, record);
        return record;
    }

    public CharacterRecord? FindByName(string account, string name)
    {
        using var conn = _dataSource.OpenConnection();
        var record = NormalizedStoreReader.TryLoadByName(conn, account, name);
        if (record == null)
            return null;
        Hydrate(conn, record);
        return record;
    }

    public IReadOnlyList<CharacterRecord> List(string account, DateTimeOffset now)
    {
        using var conn = _dataSource.OpenConnection();
        var rows = NormalizedStoreReader.ListByAccount(conn, account);
        if (rows.Count == 0)
            return [];

        var cred = NormalizedStoreReader.TryLoadAccount(conn, account);
        foreach (var row in rows)
        {
            _inner.HydrateCharacter(
                row,
                cred?.PasswordHash,
                cred?.PasswordSalt,
                cred?.Iterations ?? 0,
                cred?.Email);
        }

        // Honour pending-delete purge rules from the in-memory store after hydrate.
        return _inner.List(account, now);
    }

    public IReadOnlyList<CharacterRecord> List(string account) =>
        List(account, DateTimeOffset.UtcNow);

    void Hydrate(NpgsqlConnection conn, CharacterRecord record)
    {
        var cred = NormalizedStoreReader.TryLoadAccount(conn, record.Account);
        _inner.HydrateCharacter(
            record,
            cred?.PasswordHash,
            cred?.PasswordSalt,
            cred?.Iterations ?? 0,
            cred?.Email);
    }
}
