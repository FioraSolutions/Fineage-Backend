using Fineage.Protocol;

namespace Fineage.Login;

public static class LobbyWire
{
    public static CharacterSummaryMsg ToSummary(CharacterRecord character, int slot, DateTimeOffset now, int lastPlayedId)
    {
        var point = character.HasPosition
            ? new ClassSpawn.Point(character.X, character.Y, character.Z)
            : ClassSpawn.Preview(character.ClassId);
        return new CharacterSummaryMsg
        {
            CharacterId = character.Id,
            Name = character.Name,
            ClassId = character.ClassId,
            Race = character.Race,
            Sex = character.Sex,
            Level = character.Level,
            Slot = slot,
            HairStyle = character.HairStyle,
            HairColor = character.HairColor,
            Face = character.Face,
            X = point.X,
            Y = point.Y,
            Z = point.Z,
            DeleteSecondsLeft = character.DeleteSecondsLeft(now),
            IsLastPlayed = character.Id == lastPlayedId
        };
    }

    public static CharacterSummaryMsg[] ToSummaries(IReadOnlyList<CharacterRecord> characters, DateTimeOffset now)
    {
        int lastPlayedId = LoginService.LastPlayedId(characters);
        var rows = new CharacterSummaryMsg[characters.Count];
        for (int i = 0; i < characters.Count; i++)
            rows[i] = ToSummary(characters[i], i, now, lastPlayedId);
        return rows;
    }
}
