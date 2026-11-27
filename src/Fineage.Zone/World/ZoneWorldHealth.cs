using System.Diagnostics;
using Fineage.Protocol.Health;

namespace Fineage.Zone.World;

/// <summary>#989: the simulation loop as a health check.</summary>
public sealed partial class ZoneWorld
{
    /// <summary>True once <see cref="StartSimulation"/> has started the loop.</summary>
    public bool SimulationStarted => _simulation != null;

    /// <summary>
    /// Ok while the simulation loop has passed its top within <paramref name="maxLag"/>. The loop passes at least
    /// once per step (it waits at most one step for the next tick), so a lag beyond a few steps means a stuck step,
    /// a dead loop, or a simulation that never started.
    /// </summary>
    public HealthCheck SimulationHealth(TimeSpan maxLag)
    {
        var task = _simulation;
        if (task == null)
            return new HealthCheck("simulation", false, "simulation not started");
        if (task.IsCompleted)
        {
            string why = task.Exception?.GetBaseException() is { } ex ? $"{ex.GetType().Name}: {ex.Message}" : task.Status.ToString();
            return new HealthCheck("simulation", false, $"simulation loop ended ({why})");
        }

        long beat = Volatile.Read(ref _loopHeartbeat);
        if (beat == 0)
            return new HealthCheck("simulation", false, "simulation loop has not run yet");
        var age = Stopwatch.GetElapsedTime(beat);
        if (age > maxLag)
        {
            string stuck = StepWatchdogReport(TimeSpan.Zero) ?? $"phase={StepPhase}";
            return new HealthCheck("simulation", false,
                $"last loop pass {age.TotalMilliseconds:0}ms ago (limit {maxLag.TotalMilliseconds:0}ms): {stuck}");
        }

        return new HealthCheck("simulation", true, $"tick {_simulationTicks.Tick}, last loop pass {age.TotalMilliseconds:0}ms ago");
    }
}
