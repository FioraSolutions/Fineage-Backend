using Fineage.Protocol.V2;

namespace Fineage.Login;

/// <summary>Maps account records to Unity V2 login wire shapes (separate from legacy 20+/1020+ opcodes).</summary>
static class LoginV2Wire
{
    public static CharacterSummaryMsg ToSummary(CharacterRecord character, int slot, DateTimeOffset now, int lastPlayedId)
    {
        var point = character.HasPosition
            ? new Fineage.Protocol.ClassSpawn.Point(character.X, character.Y, character.Z)
            : ClassTemplates.Preview(character.ClassId);
        return new CharacterSummaryMsg
        {
            Slot = slot,
            ObjectId = character.Id,
            Name = character.Name,
            Race = character.Race,
            Sex = character.Sex,
            ClassId = character.ClassId,
            Level = character.Level,
            X = point.X,
            Y = point.Y,
            Z = point.Z,
            HairStyle = character.HairStyle,
            HairColor = character.HairColor,
            Face = character.Face,
            DeleteSecondsLeft = character.DeleteSecondsLeft(now),
            IsLastPlayed = character.Id == lastPlayedId
        };
    }

    public static CharacterRosterMsg ToRoster(IReadOnlyList<CharacterRecord> characters, DateTimeOffset now)
    {
        int lastPlayedId = LoginService.LastPlayedId(characters);
        var rows = new CharacterSummaryMsg[characters.Count];
        for (int i = 0; i < characters.Count; i++)
            rows[i] = ToSummary(characters[i], i, now, lastPlayedId);
        return new CharacterRosterMsg { Characters = rows };
    }

    public static CreateCharacterResultMsg ToCreateResult(CharacterRecord character, int slot, DateTimeOffset now, int lastPlayedId) => new()
    {
        Ok = true,
        Reason = "",
        Character = ToSummary(character, slot, now, lastPlayedId)
    };

    public static CreateCharacterResultMsg CreateFailed(string code, string message) => new()
    {
        Ok = false,
        Reason = code,
        Character = new CharacterSummaryMsg()
    };

    public static ServerListMsg DefaultServerList()
    {
        return new ServerListMsg
        {
            Servers =
            [
                new ServerEntryMsg
                {
                    ServerId = 1,
                    Host = ZoneHandoff.AdvertiseHost,
                    Port = ZoneHandoff.AdvertisePort,
                    CurrentPlayers = 0,
                    MaxPlayers = 2000,
                    Region = "local",
                    Name = "Fineage",
                    Status = 1
                }
            ]
        };
    }

    public static PlayTokenMsg ToPlayToken(HandoffTicket ticket) => new()
    {
        PlayKey1 = ticket.CharacterId,
        PlayKey2 = 0,
        LoginKey1 = 0,
        LoginKey2 = 0,
        ZoneHost = ticket.ZoneHost,
        ZonePort = ticket.ZonePort,
        ServerId = 1,
        Region = "local"
    };
}
