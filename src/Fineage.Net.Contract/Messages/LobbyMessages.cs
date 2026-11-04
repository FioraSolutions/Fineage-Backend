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

    /// <summary>
    /// CharSelect row (Mobius CharSelectionInfo shape). Matches V2 <c>CharacterSummaryMsg</c> fields
    /// used by the lobby UI: vitals, CP, paperdoll[17], position, delete timer, last-played.
    /// </summary>
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
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int DeleteSecondsLeft { get; set; }
        public bool IsLastPlayed { get; set; }
        public int CurCp { get; set; }
        public int MaxCp { get; set; }
        public long Exp { get; set; }
        public int Sp { get; set; }
        public int Karma { get; set; }
        public int WeaponItemId { get; set; }
        /// <summary>Equipped item ids by paperdoll slot (length 17). Empty slot = 0.</summary>
        public int[] Paperdoll { get; set; } = System.Array.Empty<int>();
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
