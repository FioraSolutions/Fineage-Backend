namespace Fineage.Zone.World;

/// <summary>Guards against blocking economy persistence on the simulation thread.</summary>
internal static class EconomyPersistThreading
{
    /// <summary>
    /// A durable-commit wait is only illegal inside a simulation step. Holding the world turn is not a step:
    /// every request, test hook and logout path takes the turn before it touches world state and then has to
    /// see its commit land, so keying this on the turn made those paths skip the wait and leave the character
    /// reserved, which refused the next economy move of the same character.
    /// </summary>
    public static void AssertNoBlockingWaitOnSimulationThread(bool inStep, string operation)
    {
        if (inStep)
            throw new InvalidOperationException(
                $"economy persist must not block the simulation thread ({operation})");
    }
}
