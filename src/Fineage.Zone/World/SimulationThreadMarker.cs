namespace Fineage.Zone.World;

/// <summary>
/// Marks the thread running an authoritative simulation step. Used with <see cref="StepWaitRegistry"/> so a blocking
/// wait on the simulation thread fails fast in diagnostics builds.
/// </summary>
internal static class SimulationThreadMarker
{
    [ThreadStatic] static int t_depth;

    public static bool Active => t_depth > 0;

    public static void Enter() => t_depth++;

    public static void Exit()
    {
        if (t_depth > 0)
            t_depth--;
    }
}