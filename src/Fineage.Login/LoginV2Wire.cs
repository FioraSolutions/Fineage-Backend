using Fineage.Protocol.V2;

namespace Fineage.Login;

/// <summary>Maps account records to Unity V2 login wire shapes (separate from legacy 20+/1020+ opcodes).</summary>
static class LoginV2Wire
{
    public static CharacterSummaryMsg ToSummary(CharacterRecord character, int slot)
    {
        var start = Fineage.Protocol.ClassSpawn.Preview(character.ClassId);
        return new CharacterSummaryMsg
        {
            Slot = slot,
            ObjectId = character.Id,
            Name = character.Name,
            Race = character.Race,
            Sex = character.Sex,
            ClassId = character.ClassId,
            Level = character.Level,
            X = start.X,
            Y = start.Y,
            Z = start.Z
        };
    }

    public static CharacterRosterMsg ToRoster(IReadOnlyList<CharacterRecord> characters)
    {
        var rows = new CharacterSummaryMsg[characters.Count];
        for (int i = 0; i < characters.Count; i++)
            rows[i] = ToSummary(characters[i], i);
        return new CharacterRosterMsg { Characters = rows };
    }

    public static CreateCharacterResultMsg ToCreateResult(CharacterRecord character, int slot) => new()
    {
        Ok = true,
        Reason = "",
        Character = ToSummary(character, slot)
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
