using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Accounts;

/// <summary>
/// Authoritative character-domain reads from normalized Postgres tables
/// (Mobius-shaped <c>characters</c> / <c>character_items</c> / quests).
/// </summary>
static partial class NormalizedStoreReader
{
    public readonly record struct AccountCredentialRow(
        string Name,
        string PasswordHash,
        string PasswordSalt,
        int Iterations,
        string? Email);

    public static AccountCredentialRow? TryLoadAccount(NpgsqlConnection conn, string account)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            SELECT name, password_hash, password_salt, iterations, email
            FROM accounts
            WHERE lower(name) = lower(@name)
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("name", account.Trim());
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return new AccountCredentialRow(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    public static CharacterRecord? TryLoadById(NpgsqlConnection conn, string account, int characterId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = CharacterSelectSql + " WHERE c.id = @id AND lower(c.account) = lower(@account) LIMIT 1";
        cmd.Parameters.AddWithValue("id", characterId);
        cmd.Parameters.AddWithValue("account", account.Trim());
        return ReadOneCharacter(conn, cmd);
    }

    public static CharacterRecord? TryLoadByName(NpgsqlConnection conn, string account, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = CharacterSelectSql + " WHERE lower(c.account) = lower(@account) AND lower(c.name) = lower(@name) LIMIT 1";
        cmd.Parameters.AddWithValue("account", account.Trim());
        cmd.Parameters.AddWithValue("name", name.Trim());
        return ReadOneCharacter(conn, cmd);
    }

    public static IReadOnlyList<CharacterRecord> ListByAccount(NpgsqlConnection conn, string account)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = CharacterSelectSql + " WHERE lower(c.account) = lower(@account) ORDER BY c.id";
        cmd.Parameters.AddWithValue("account", account.Trim());
        var rows = new List<CharacterRecord>();
        using var reader = cmd.ExecuteReader();
        var stubs = new List<CharacterStub>();
        while (reader.Read())
            stubs.Add(ReadStub(reader));
        reader.Close();

        foreach (var stub in stubs)
            rows.Add(Materialize(conn, stub));
        return rows;
    }

    const string CharacterSelectSql =
        """
        SELECT c.id, c.account, c.name, c.class_id, c.race, c.sex, c.level,
               c.hair_style, c.hair_color, c.face, c.has_position, c.x, c.y, c.z,
               c.exp, c.sp, c.title, c.newbie, c.max_hp, c.hp, c.max_mp, c.mp,
               c.clan_id, c.clan_join_expiry_unix_ms, c.clan_create_expiry_unix_ms,
               c.death_penalty_level, c.karma, c.pk_kills, c.pvp_kills,
               c.henna_dye1, c.henna_dye2, c.henna_dye3,
               c.created_at, c.last_access, c.delete_at
        FROM characters c
        """;

    sealed record CharacterStub(
        int Id,
        string Account,
        string Name,
        int ClassId,
        int Race,
        int Sex,
        int Level,
        int HairStyle,
        int HairColor,
        int Face,
        bool HasPosition,
        int X,
        int Y,
        int Z,
        long Exp,
        int Sp,
        string Title,
        bool Newbie,
        int MaxHp,
        int Hp,
        int MaxMp,
        int Mp,
        int ClanId,
        long ClanJoinExpiryUnixMs,
        long ClanCreateExpiryUnixMs,
        int DeathPenaltyLevel,
        int Karma,
        int PkKills,
        int PvpKills,
        int HennaDye1,
        int HennaDye2,
        int HennaDye3,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastAccess,
        DateTimeOffset? DeleteAt);

    static CharacterRecord? ReadOneCharacter(NpgsqlConnection conn, NpgsqlCommand cmd)
    {
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        var stub = ReadStub(reader);
        reader.Close();
        return Materialize(conn, stub);
    }

    static CharacterStub ReadStub(NpgsqlDataReader reader) =>
        new(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            reader.GetInt32(9),
            reader.GetBoolean(10),
            reader.GetInt32(11),
            reader.GetInt32(12),
            reader.GetInt32(13),
            reader.GetInt64(14),
            reader.GetInt32(15),
            reader.IsDBNull(16) ? "" : reader.GetString(16),
            reader.GetBoolean(17),
            reader.GetInt32(18),
            reader.GetInt32(19),
            reader.GetInt32(20),
            reader.GetInt32(21),
            reader.GetInt32(22),
            reader.GetInt64(23),
            reader.GetInt64(24),
            reader.GetInt32(25),
            reader.GetInt32(26),
            reader.GetInt32(27),
            reader.GetInt32(28),
            reader.GetInt32(29),
            reader.GetInt32(30),
            reader.GetInt32(31),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(32), DateTimeKind.Utc)),
            reader.IsDBNull(33)
                ? default
                : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(33), DateTimeKind.Utc)),
            reader.IsDBNull(34)
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(34), DateTimeKind.Utc)));

    static CharacterRecord Materialize(NpgsqlConnection conn, CharacterStub stub)
    {
        var items = LoadItems(conn, stub.Id, NormalizedStoreWriter.LocInventory, NormalizedStoreWriter.LocPaperdoll);
        var warehouse = LoadItems(conn, stub.Id, NormalizedStoreWriter.LocWarehouse);
        var quests = LoadQuests(conn, stub.Id);
        var shortcuts = LoadShortcuts(conn, stub.Id);
        var learned = LoadLearnedSkills(conn, stub.Id);
        var effects = LoadEffects(conn, stub.Id);
        return new CharacterRecord(
            stub.Id,
            stub.Name,
            stub.Account,
            stub.ClassId,
            stub.Race,
            stub.Sex,
            stub.Level,
            items,
            stub.HairStyle,
            stub.HairColor,
            stub.Face,
            stub.HasPosition,
            stub.X,
            stub.Y,
            stub.Z,
            stub.CreatedAt,
            stub.LastAccess,
            stub.DeleteAt,
            stub.MaxHp,
            stub.Hp,
            stub.MaxMp,
            stub.Mp,
            stub.Sp,
            Adena: 0,
            stub.Title,
            stub.ClanId,
            stub.ClanJoinExpiryUnixMs,
            stub.ClanCreateExpiryUnixMs,
            stub.Exp,
            quests,
            warehouse,
            stub.Newbie,
            shortcuts,
            learned,
            stub.DeathPenaltyLevel,
            stub.Karma,
            stub.PkKills,
            stub.PvpKills,
            HennaDye1: stub.HennaDye1,
            HennaDye2: stub.HennaDye2,
            HennaDye3: stub.HennaDye3,
            Effects: effects);
    }
}
