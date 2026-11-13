using System.Globalization;
using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Accounts;

static partial class NormalizedStoreWriter
{
    static void SyncMeta(NpgsqlConnection conn, NpgsqlTransaction tx, JsonElement root)
    {
        SetMeta(conn, tx, "next_character_id", ReadInt(root, "nextCharacterId", 1));
        SetMeta(conn, tx, "next_clan_id", ReadInt(root, "nextClanId", 1));
        long nextItem = root.TryGetProperty("nextItemObjectId", out var n) && n.TryGetInt64(out long v)
            ? v
            : PlayerInventory.FirstObjectId;
        if (nextItem < PlayerInventory.FirstObjectId)
            nextItem = PlayerInventory.FirstObjectId;
        SetMeta(conn, tx, "next_item_object_id", nextItem);
    }

    static void SetMeta(NpgsqlConnection conn, NpgsqlTransaction tx, string key, long value)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO store_meta (key, value_bigint) VALUES (@key, @value)
            ON CONFLICT (key) DO UPDATE SET value_bigint = EXCLUDED.value_bigint
            """;
        cmd.Parameters.AddWithValue("key", key);
        cmd.Parameters.AddWithValue("value", value);
        cmd.ExecuteNonQuery();
    }

    static void UpsertAccount(NpgsqlConnection conn, NpgsqlTransaction tx, JsonElement account)
    {
        object email = account.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()!
            : DBNull.Value;
        if (email is string es && string.IsNullOrWhiteSpace(es))
            email = DBNull.Value;
        UpsertAccountRow(
            conn,
            tx,
            account.GetProperty("name").GetString() ?? "",
            email,
            account.GetProperty("passwordHash").GetString() ?? "",
            account.GetProperty("passwordSalt").GetString() ?? "",
            account.GetProperty("iterations").GetInt32());
    }

    /// <summary>
    /// One account row. Character insert FKs <c>characters.account</c>, and a full snapshot
    /// reproject would wipe the zone's item rows, so register must write this row by itself.
    /// </summary>
    public static void UpsertAccountRow(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string name,
        object? email,
        string passwordHash,
        string passwordSalt,
        int iterations)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO accounts (name, email, password_hash, password_salt, iterations)
            VALUES (@name, @email, @hash, @salt, @iter)
            ON CONFLICT (name) DO UPDATE SET
                email = EXCLUDED.email,
                password_hash = EXCLUDED.password_hash,
                password_salt = EXCLUDED.password_salt,
                iterations = EXCLUDED.iterations
            """;
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("email", email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("hash", passwordHash);
        cmd.Parameters.AddWithValue("salt", passwordSalt);
        cmd.Parameters.AddWithValue("iter", iterations);
        cmd.ExecuteNonQuery();
    }

