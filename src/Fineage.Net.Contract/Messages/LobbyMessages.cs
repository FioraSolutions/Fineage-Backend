namespace Fineage.Net.Contract
{
    public sealed class AuthMsg
    {
        public string Account { get; set; } = "";
        public string Password { get; set; } = "";
        public bool RegisterIfMissing { get; set; }
    }

    public sealed class AuthResultMsg
    {
        public bool Ok { get; set; }
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public string Account { get; set; } = "";
    }

    public sealed class ListCharactersMsg
    {
    }

    public sealed class CharacterSummaryNet
    {
        public int CharacterId { get; set; }
        public int Slot { get; set; }
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int Level { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        public int HairStyle { get; set; }
        public int HairColor { get; set; }
        public int Face { get; set; }
        public bool PendingDelete { get; set; }
    }

    public sealed class CharacterRosterMsg
    {
        public CharacterSummaryNet[] Characters { get; set; } = System.Array.Empty<CharacterSummaryNet>();
    }

    public sealed class CreateCharacterMsg
    {
        public string Name { get; set; } = "";
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int HairStyle { get; set; }
        public int HairColor { get; set; }
        public int Face { get; set; }
    }

    public sealed class CreateResultMsg
    {
        public bool Ok { get; set; }
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public CharacterSummaryNet? Character { get; set; }
    }

    public sealed class SelectCharacterMsg
    {
        public int CharacterId { get; set; }
        public int Slot { get; set; } = -1;
        public string Name { get; set; } = "";
    }

    public sealed class HandoffMsg
    {
        public string Account { get; set; } = "";
        public int CharacterId { get; set; }
        public string CharacterName { get; set; } = "";
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public string Token { get; set; } = "";
        public long ExpiresUnixSeconds { get; set; }
        public string ZoneHost { get; set; } = "";
        public int ZonePort { get; set; }
    }
}
