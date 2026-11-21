namespace Fineage.Game.Punishment;

public static class PunishmentStatePaths
{
    public const string StateDirEnvVar = "FINEAGE_STATE_DIR";
    public static string ResolveStateDirectory(string? overrideDir = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDir)) return overrideDir;
        var fromEnv = Environment.GetEnvironmentVariable(StateDirEnvVar);
        return string.IsNullOrWhiteSpace(fromEnv) ? "data" : fromEnv;
    }
    public static string PunishmentsJsonPath(string? stateDirectory = null) =>
      Path.Combine(ResolveStateDirectory(stateDirectory), "punishments.json");
}
