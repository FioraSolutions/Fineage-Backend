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
                castle.EndsUnixMs,
                castle.Treasury,
                castle.DoorUpgrades is { Count: > 0 } doors ? new Dictionary<int, int>(doors) : null,
                castle.HiredGuards is { Count: > 0 } guards
                    ? guards.Select(g => new HiredSiegeGuardSave(g.ItemId, g.NpcId, g.X, g.Y, g.Z, g.Heading, g.GroundObjectId)).ToArray()
                    : null)).ToArray();
        }
    }

    public IReadOnlyList<SiegeClanSave> ListSiegeClans()
    {
        lock (_gate)
        {
            return _data.SiegeClans.Select(row => new SiegeClanSave(row.CastleId, row.ClanId, row.Side)).ToArray();
        }
    }

    public StoredPet? FindPet(int controlObjectId)
    {
        lock (_gate)
            return _data.Pets.FirstOrDefault(p => p.ControlObjectId == controlObjectId);
    }

    public bool SavePet(StoredPet pet)
    {
        ArgumentNullException.ThrowIfNull(pet);
        if (pet.ControlObjectId <= 0 || pet.NpcId <= 0 || pet.Level <= 0 || pet.Exp < 0)
            return false;
        lock (_gate)
        {
            var copy = pet with { Name = pet.Name ?? "", Items = (pet.Items ?? []).ToArray() };
            int index = _data.Pets.FindIndex(p => p.ControlObjectId == pet.ControlObjectId);
            if (index >= 0)
                _data.Pets[index] = copy;
            else
                _data.Pets.Add(copy);
            foreach (var item in copy.Items)
            {
                if (item.ObjectId >= _data.NextItemObjectId)
                    _data.NextItemObjectId = item.ObjectId + 1;
            }
            Save();
            return true;
        }
    }

    public bool DeletePet(int controlObjectId)
    {
        lock (_gate)
        {
            if (_data.Pets.RemoveAll(p => p.ControlObjectId == controlObjectId) == 0)
                return false;
            Save();
            return true;
        }
    }

    public IReadOnlyList<ManorProductionSave> ListManorProduction()
    {
        lock (_gate)
            return _data.ManorProduction.ToArray();
    }

    public IReadOnlyList<ManorProcureSave> ListManorProcure()
    {
        lock (_gate)
            return _data.ManorProcure.ToArray();
    }

    public bool SaveManor(IReadOnlyList<ManorProductionSave> production, IReadOnlyList<ManorProcureSave> procure)
    {
        ArgumentNullException.ThrowIfNull(production);
        ArgumentNullException.ThrowIfNull(procure);
        lock (_gate)
        {
            if (production.Any(p => p.CastleId <= 0 || p.SeedId <= 0 || p.Amount < 0 || p.Price < 0) ||
                procure.Any(p => p.CastleId <= 0 || p.CropId <= 0 || p.Amount < 0 || p.Price < 0))
                return false;
            _data.ManorProduction = production.ToList();
            _data.ManorProcure = procure.ToList();
            Save();
            return true;
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
                EndsUnixMs = castle.EndsUnixMs,
                Treasury = castle.Treasury,
                DoorUpgrades = castle.DoorUpgrades is { Count: > 0 } doors ? new Dictionary<int, int>(doors) : null,
                HiredGuards = castle.HiredGuards is { Count: > 0 } guards
                    ? guards.Select(g => new HiredSiegeGuardDto
                    {
                        ItemId = g.ItemId,
                        NpcId = g.NpcId,
                        X = g.X,
                        Y = g.Y,
                        Z = g.Z,
                        Heading = g.Heading,
                        GroundObjectId = g.GroundObjectId
                    }).ToList()
                    : null
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
        Details = clan.Details?.Clone(),
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
            : null,
        clan.Details?.Clone());

    public CrestSave? InsertCrest(int kind, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate)
        {
            _data.Crests ??= [];
            if (_data.NextCrestId < 1)
                _data.NextCrestId = 1;
            int id = _data.NextCrestId++;
            _data.Crests.Add(new CrestDto { Id = id, Kind = kind, Data = data.ToArray() });
            Save();
            return new CrestSave(id, kind, data.ToArray());
        }
    }

    public CrestSave? FindCrest(int crestId)
    {
        lock (_gate)
        {
            var dto = _data.Crests?.FirstOrDefault(c => c.Id == crestId);
            return dto == null ? null : new CrestSave(dto.Id, dto.Kind, dto.Data.ToArray());
        }
    }

    public IReadOnlyList<CrestSave> ListCrests()
    {
        lock (_gate)
            return (_data.Crests ?? []).Select(dto => new CrestSave(dto.Id, dto.Kind, dto.Data.ToArray())).ToArray();
    }

    public bool DeleteCrest(int crestId)
    {
        lock (_gate)
        {
            int removed = _data.Crests?.RemoveAll(c => c.Id == crestId) ?? 0;
            if (removed > 0)
                Save();
            return removed > 0;
        }
    }

    public IReadOnlyList<ClanHallSave> ListClanHalls()
    {
        lock (_gate)
            return (_data.ClanHalls ?? []).Select(CloneHall).ToArray();
    }

    public bool SaveClanHalls(IReadOnlyList<ClanHallSave> halls)
    {
        ArgumentNullException.ThrowIfNull(halls);
        lock (_gate)
        {
            _data.ClanHalls = halls.Select(CloneHall).ToList();
            Save();
            return true;
        }
    }

    static ClanHallSave CloneHall(ClanHallSave hall) => new()
    {
        Id = hall.Id,
        OwnerClanId = hall.OwnerClanId,
        PaidUntilUnixMs = hall.PaidUntilUnixMs,
        AuctionEndUnixMs = hall.AuctionEndUnixMs,
        StartingBid = hall.StartingBid,
        Bids = hall.Bids.Select(bid => new ClanHallBidSave
        {
            ClanId = bid.ClanId,
            BidderName = bid.BidderName,
            Amount = bid.Amount,
            TimeUnixMs = bid.TimeUnixMs
        }).ToList()
    };

    public bool TrySetCharacterTitle(string account, int characterId, string title)
    {
        lock (_gate)
        {
            var character = FindCharacter(account, characterId);
            if (character == null)
                return false;
            character.Title = title ?? "";
            Save();
            return true;
        }
    }
}
