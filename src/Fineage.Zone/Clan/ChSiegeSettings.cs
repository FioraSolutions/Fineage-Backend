namespace Fineage.Zone.Clan;

/// <summary>Mobius <c>ConquerableHallSiege.ini</c>.</summary>
public sealed class ChSiegeSettings
{
    public int MinClanLevel { get; init; } = 4;
    public int MaxAttackers { get; init; } = 500;
    public long RegistrationCloseBeforeMs { get; init; } = 3_600_000;

    public static ChSiegeSettings Default { get; } = new();
}