    static void DeleteMissingAccounts(NpgsqlConnection conn, NpgsqlTransaction tx, HashSet<string> keep)
    {
        using var list = conn.CreateCommand();
        list.Transaction = tx;
        list.CommandText = "SELECT name FROM accounts";
        var drop = new List<string>();
        using (var reader = list.ExecuteReader())
        {
            while (reader.Read())
            {
                string name = reader.GetString(0);
                if (!keep.Contains(name))
                    drop.Add(name);
            }
        }

        foreach (string name in drop)
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM accounts WHERE name = @name";
            del.Parameters.AddWithValue("name", name);
            del.ExecuteNonQuery();
        }
    }

    static void UpsertCharacterFromJson(NpgsqlConnection conn, NpgsqlTransaction tx, string account, JsonElement ch)
    {
        int id = ch.GetProperty("id").GetInt32();
        UpsertCharacterColumns(
            conn,
            tx,
            id,
            account,
            ch.GetProperty("name").GetString() ?? "",
            ch.GetProperty("classId").GetInt32(),
            ReadInt(ch, "race", 0),
            ReadInt(ch, "sex", 0),
            ReadInt(ch, "level", 1),
            ReadInt(ch, "hairStyle", 0),
            ReadInt(ch, "hairColor", 0),
            ReadInt(ch, "face", 0),
            ch.TryGetProperty("hasPosition", out var hp) && hp.ValueKind == JsonValueKind.True,
            ReadInt(ch, "x", 0),
            ReadInt(ch, "y", 0),
            ReadInt(ch, "z", 0),
            ReadLong(ch, "exp", 0),
            ReadInt(ch, "sp", 0),
            ch.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() ?? "" : "",
            !ch.TryGetProperty("newbie", out var nb) || nb.ValueKind != JsonValueKind.False,
            ReadInt(ch, "maxHp", 0),
            ReadInt(ch, "hp", 0),
            ReadInt(ch, "maxMp", 0),
            ReadInt(ch, "mp", 0),
            ReadInt(ch, "clanId", 0),
            ReadLong(ch, "clanJoinExpiryUnixMs", 0),
            ReadLong(ch, "clanCreateExpiryUnixMs", 0),
            ReadInt(ch, "deathPenaltyLevel", 0),
            ReadInt(ch, "karma", 0),
            ReadInt(ch, "pkKills", 0),
            ReadInt(ch, "pvpKills", 0),
            ReadDate(ch, "createdAt") ?? DateTimeOffset.UtcNow,
            ReadDate(ch, "lastAccess"),
            ReadDate(ch, "deleteAt"),
            ReadInt(ch, "hennaDye1", 0),
            ReadInt(ch, "hennaDye2", 0),
            ReadInt(ch, "hennaDye3", 0));

        var inventory = ReadItems(ch, "items");
        var warehouse = ReadItems(ch, "warehouse");
        ReplaceItems(conn, tx, id, inventory, warehouse);
        ReplaceQuests(conn, tx, id, ReadQuests(ch));
        ReplaceShortcuts(conn, tx, id, ReadShortcuts(ch));
        ReplaceLearnedSkills(conn, tx, id, ReadLearnedSkills(ch));
        ReplaceEffects(conn, tx, id, ReadEffects(ch));
    }

    static List<StoredEffect>? ReadEffects(JsonElement character)
    {
        if (!character.TryGetProperty("effects", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var rows = new List<StoredEffect>();
        foreach (var e in arr.EnumerateArray())
        {
            var row = new StoredEffect(
                ReadInt(e, "skillId", 0),
                ReadInt(e, "level", 0),
                ReadInt(e, "remainingSeconds", 0));
            if (row.IsValid)
                rows.Add(row);
        }

        return rows;
    }

    static void UpsertCharacterColumns(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        int id,
        string account,
        string name,
        int classId,
        int race,
        int sex,
        int level,
        int hairStyle,
        int hairColor,
        int face,
        bool hasPosition,
        int x,
        int y,
        int z,
        long exp,
        int sp,
        string title,
        bool newbie,
        int maxHp,
        int hp,
        int maxMp,
        int mp,
        int clanId,
        long clanJoinExpiry,
        long clanCreateExpiry,
        int deathPenaltyLevel,
        int karma,
        int pkKills,
        int pvpKills,
        DateTimeOffset createdAt,
        DateTimeOffset? lastAccess,
        DateTimeOffset? deleteAt,
        int hennaDye1 = 0,
        int hennaDye2 = 0,
        int hennaDye3 = 0)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO characters (
                id, account, name, class_id, race, sex, level,
                hair_style, hair_color, face, has_position, x, y, z, heading,
                exp, sp, title, newbie, max_hp, hp, max_mp, mp,
                clan_id, clan_join_expiry_unix_ms, clan_create_expiry_unix_ms,
                death_penalty_level, karma, pk_kills, pvp_kills,
                henna_dye1, henna_dye2, henna_dye3,
                created_at, last_access, delete_at, payload, updated_at)
            VALUES (
                @id, @account, @name, @classId, @race, @sex, @level,
                @hairStyle, @hairColor, @face, @hasPosition, @x, @y, @z, 0,
                @exp, @sp, @title, @newbie, @maxHp, @hp, @maxMp, @mp,
                @clanId, @clanJoin, @clanCreate,
                @deathPenalty, @karma, @pk, @pvp,
                @henna1, @henna2, @henna3,
                @createdAt, @lastAccess, @deleteAt, '{}'::jsonb, now())
            ON CONFLICT (id) DO UPDATE SET
                account = EXCLUDED.account,
                name = EXCLUDED.name,
                class_id = EXCLUDED.class_id,
                race = EXCLUDED.race,
                sex = EXCLUDED.sex,
                level = EXCLUDED.level,
                hair_style = EXCLUDED.hair_style,
                hair_color = EXCLUDED.hair_color,
                face = EXCLUDED.face,
                has_position = EXCLUDED.has_position,
                x = EXCLUDED.x,
                y = EXCLUDED.y,
                z = EXCLUDED.z,
                exp = EXCLUDED.exp,
                sp = EXCLUDED.sp,
                title = EXCLUDED.title,
                newbie = EXCLUDED.newbie,
                max_hp = EXCLUDED.max_hp,
                hp = EXCLUDED.hp,
                max_mp = EXCLUDED.max_mp,
                mp = EXCLUDED.mp,
                clan_id = EXCLUDED.clan_id,
                clan_join_expiry_unix_ms = EXCLUDED.clan_join_expiry_unix_ms,
                clan_create_expiry_unix_ms = EXCLUDED.clan_create_expiry_unix_ms,
                death_penalty_level = EXCLUDED.death_penalty_level,
                karma = EXCLUDED.karma,
                pk_kills = EXCLUDED.pk_kills,
                pvp_kills = EXCLUDED.pvp_kills,
                henna_dye1 = EXCLUDED.henna_dye1,
                henna_dye2 = EXCLUDED.henna_dye2,
                henna_dye3 = EXCLUDED.henna_dye3,
                created_at = EXCLUDED.created_at,
                last_access = EXCLUDED.last_access,
                delete_at = EXCLUDED.delete_at,
                updated_at = now()
            """;
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("account", account);
        cmd.Parameters.AddWithValue("name", name);
        cmd.Parameters.AddWithValue("classId", classId);
        cmd.Parameters.AddWithValue("race", race);
        cmd.Parameters.AddWithValue("sex", sex);
        cmd.Parameters.AddWithValue("level", level);
        cmd.Parameters.AddWithValue("hairStyle", hairStyle);
        cmd.Parameters.AddWithValue("hairColor", hairColor);
        cmd.Parameters.AddWithValue("face", face);
        cmd.Parameters.AddWithValue("hasPosition", hasPosition);
        cmd.Parameters.AddWithValue("x", x);
        cmd.Parameters.AddWithValue("y", y);
        cmd.Parameters.AddWithValue("z", z);
        cmd.Parameters.AddWithValue("exp", exp);
        cmd.Parameters.AddWithValue("sp", sp);
        cmd.Parameters.AddWithValue("title", title);
        cmd.Parameters.AddWithValue("newbie", newbie);
        cmd.Parameters.AddWithValue("maxHp", maxHp);
        cmd.Parameters.AddWithValue("hp", hp);
        cmd.Parameters.AddWithValue("maxMp", maxMp);
        cmd.Parameters.AddWithValue("mp", mp);
        cmd.Parameters.AddWithValue("clanId", clanId);
        cmd.Parameters.AddWithValue("clanJoin", clanJoinExpiry);
        cmd.Parameters.AddWithValue("clanCreate", clanCreateExpiry);
        cmd.Parameters.AddWithValue("deathPenalty", deathPenaltyLevel);
        cmd.Parameters.AddWithValue("karma", Math.Max(0, karma));
        cmd.Parameters.AddWithValue("pk", Math.Max(0, pkKills));
        cmd.Parameters.AddWithValue("pvp", Math.Max(0, pvpKills));
        cmd.Parameters.AddWithValue("henna1", Math.Max(0, hennaDye1));
        cmd.Parameters.AddWithValue("henna2", Math.Max(0, hennaDye2));
        cmd.Parameters.AddWithValue("henna3", Math.Max(0, hennaDye3));
        cmd.Parameters.AddWithValue("createdAt", createdAt.UtcDateTime);
        cmd.Parameters.AddWithValue("lastAccess", (object?)lastAccess?.UtcDateTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("deleteAt", (object?)deleteAt?.UtcDateTime ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    static void InsertItem(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        StoredItem item,
        string bagLoc)
    {
        if (item.ObjectId <= 0)
            throw new InvalidOperationException($"Character {characterId} item {item.ItemId} is missing object_id.");

        string loc = item.EquippedSlot >= 0 ? LocPaperdoll : bagLoc;
        int locData = item.EquippedSlot >= 0 ? item.EquippedSlot : 0;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            INSERT INTO character_items (object_id, character_id, item_id, count, enchant_level, loc, loc_data)
            VALUES (@oid, @cid, @itemId, @count, @enchant, @loc, @locData)
            """;
        cmd.Parameters.AddWithValue("oid", (long)item.ObjectId);
        cmd.Parameters.AddWithValue("cid", characterId);
        cmd.Parameters.AddWithValue("itemId", item.ItemId);
        cmd.Parameters.AddWithValue("count", item.Count);
        cmd.Parameters.AddWithValue("enchant", item.EnchantLevel);
        cmd.Parameters.AddWithValue("loc", loc);
        cmd.Parameters.AddWithValue("locData", locData);
        cmd.ExecuteNonQuery();
    }

    static List<StoredItem>? ReadItems(JsonElement character, string property)
    {
        if (!character.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return property == "items" ? null : [];
        var rows = new List<StoredItem>();
        foreach (var item in arr.EnumerateArray())
        {
            rows.Add(new StoredItem(
                ReadInt(item, "itemId", 0),
                ReadInt(item, "count", 1),
                ReadInt(item, "equippedSlot", -1),
                ReadInt(item, "enchantLevel", 0),
                ReadInt(item, "objectId", 0)));
        }

        return rows;
    }
}
