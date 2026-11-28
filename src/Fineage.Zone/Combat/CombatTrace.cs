using Fineage.Accounts;

namespace Fineage.Zone.Combat;

/// <summary>
/// Melee timing trace (category <c>Fineage.Zone.CombatTrace</c>): one line per swing, hit and status update with
/// the simulation time <c>t</c> and the wall clock <c>wall</c> (unix ms), so a session can be lined up with the
/// client's <c>combat-trace.txt</c> on the same machine. On by default in Debug builds; <c>FINEAGE_COMBAT_TRACE=0</c>
/// turns it off, <c>=1</c> turns it on in Release.
/// </summary>
public static class CombatTrace
{
    public const string Category = "Fineage.Zone.CombatTrace";
    public const string Variable = "FINEAGE_COMBAT_TRACE";

    public static bool Enabled { get; set; } = ResolveDefault(Environment.GetEnvironmentVariable(Variable));

    public static bool ResolveDefault(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim() is "1" or "true" or "on" or "yes";
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    public static long WallUnixMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public static void Write(string eventName, params (string Key, object? Value)[] fields)
    {
        if (!Enabled)
            return;
        var all = new (string Key, object? Value)[fields.Length + 1];
        all[0] = ("wall", WallUnixMs());
        Array.Copy(fields, 0, all, 1, fields.Length);
        FineageLogging.Write(Category, "info", eventName, all);
    }
}
