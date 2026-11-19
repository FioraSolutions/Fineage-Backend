using Fineage.Accounts;
namespace Fineage.Login;

/// <summary>
/// Lobby character policy. Default is immediate hard delete. Set
/// <c>FINEAGE_DELETE_CHAR_AFTER_DAYS</c> above 0 for a Mobius-style pending timer.
/// </summary>
public static class CharacterRules
{
    public const int DefaultDeleteAfterDays = 0;

    public static int DeleteAfterDays =>
        ReadInt("FINEAGE_DELETE_CHAR_AFTER_DAYS", DefaultDeleteAfterDays, min: 0, max: 3650);

    static int ReadInt(string name, int fallback, int min, int max)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value))
            return fallback;
        return Math.Clamp(value, min, max);
    }
}

public sealed record CharacterDeleteOutcome(
    int Slot,
    int CharacterId,
    bool HardDeleted,
    IReadOnlyList<CharacterRecord> Roster);
