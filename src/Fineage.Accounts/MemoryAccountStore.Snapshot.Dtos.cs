using System.Text.Json.Serialization;
using Fineage.Game;

namespace Fineage.Accounts;

public sealed partial class MemoryAccountStore
{
    sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public int NextCharacterId { get; set; } = 1;
        public int NextClanId { get; set; } = 1;
        public int NextItemObjectId { get; set; } = PlayerInventory.FirstObjectId;
        public List<AccountDto> Accounts { get; set; } = [];
        public List<ClanDto> Clans { get; set; } = [];
        public List<CastleDto> Castles { get; set; } = [];
        public List<SiegeClanDto> SiegeClans { get; set; } = [];
    }

    sealed class AccountDto
    {
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public int Iterations { get; set; }
        public List<CharacterDto> Characters { get; set; } = [];
    }

    sealed class CharacterDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int Level { get; set; } = 1;
        public int HairStyle { get; set; }
        public int HairColor { get; set; }
        public int Face { get; set; }
        public bool HasPosition { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastAccess { get; set; }
        public DateTimeOffset? DeleteAt { get; set; }
        public int MaxHp { get; set; }
        public int Hp { get; set; }
        public int MaxMp { get; set; }
        public int Mp { get; set; }
        public int Sp { get; set; }
        public long Adena { get; set; }
        public string Title { get; set; } = "";
        public int ClanId { get; set; }
        public long ClanJoinExpiryUnixMs { get; set; }
        public long ClanCreateExpiryUnixMs { get; set; }
        public long Exp { get; set; }
        public List<StoredQuestDto>? Quests { get; set; }
        public List<StoredItemDto>? Items { get; set; }
        public List<StoredItemDto>? Warehouse { get; set; }
        public bool Newbie { get; set; } = true;
        public List<StoredShortcutDto>? Shortcuts { get; set; }
        public List<StoredSkillDto>? LearnedSkills { get; set; }
        public int DeathPenaltyLevel { get; set; }
        public int Karma { get; set; }
        public int PkKills { get; set; }
        public int PvpKills { get; set; }
        public List<int>? KnownRecipes { get; set; }
        public List<StoredSocialDto>? Friends { get; set; }
        public List<StoredSocialDto>? Blocked { get; set; }
        public bool BlockAll { get; set; }
        public int HennaDye1 { get; set; }
        public int HennaDye2 { get; set; }
        public int HennaDye3 { get; set; }
        public List<StoredEffectDto>? Effects { get; set; }
    }

    sealed class StoredSocialDto
    {
        public int ObjectId { get; set; }
        public string Name { get; set; } = "";
    }

    sealed class StoredSkillDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
    }

    sealed class StoredEffectDto
    {
        public int SkillId { get; set; }
        public int Level { get; set; }
        public int RemainingSeconds { get; set; }
    }

    sealed class StoredShortcutDto
    {
        public int Slot { get; set; }
        public int Page { get; set; }
        public int Type { get; set; }
        public int Id { get; set; }
        public int Level { get; set; }
    }

    sealed class StoredQuestDto
    {
        public int QuestId { get; set; }
        public int State { get; set; }
        public List<StoredQuestVarDto>? Vars { get; set; }
    }

    sealed class StoredQuestVarDto
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }

    sealed class ClanDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; set; }
        public int LeaderId { get; set; }
        public int CrestId { get; set; }
        public long DissolvingExpiryUnixMs { get; set; }
        public long CharPenaltyExpiryUnixMs { get; set; }
        public int CastleId { get; set; }
        public Dictionary<int, int>? Skills { get; set; }
        public List<ClanMemberDto> Members { get; set; } = [];
    }

    sealed class CastleDto
    {
        public int Id { get; set; }
        public int OwnerClanId { get; set; }
        public int TaxPercent { get; set; }
        public long SiegeUnixMs { get; set; }
        public bool RegistrationClosed { get; set; }
        public bool ScheduleAccepted { get; set; }
        public bool InProgress { get; set; }
        public long EndsUnixMs { get; set; }
    }

    sealed class SiegeClanDto
    {
        public int CastleId { get; set; }
        public int ClanId { get; set; }
        public int Side { get; set; }
    }

    sealed class ClanMemberDto
    {
        public int CharacterId { get; set; }
        public string Account { get; set; } = "";
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Level { get; set; }
        public int PledgeType { get; set; }
    }

    sealed class StoredItemDto
    {
        public int ItemId { get; set; }
        public int Count { get; set; }
        public int EquippedSlot { get; set; } = -1;
        public int EnchantLevel { get; set; }
        public int ObjectId { get; set; }
    }
}
