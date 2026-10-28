using Fineage.Protocol;

namespace Fineage.Zone.Siege;

/// <summary>Mobius <c>Siege.ini</c> values. Environment overrides are optional and positive.</summary>
public sealed class SiegeSettings
{
    public int CycleWeeks { get; init; } = SiegeProtocol.CycleWeeks;
    public int LengthMinutes { get; init; } = SiegeProtocol.LengthMinutes;
    public int MinClanLevel { get; init; } = SiegeProtocol.MinClanLevel;
    public int MaxAttackers { get; init; } = SiegeProtocol.MaxAttackers;
    public int MaxDefenders { get; init; } = SiegeProtocol.MaxDefenders;
    public long RegistrationCloseMillis { get; init; } = SiegeProtocol.RegistrationCloseMillis;

    public static SiegeSettings Mobius { get; } = new();

    public static SiegeSettings FromEnvironment() => new()
    {
        CycleWeeks = Read("FINEAGE_SIEGE_CYCLE_WEEKS", SiegeProtocol.CycleWeeks),
        LengthMinutes = Read("FINEAGE_SIEGE_LENGTH_MINUTES", SiegeProtocol.LengthMinutes),
        MinClanLevel = Read("FINEAGE_SIEGE_MIN_CLAN_LEVEL", SiegeProtocol.MinClanLevel),
        MaxAttackers = Read("FINEAGE_SIEGE_MAX_ATTACKERS", SiegeProtocol.MaxAttackers),
        MaxDefenders = Read("FINEAGE_SIEGE_MAX_DEFENDERS", SiegeProtocol.MaxDefenders),
        RegistrationCloseMillis = Read("FINEAGE_SIEGE_REGISTRATION_CLOSE_HOURS", 24) * 3_600_000L
    };

    static int Read(string name, int fallback)
    {
        string? text = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(text) || !int.TryParse(text, out int value) || value < 1)
            return fallback;
        return value;
    }
}
