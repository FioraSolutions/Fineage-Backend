namespace Fineage.Zone.World;

/// <summary>Guards against blocking economy persistence on the simulation thread.</summary>
internal static class EconomyPersistThreading
{
    public static void AssertNoBlockingWaitOnSimulationThread(WorldAffinity affinity, string operation)
    {
        if (affinity.OnStep)
            throw new InvalidOperationException(
                $"economy persist must not block the simulation thread ({operation})");
    }
}