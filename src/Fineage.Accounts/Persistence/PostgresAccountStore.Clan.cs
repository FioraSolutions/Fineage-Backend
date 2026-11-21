using System.Text.Json;
using Npgsql;

namespace Fineage.Accounts;

/// <summary>
/// PostgreSQL-backed <see cref="IAccountStore"/>. In-process mutations reuse
/// <see cref="MemoryAccountStore"/> (clans, crests and clan halls ride the <c>store_snapshot</c> boot export);
/// each mutation is mirrored into <c>clans</c>, <c>clan_crests</c>, <c>clan_halls</c> and the character
/// columns so the SQL side stays queryable. Titles are character-domain SQL (<c>characters.title</c>).
/// </summary>
public sealed partial class PostgresAccountStore
{
    static readonly JsonSerializerOptions MirrorJson = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<ClanRecord> ListClans() => _inner.ListClans();

    public ClanRecord? FindClan(int clanId) => _inner.FindClan(clanId);

    public ClanRecord? FindClanByName(string name) => _inner.FindClanByName(name);

    public ClanRecord? InsertClan(ClanRecord draft, CharacterClanState leader)
    {
        var saved = _inner.InsertClan(draft, leader);
        if (saved != null)
            Mirror(conn =>
            {
                UpsertClanRow(conn, saved);
                UpdateCharacterClan(conn, leader with { ClanId = saved.Id });
            });
        return saved;
    }

    public bool UpdateClan(ClanRecord clan, IReadOnlyList<CharacterClanState> characters)
    {
        if (!_inner.UpdateClan(clan, characters))
            return false;
        var saved = _inner.FindClan(clan.Id) ?? clan;
        Mirror(conn =>
        {
            UpsertClanRow(conn, saved);
            foreach (var state in characters)
                UpdateCharacterClan(conn, state);
        });
        return true;
    }

