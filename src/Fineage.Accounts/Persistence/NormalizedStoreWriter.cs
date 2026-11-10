using System.Globalization;
using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Quests;
using Npgsql;

namespace Fineage.Accounts;

/// <summary>Upserts Mobius-shaped character / item / quest rows from a versioned snapshot or live records.</summary>
static partial class NormalizedStoreWriter
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
                     "DELETE FROM character_skills_save",
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
            character.DeathPenaltyLevel,
            character.Karma,
            character.PkKills,
            character.PvpKills,
            character.CreatedAt,
            character.LastAccess,
            character.DeleteAt,
            character.HennaDye1,
            character.HennaDye2,
            character.HennaDye3);

        ReplaceItems(conn, tx, character.Id, character.Items, character.Warehouse);
        ReplaceQuests(conn, tx, character.Id, character.Quests);
        ReplaceShortcuts(conn, tx, character.Id, character.Shortcuts);
        ReplaceLearnedSkills(conn, tx, character.Id, character.LearnedSkills);
        ReplaceEffects(conn, tx, character.Id, character.Effects);
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

    public static void UpdateClass(NpgsqlConnection conn, int characterId, int classId)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET class_id = @classId, updated_at = now() WHERE id = @id";
        cmd.Parameters.AddWithValue("id", characterId);
        cmd.Parameters.AddWithValue("classId", classId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Mobius <c>characters.death_penalty_level</c> (<c>Player.java:337</c>).</summary>
    public static void UpdateDeathPenalty(NpgsqlConnection conn, int characterId, int level)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE characters SET death_penalty_level = @level, updated_at = now() WHERE id = @id";
        cmd.Parameters.AddWithValue("id", characterId);
        cmd.Parameters.AddWithValue("level", level);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Mobius henna dye slots (three tattoos).</summary>
    public static void UpdateHenna(NpgsqlConnection conn, int characterId, int dye1, int dye2, int dye3)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE characters
            SET henna_dye1 = @d1, henna_dye2 = @d2, henna_dye3 = @d3, updated_at = now()
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id", characterId);
        cmd.Parameters.AddWithValue("d1", Math.Max(0, dye1));
        cmd.Parameters.AddWithValue("d2", Math.Max(0, dye2));
        cmd.Parameters.AddWithValue("d3", Math.Max(0, dye3));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Mobius <c>characters.karma|pkkills|pvpkills</c>.</summary>
    public static void UpdateKarmaPk(NpgsqlConnection conn, int characterId, int karma, int pkKills, int pvpKills)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE characters
            SET karma = @karma, pk_kills = @pk, pvp_kills = @pvp, updated_at = now()
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id", characterId);
        cmd.Parameters.AddWithValue("karma", Math.Max(0, karma));
        cmd.Parameters.AddWithValue("pk", Math.Max(0, pkKills));
        cmd.Parameters.AddWithValue("pvp", Math.Max(0, pvpKills));
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
}
