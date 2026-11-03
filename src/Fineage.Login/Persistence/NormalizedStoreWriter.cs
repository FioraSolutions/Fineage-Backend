using System.Globalization;
using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Login;

/// <summary>Upserts Mobius-shaped character / item / quest rows from a versioned snapshot or live records.</summary>
static class NormalizedStoreWriter
{
    public const string LocInventory = "inventory";
    public const string LocPaperdoll = "paperdoll";
    public const string LocWarehouse = "warehouse";

    public static void UpsertFromSnapshot(NpgsqlConnection conn, NpgsqlTransaction tx, string snapshotJson)
    {
        using var doc = JsonDocument.Parse(snapshotJson);
        var root = doc.RootElement;

        SyncMeta(conn, tx, root);

        // Full reproject of item/quest rows — clears global object_id PKs so memory and SQL stay aligned.
        foreach (string wipe in new[]
                 {
                     "DELETE FROM character_skills",
                     "DELETE FROM character_shortcuts",
                     "DELETE FROM character_quest_vars",
                     "DELETE FROM character_quests",
                     "DELETE FROM character_items"
                 })
        {
            using var wipeCmd = conn.CreateCommand();
            wipeCmd.Transaction = tx;
            wipeCmd.CommandText = wipe;
            wipeCmd.ExecuteNonQuery();
        }

        var accountNames = new HashSet<string>(StringComparer.Ordinal);
        var keepCharacterIds = new HashSet<int>();
        if (root.TryGetProperty("accounts", out var accounts))
        {
            foreach (var account in accounts.EnumerateArray())
            {
                string name = account.GetProperty("name").GetString() ?? "";
                if (string.IsNullOrEmpty(name))
                    continue;
                accountNames.Add(name);
                UpsertAccount(conn, tx, account);
                if (!account.TryGetProperty("characters", out var characters))
                    continue;
                foreach (var ch in characters.EnumerateArray())
                {
                    keepCharacterIds.Add(ch.GetProperty("id").GetInt32());
                    UpsertCharacterFromJson(conn, tx, name, ch);
                }
            }
        }

        DeleteMissingCharacters(conn, tx, keepCharacterIds);
        DeleteMissingAccounts(conn, tx, accountNames);

        if (root.TryGetProperty("clans", out var clans))
        {
            using (var wipe = conn.CreateCommand())
            {
                wipe.Transaction = tx;
                wipe.CommandText = "DELETE FROM clans";
                wipe.ExecuteNonQuery();
            }

            foreach (var clan in clans.EnumerateArray())
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText =
                    """
                    INSERT INTO clans (id, name, payload)
                    VALUES (@id, @name, @payload::jsonb)
                    """;
                ins.Parameters.AddWithValue("id", clan.GetProperty("id").GetInt32());
                ins.Parameters.AddWithValue("name", clan.GetProperty("name").GetString() ?? "");
                ins.Parameters.AddWithValue("payload", clan.GetRawText());
                ins.ExecuteNonQuery();
            }
        }
    }

    public static void UpsertCharacterRecord(NpgsqlConnection conn, NpgsqlTransaction tx, CharacterRecord character)
    {
        UpsertCharacterColumns(
            conn,
            tx,
            character.Id,
            character.Account,
            character.Name,
            character.ClassId,
            character.Race,
            character.Sex,
            character.Level,
            character.HairStyle,
            character.HairColor,
            character.Face,
            character.HasPosition,
            character.X,
            character.Y,
            character.Z,
            character.Exp,
            character.Sp,
            character.Title ?? "",
            character.Newbie,
            character.MaxHp,
            character.Hp,
            character.MaxMp,
            character.Mp,
            character.ClanId,
            character.ClanJoinExpiryUnixMs,
            character.ClanCreateExpiryUnixMs,
            character.CreatedAt,
            character.LastAccess,
            character.DeleteAt);

        ReplaceItems(conn, tx, character.Id, character.Items, character.Warehouse);
        ReplaceQuests(conn, tx, character.Id, character.Quests);
        ReplaceShortcuts(conn, tx, character.Id, character.Shortcuts);
        ReplaceLearnedSkills(conn, tx, character.Id, character.LearnedSkills);
    }

    public static void UpdatePresence(
        NpgsqlConnection conn,
        int characterId,
        int x,
        int y,
        int z,
        DateTimeOffset? lastAccess)
    {
        using var cmd = conn.CreateCommand();
        if (lastAccess is { } access)
        {
            cmd.CommandText =
                """
                UPDATE characters
                SET has_position = true, x = @x, y = @y, z = @z,
                    last_access = CASE
                        WHEN last_access IS NULL OR @access > last_access THEN @access
                        ELSE last_access
                    END,
                    updated_at = now()
                WHERE id = @id
                """;
            cmd.Parameters.AddWithValue("access", access.UtcDateTime);
        }
        else
        {
            cmd.CommandText =
                """
                UPDATE characters
                SET has_position = true, x = @x, y = @y, z = @z, updated_at = now()
                WHERE id = @id
                """;
        }

        cmd.Parameters.AddWithValue("id", characterId);
        cmd.Parameters.AddWithValue("x", x);
        cmd.Parameters.AddWithValue("y", y);
        cmd.Parameters.AddWithValue("z", z);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateVitals(NpgsqlConnection conn, int characterId, int hp, int mp)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE characters SET hp = @hp, mp = @mp, updated_at = now()
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id", characterId);
        cmd.Parameters.AddWithValue("hp", hp);
        cmd.Parameters.AddWithValue("mp", mp);
        cmd.ExecuteNonQuery();
    }

    public static void UpdateProgress(
        NpgsqlConnection conn,
        int characterId,
        int level,
        long exp,
        int sp,
        int maxHp,
        int hp,
        int maxMp,
        int mp)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE characters
            SET level = @level, exp = @exp, sp = @sp,
                max_hp = @maxHp, hp = @hp, max_mp = @maxMp, mp = @mp,
                updated_at = now()
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id", characterId);
        cmd.Parameters.AddWithValue("level", level);
        cmd.Parameters.AddWithValue("exp", exp);
        cmd.Parameters.AddWithValue("sp", sp);
        cmd.Parameters.AddWithValue("maxHp", maxHp);
        cmd.Parameters.AddWithValue("hp", hp);
        cmd.Parameters.AddWithValue("maxMp", maxMp);
        cmd.Parameters.AddWithValue("mp", mp);
        cmd.ExecuteNonQuery();
    }

    public static void ReplaceItems(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<StoredItem>? inventory,
        IReadOnlyList<StoredItem>? warehouse)
    {
        // Caller may have already wiped all items during a full snapshot upsert.
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_items WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (inventory != null)
        {
            foreach (var item in inventory)
                InsertItem(conn, tx, characterId, item, bagLoc: LocInventory);
        }

        if (warehouse != null)
        {
            foreach (var item in warehouse)
                InsertItem(conn, tx, characterId, item, bagLoc: LocWarehouse);
        }
    }

    public static void ReplaceInventoryOnly(
        NpgsqlConnection conn,
        int characterId,
        IReadOnlyList<StoredItem> inventory)
    {
        using var tx = conn.BeginTransaction();
        DeleteItemsByObjectIds(conn, tx, inventory);
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_items WHERE character_id = @id AND loc <> @wh";
            del.Parameters.AddWithValue("id", characterId);
            del.Parameters.AddWithValue("wh", LocWarehouse);
            del.ExecuteNonQuery();
        }

        foreach (var item in inventory)
            InsertItem(conn, tx, characterId, item, bagLoc: LocInventory);
        tx.Commit();
    }

    public static void ReplaceWarehouseOnly(
        NpgsqlConnection conn,
        int characterId,
        IReadOnlyList<StoredItem> warehouse)
    {
        using var tx = conn.BeginTransaction();
        DeleteItemsByObjectIds(conn, tx, warehouse);
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_items WHERE character_id = @id AND loc = @wh";
            del.Parameters.AddWithValue("id", characterId);
            del.Parameters.AddWithValue("wh", LocWarehouse);
            del.ExecuteNonQuery();
        }

        foreach (var item in warehouse)
            InsertItem(conn, tx, characterId, item, bagLoc: LocWarehouse);
        tx.Commit();
    }

    static void DeleteItemsByObjectIds(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        IReadOnlyList<StoredItem> items)
    {
        foreach (var item in items)
        {
            if (item.ObjectId <= 0)
                continue;
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_items WHERE object_id = @oid";
            del.Parameters.AddWithValue("oid", (long)item.ObjectId);
            del.ExecuteNonQuery();
        }
    }

    static void DeleteMissingCharacters(NpgsqlConnection conn, NpgsqlTransaction tx, HashSet<int> keep)
    {
        using var list = conn.CreateCommand();
        list.Transaction = tx;
        list.CommandText = "SELECT id FROM characters";
        var drop = new List<int>();
        using (var reader = list.ExecuteReader())
        {
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                if (!keep.Contains(id))
                    drop.Add(id);
            }
        }

        foreach (int id in drop)
        {
            using var del = conn.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM characters WHERE id = @id";
            del.Parameters.AddWithValue("id", id);
            del.ExecuteNonQuery();
        }
    }

    public static void ReplaceQuests(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<QuestSaveRow>? quests)
    {
        using (var delVars = conn.CreateCommand())
        {
            delVars.Transaction = tx;
            delVars.CommandText = "DELETE FROM character_quest_vars WHERE character_id = @id";
            delVars.Parameters.AddWithValue("id", characterId);
            delVars.ExecuteNonQuery();
        }

        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_quests WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (quests == null)
            return;

        foreach (var quest in quests)
        {
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText =
                    """
                    INSERT INTO character_quests (character_id, quest_id, state)
                    VALUES (@cid, @qid, @state)
                    """;
                ins.Parameters.AddWithValue("cid", characterId);
                ins.Parameters.AddWithValue("qid", quest.QuestId);
                ins.Parameters.AddWithValue("state", quest.State);
                ins.ExecuteNonQuery();
            }

            foreach (var variable in quest.Vars)
            {
                using var insVar = conn.CreateCommand();
                insVar.Transaction = tx;
                insVar.CommandText =
                    """
                    INSERT INTO character_quest_vars (character_id, quest_id, var_key, var_value)
                    VALUES (@cid, @qid, @key, @value)
                    """;
                insVar.Parameters.AddWithValue("cid", characterId);
                insVar.Parameters.AddWithValue("qid", quest.QuestId);
                insVar.Parameters.AddWithValue("key", variable.Key);
                insVar.Parameters.AddWithValue("value", variable.Value);
                insVar.ExecuteNonQuery();
            }
        }
    }

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
        cmd.Parameters.AddWithValue("name", account.GetProperty("name").GetString() ?? "");
        object email = account.TryGetProperty("email", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()!
            : DBNull.Value;
        if (email is string es && string.IsNullOrWhiteSpace(es))
            email = DBNull.Value;
        cmd.Parameters.AddWithValue("email", email);
        cmd.Parameters.AddWithValue("hash", account.GetProperty("passwordHash").GetString() ?? "");
        cmd.Parameters.AddWithValue("salt", account.GetProperty("passwordSalt").GetString() ?? "");
        cmd.Parameters.AddWithValue("iter", account.GetProperty("iterations").GetInt32());
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
            ReadDate(ch, "createdAt") ?? DateTimeOffset.UtcNow,
            ReadDate(ch, "lastAccess"),
            ReadDate(ch, "deleteAt"));

        var inventory = ReadItems(ch, "items");
        var warehouse = ReadItems(ch, "warehouse");
        ReplaceItems(conn, tx, id, inventory, warehouse);
        ReplaceQuests(conn, tx, id, ReadQuests(ch));
        ReplaceShortcuts(conn, tx, id, ReadShortcuts(ch));
        ReplaceLearnedSkills(conn, tx, id, ReadLearnedSkills(ch));
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
        DateTimeOffset createdAt,
        DateTimeOffset? lastAccess,
        DateTimeOffset? deleteAt)
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
                created_at, last_access, delete_at, payload, updated_at)
            VALUES (
                @id, @account, @name, @classId, @race, @sex, @level,
                @hairStyle, @hairColor, @face, @hasPosition, @x, @y, @z, 0,
                @exp, @sp, @title, @newbie, @maxHp, @hp, @maxMp, @mp,
                @clanId, @clanJoin, @clanCreate,
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

    public static void ReplaceLearnedSkills(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<StoredSkill>? skills)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_skills WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (skills == null)
            return;
        foreach (var s in skills)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                "INSERT INTO character_skills (character_id, skill_id, skill_level) VALUES (@cid, @sid, @lvl) " +
                "ON CONFLICT (character_id, skill_id) DO UPDATE SET skill_level = excluded.skill_level";
            ins.Parameters.AddWithValue("cid", characterId);
            ins.Parameters.AddWithValue("sid", s.SkillId);
            ins.Parameters.AddWithValue("lvl", s.Level);
            ins.ExecuteNonQuery();
        }
    }

    static List<StoredSkill>? ReadLearnedSkills(JsonElement character)
    {
        if (!character.TryGetProperty("learnedSkills", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var rows = new List<StoredSkill>();
        foreach (var s in arr.EnumerateArray())
        {
            var row = new StoredSkill(ReadInt(s, "skillId", 0), ReadInt(s, "level", 0));
            if (row.IsValid)
                rows.Add(row);
        }

        return rows;
    }

    public static void ReplaceShortcuts(
        NpgsqlConnection conn,
        NpgsqlTransaction? tx,
        int characterId,
        IReadOnlyList<StoredShortcut>? shortcuts)
    {
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM character_shortcuts WHERE character_id = @id";
            del.Parameters.AddWithValue("id", characterId);
            del.ExecuteNonQuery();
        }

        if (shortcuts == null)
            return;

        foreach (var s in shortcuts)
        {
            using var ins = conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText =
                """
                INSERT INTO character_shortcuts (character_id, slot, page, type, shortcut_id, level)
                VALUES (@cid, @slot, @page, @type, @sid, @level)
                ON CONFLICT (character_id, slot, page) DO UPDATE
                SET type = excluded.type, shortcut_id = excluded.shortcut_id, level = excluded.level
                """;
            ins.Parameters.AddWithValue("cid", characterId);
            ins.Parameters.AddWithValue("slot", s.Slot);
            ins.Parameters.AddWithValue("page", s.Page);
            ins.Parameters.AddWithValue("type", s.Type);
            ins.Parameters.AddWithValue("sid", s.Id);
            ins.Parameters.AddWithValue("level", s.Level);
            ins.ExecuteNonQuery();
        }
    }

    static List<StoredShortcut>? ReadShortcuts(JsonElement character)
    {
        if (!character.TryGetProperty("shortcuts", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var rows = new List<StoredShortcut>();
        foreach (var s in arr.EnumerateArray())
        {
            var row = new StoredShortcut(
                ReadInt(s, "slot", -1),
                ReadInt(s, "page", -1),
                ReadInt(s, "type", 0),
                ReadInt(s, "id", 0),
                ReadInt(s, "level", 0));
            if (row.IsValid)
                rows.Add(row);
        }

        return rows;
    }

    static List<QuestSaveRow>? ReadQuests(JsonElement character)
    {
        if (!character.TryGetProperty("quests", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;
        var rows = new List<QuestSaveRow>();
        foreach (var q in arr.EnumerateArray())
        {
            var vars = new List<QuestSaveVar>();
            if (q.TryGetProperty("vars", out var varsEl) && varsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in varsEl.EnumerateArray())
                {
                    string key = v.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                    string value = v.TryGetProperty("value", out var val) ? val.GetString() ?? "" : "";
                    if (key.Length > 0)
                        vars.Add(new QuestSaveVar(key, value));
                }
            }

            rows.Add(new QuestSaveRow(ReadInt(q, "questId", 0), ReadInt(q, "state", 0), vars));
        }

        return rows;
    }

    static int ReadInt(JsonElement el, string name, int fallback) =>
        el.TryGetProperty(name, out var p) && p.TryGetInt32(out int v) ? v : fallback;

    static long ReadLong(JsonElement el, string name, long fallback) =>
        el.TryGetProperty(name, out var p) && p.TryGetInt64(out long v) ? v : fallback;

    static DateTimeOffset? ReadDate(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String)
            return null;
        string? text = p.GetString();
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture);
    }
}