    public bool DeleteClan(int clanId, IReadOnlyList<CharacterClanState> characters)
    {
        if (!_inner.DeleteClan(clanId, characters))
            return false;
        Mirror(conn =>
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM clans WHERE id = @id";
                cmd.Parameters.AddWithValue("id", clanId);
                cmd.ExecuteNonQuery();
            }
            foreach (var state in characters)
                UpdateCharacterClan(conn, state);
        });
        return true;
    }

    public bool TrySetCharacterClanState(CharacterClanState state)
    {
        if (!_inner.TrySetCharacterClanState(state))
            return false;
        Mirror(conn => UpdateCharacterClan(conn, state));
        return true;
    }

    public IReadOnlyList<CastleSave> ListCastles() => _inner.ListCastles();

    public IReadOnlyList<SiegeClanSave> ListSiegeClans() => _inner.ListSiegeClans();

    public bool SaveSiege(
        IReadOnlyList<CastleSave> castles,
        IReadOnlyList<SiegeClanSave> clans,
        IReadOnlyDictionary<int, int> clanCastleIds) =>
        _inner.SaveSiege(castles, clans, clanCastleIds);

    public IReadOnlyList<ManorProductionSave> ListManorProduction() => _inner.ListManorProduction();

    public IReadOnlyList<ManorProcureSave> ListManorProcure() => _inner.ListManorProcure();

    public bool SaveManor(IReadOnlyList<ManorProductionSave> production, IReadOnlyList<ManorProcureSave> procure) =>
        _inner.SaveManor(production, procure);

    public CrestSave? InsertCrest(int kind, byte[] data)
    {
        var saved = _inner.InsertCrest(kind, data);
        if (saved != null)
            Mirror(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO clan_crests (id, kind, data, updated_at) VALUES (@id, @kind, @data, now())
                    ON CONFLICT (id) DO UPDATE SET kind = excluded.kind, data = excluded.data, updated_at = now()
                    """;
                cmd.Parameters.AddWithValue("id", saved.Id);
                cmd.Parameters.AddWithValue("kind", (short)saved.Kind);
                cmd.Parameters.AddWithValue("data", saved.Data);
                cmd.ExecuteNonQuery();
            });
        return saved;
    }

    public CrestSave? FindCrest(int crestId) => _inner.FindCrest(crestId);

    public IReadOnlyList<CrestSave> ListCrests() => _inner.ListCrests();

    public bool DeleteCrest(int crestId)
    {
        if (!_inner.DeleteCrest(crestId))
            return false;
        Mirror(conn =>
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM clan_crests WHERE id = @id";
            cmd.Parameters.AddWithValue("id", crestId);
            cmd.ExecuteNonQuery();
        });
        return true;
    }

    public IReadOnlyList<ClanHallSave> ListClanHalls() => _inner.ListClanHalls();

    public bool SaveClanHalls(IReadOnlyList<ClanHallSave> halls)
    {
        if (!_inner.SaveClanHalls(halls))
            return false;
        Mirror(conn =>
        {
            foreach (var hall in halls)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO clan_halls (id, owner_clan_id, paid_until_unix_ms, auction_end_unix_ms, payload, updated_at)
                    VALUES (@id, @owner, @paid, @end, @payload::jsonb, now())
                    ON CONFLICT (id) DO UPDATE SET owner_clan_id = excluded.owner_clan_id,
                        paid_until_unix_ms = excluded.paid_until_unix_ms,
                        auction_end_unix_ms = excluded.auction_end_unix_ms,
                        payload = excluded.payload, updated_at = now()
                    """;
                cmd.Parameters.AddWithValue("id", hall.Id);
                cmd.Parameters.AddWithValue("owner", hall.OwnerClanId);
                cmd.Parameters.AddWithValue("paid", hall.PaidUntilUnixMs);
                cmd.Parameters.AddWithValue("end", hall.AuctionEndUnixMs);
                cmd.Parameters.AddWithValue("payload", JsonSerializer.Serialize(hall, MirrorJson));
                cmd.ExecuteNonQuery();
            }
        });
        return true;
    }

    public IReadOnlyList<SiegableClanHallSave> ListSiegableClanHalls() => _inner.ListSiegableClanHalls();

    public bool SaveSiegableClanHalls(IReadOnlyList<SiegableClanHallSave> halls) => _inner.SaveSiegableClanHalls(halls);

    public bool TrySetCharacterTitle(string account, int characterId, string title) =>
        RunSilent(
            () => _inner.TrySetCharacterTitle(account, characterId, title),
            conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE characters SET title = @title, updated_at = now() WHERE id = @id";
                cmd.Parameters.AddWithValue("id", characterId);
                cmd.Parameters.AddWithValue("title", title ?? "");
                cmd.ExecuteNonQuery();
            });

    static void UpsertClanRow(NpgsqlConnection conn, ClanRecord clan)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO clans (id, name, payload, updated_at) VALUES (@id, @name, @payload::jsonb, now())
            ON CONFLICT (id) DO UPDATE SET name = excluded.name, payload = excluded.payload, updated_at = now()
            """;
        cmd.Parameters.AddWithValue("id", clan.Id);
        cmd.Parameters.AddWithValue("name", clan.Name);
        cmd.Parameters.AddWithValue("payload", JsonSerializer.Serialize(clan, MirrorJson));
        cmd.ExecuteNonQuery();
    }

    static void UpdateCharacterClan(NpgsqlConnection conn, CharacterClanState state)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            """
            UPDATE characters
            SET clan_id = @clan, clan_join_expiry_unix_ms = @join, clan_create_expiry_unix_ms = @create, updated_at = now()
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("id", state.CharacterId);
        cmd.Parameters.AddWithValue("clan", state.ClanId);
        cmd.Parameters.AddWithValue("join", state.JoinExpiryUnixMs);
        cmd.Parameters.AddWithValue("create", state.CreateExpiryUnixMs);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Best-effort SQL mirror: the snapshot already holds the change, so a mirror failure only logs.</summary>
    void Mirror(Action<NpgsqlConnection> sql)
    {
        try
        {
            lock (_persistGate)
            {
                using var conn = _dataSource.OpenConnection();
                sql(conn);
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or InvalidOperationException)
        {
            FineageLogging.Write("accounts", "warning", "clan_mirror_failed", ("error", ex.Message));
        }
    }
}
