namespace Fineage.Zone.World;

/// <summary>
/// Who submitted a command through ingress. Audit and metrics metadata only;
/// it does not change validation, rate limits, or the apply path.
/// </summary>
public enum CommandSource
{
    Player = 0,
    Ai = 1,
    System = 2
}

public static class CommandSourceTags
{
    public static string Tag(CommandSource source) => source switch
    {
        CommandSource.Ai => "ai",
        CommandSource.System => "system",
        _ => "player"
    };
}
