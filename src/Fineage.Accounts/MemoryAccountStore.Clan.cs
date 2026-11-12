using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game;
using Fineage.Game.Quests;

namespace Fineage.Accounts;

public sealed partial class MemoryAccountStore
{
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

    public IReadOnlyList<CastleSave> ListCastles()
    {
        lock (_gate)
        {
            return _data.Castles.Select(castle => new CastleSave(
                castle.Id,
                castle.OwnerClanId,
                castle.TaxPercent,
                castle.SiegeUnixMs,
                castle.RegistrationClosed,
                castle.ScheduleAccepted,
                castle.InProgress,
                castle.EndsUnixMs)).ToArray();
        }
    }

    public IReadOnlyList<SiegeClanSave> ListSiegeClans()
    {
        lock (_gate)
        {
            return _data.SiegeClans.Select(row => new SiegeClanSave(row.CastleId, row.ClanId, row.Side)).ToArray();
        }
    }

    public bool SaveSiege(IReadOnlyList<CastleSave> castles, IReadOnlyList<SiegeClanSave> clans, IReadOnlyDictionary<int, int> clanCastleIds)
    {
        ArgumentNullException.ThrowIfNull(castles);
        ArgumentNullException.ThrowIfNull(clans);
        ArgumentNullException.ThrowIfNull(clanCastleIds);
        lock (_gate)
        {
            var ids = new HashSet<int>();
            foreach (var castle in castles)
            {
                if (castle.Id <= 0 || !ids.Add(castle.Id))
                    return false;
            }

            var pairs = new HashSet<(int CastleId, int ClanId)>();
            foreach (var row in clans)
            {
                if (!ids.Contains(row.CastleId) || row.ClanId <= 0 || !pairs.Add((row.CastleId, row.ClanId)))
                    return false;
            }

            _data.Castles = castles.Select(castle => new CastleDto
            {
                Id = castle.Id,
                OwnerClanId = castle.OwnerClanId,
                TaxPercent = castle.TaxPercent,
                SiegeUnixMs = castle.SiegeUnixMs,
                RegistrationClosed = castle.RegistrationClosed,
                ScheduleAccepted = castle.ScheduleAccepted,
                InProgress = castle.InProgress,
                EndsUnixMs = castle.EndsUnixMs
            }).ToList();
            _data.SiegeClans = clans.Select(row => new SiegeClanDto
            {
                CastleId = row.CastleId,
                ClanId = row.ClanId,
                Side = row.Side
            }).ToList();
            foreach (var clan in _data.Clans)
                clan.CastleId = clanCastleIds.TryGetValue(clan.Id, out int castleId) ? castleId : 0;
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
        CastleId = clan.CastleId,
        Skills = clan.Skills is { Count: > 0 }
            ? clan.Skills.ToDictionary(pair => pair.Key, pair => pair.Value)
            : null,
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
            member.PledgeType)).ToArray(),
        clan.CastleId,
        clan.Skills is { Count: > 0 }
            ? new Dictionary<int, int>(clan.Skills)
            : null);
}
