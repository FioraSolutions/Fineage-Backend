using Fineage.Zone.Simulation;
using Fineage.Zone.World;

namespace Fineage.Zone.Tests;

/// <summary>
/// Deterministic simulation stepping for behavior tests: advances the world clock in whole
/// <see cref="ZoneSimulationSettings.StepMilliseconds"/> steps via <see cref="ZoneWorld.StepSimulation"/>.
/// </summary>
internal sealed class ManualSimulationDriver
{
    public static readonly DateTimeOffset FixedStartUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    readonly ZoneWorld _world;
    readonly MutableClock _clock;
    long _wallUnixMs;

    public ManualSimulationDriver(ZoneWorld world, MutableClock clock)
    {
        _world = world;
        _clock = clock;
        _wallUnixMs = FixedStartUtc.ToUnixTimeMilliseconds();
        _clock.Now = FixedStartUtc;
        _world.SimulationTicks.Resume(_wallUnixMs);
    }

    public long WallUnixMs => _wallUnixMs;

    public int StepMilliseconds => ZoneSimulationSettings.StepMilliseconds;

    public long Tick => _world.SimulationTicks.Tick;

    public void Step(int count = 1)
    {
        if (count < 1)
            throw new ArgumentOutOfRangeException(nameof(count));
        int stepMs = StepMilliseconds;
        for (int i = 0; i < count; i++)
        {
            _wallUnixMs += stepMs;
            _clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(_wallUnixMs);
            _world.StepSimulation();
        }
    }

    public void Advance(TimeSpan delta)
    {
        long ms = (long)delta.TotalMilliseconds;
        int stepMs = StepMilliseconds;
        if (ms % stepMs != 0)
            throw new ArgumentException($"Simulation advance must be a multiple of {stepMs} ms.", nameof(delta));
        if (ms < 0)
            throw new ArgumentOutOfRangeException(nameof(delta));
        Step((int)(ms / stepMs));
    }
}