namespace Fineage.Zone;

/// <summary>
/// <c>FINEAGE_EXPERTISE_PENALTY</c> matches Mobius <c>ExpertisePenalty</c>, which defaults to true.
/// <c>0</c>, <c>false</c>, or <c>no</c> turns the penalty off. Any other value, including unset, leaves it on.
/// Off means higher-grade gear still equips and the UserInfo penalty keys stay 0.
/// </summary>
public static class ZoneGearSettings
{
    public const string ExpertisePenaltyVariable = "FINEAGE_EXPERTISE_PENALTY";

    public static bool ExpertisePenalty => ParseExpertisePenalty(Environment.GetEnvironmentVariable(ExpertisePenaltyVariable));

    public static bool ParseExpertisePenalty(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var token = text.Trim();
        if (token == "0"
            || token.Equals("false", StringComparison.OrdinalIgnoreCase)
            || token.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
