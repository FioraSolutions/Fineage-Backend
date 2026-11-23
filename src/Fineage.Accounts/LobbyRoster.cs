namespace Fineage.Accounts;

/// <summary>
/// One character-select row. Login and the zone restart path both project this, so the lobby
/// the player returns to is the same roster the login server would have sent.
/// </summary>
public readonly record struct LobbyCharacterRow(
    int CharacterId,
    int Slot,
    string Name,
    int ClassId,
    int Race,
    int Sex,
    int Level,
    int Hp,
    int MaxHp,
    int Mp,
    int MaxMp,
    int HairStyle,
    int HairColor,
    int Face,
    bool PendingDelete,
    int X,
    int Y,
    int Z,
    int DeleteSecondsLeft,
    bool IsLastPlayed,
    int CurCp,
    int MaxCp,
    long Exp,
    int Sp,
    int Karma,
    int WeaponItemId,
    int[] Paperdoll);

public static class LobbyRoster
{
    /// <summary>Character id with the greatest <see cref="CharacterRecord.LastAccess"/>, or 0 when nobody has played.</summary>
    public static int LastPlayedId(IReadOnlyList<CharacterRecord> characters)
    {
        ArgumentNullException.ThrowIfNull(characters);
        CharacterRecord? best = null;
        foreach (var character in characters)
        {
            if (character.LastAccess == default)
                continue;
            if (best == null || character.LastAccess >= best.LastAccess)
                best = character;
        }

        return best?.Id ?? 0;
    }

    public static LobbyCharacterRow[] Rows(IReadOnlyList<CharacterRecord> characters, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(characters);
        int lastPlayedId = LastPlayedId(characters);
        var rows = new LobbyCharacterRow[characters.Count];
        for (int i = 0; i < characters.Count; i++)
            rows[i] = Row(characters[i], i, now, lastPlayedId);
        return rows;
    }

    public static LobbyCharacterRow Row(CharacterRecord character, int slot, DateTimeOffset now, int lastPlayedId)
    {
        var point = character.HasPosition
            ? new Fineage.Protocol.ClassSpawn.Point(character.X, character.Y, character.Z)
            : ClassTemplates.Preview(character.ClassId);
        var stats = CharSelectStats.For(character);
        int deleteSeconds = character.DeleteSecondsLeft(now);
        return new LobbyCharacterRow(
            character.Id,
            slot,
            character.Name,
            character.ClassId,
            character.Race,
            character.Sex,
            character.Level <= 0 ? 1 : character.Level,
            stats.CurHp,
            stats.MaxHp,
            stats.CurMp,
            stats.MaxMp,
            character.HairStyle,
            character.HairColor,
            character.Face,
            deleteSeconds > 0 || character.IsPendingDelete,
            point.X,
            point.Y,
            point.Z,
            deleteSeconds,
            character.Id == lastPlayedId,
            stats.CurCp,
            stats.MaxCp,
            character.Exp,
            character.Sp,
            character.Karma,
            stats.WeaponItemId,
            stats.Paperdoll);
    }
}
