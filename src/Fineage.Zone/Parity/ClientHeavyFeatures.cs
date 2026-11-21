namespace Fineage.Zone.Parity;

/// <summary>
/// Feature gates for Mobius systems that require Interlude Ex-packet wiring on the client.
/// All default <b>off</b> until Net emits the contracts in <c>docs/client-packet-contracts.md</c>.
/// </summary>
public static class ClientHeavyFeatures
{
    public const string CursedWeaponsVariable = "FINEAGE_ALLOW_CURSED_WEAPONS";
    public const string SkillEnchantVariable = "FINEAGE_ALLOW_SKILL_ENCHANT";

    public static bool CursedWeaponsEnabled => ReadBool(CursedWeaponsVariable, defaultValue: false);
    public static bool SkillEnchantEnabled => ReadBool(SkillEnchantVariable, defaultValue: false);

    static bool ReadBool(string variable, bool defaultValue)
    {
        string? text = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;
        return text.Trim() is "1" or "true" or "yes" or "on";
    }
}