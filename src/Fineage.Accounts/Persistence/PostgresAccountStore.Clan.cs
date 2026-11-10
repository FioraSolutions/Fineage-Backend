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
    public IReadOnlyList<ClanRecord> ListClans() => _inner.ListClans();

    public ClanRecord? FindClan(int clanId) => _inner.FindClan(clanId);

    public ClanRecord? FindClanByName(string name) => _inner.FindClanByName(name);

    public ClanRecord? InsertClan(ClanRecord draft, CharacterClanState leader) =>
        _inner.InsertClan(draft, leader);

    public bool UpdateClan(ClanRecord clan, IReadOnlyList<CharacterClanState> characters) =>
        _inner.UpdateClan(clan, characters);

    public bool DeleteClan(int clanId, IReadOnlyList<CharacterClanState> characters) =>
        _inner.DeleteClan(clanId, characters);

    public bool TrySetCharacterClanState(CharacterClanState state) =>
        _inner.TrySetCharacterClanState(state);

    public IReadOnlyList<CastleSave> ListCastles() => _inner.ListCastles();

    public IReadOnlyList<SiegeClanSave> ListSiegeClans() => _inner.ListSiegeClans();

    public bool SaveSiege(
        IReadOnlyList<CastleSave> castles,
        IReadOnlyList<SiegeClanSave> clans,
        IReadOnlyDictionary<int, int> clanCastleIds) =>
        _inner.SaveSiege(castles, clans, clanCastleIds);
}
