namespace Fineage.Zone.Simulation;

/// <summary>
/// The fixed step a stage runs in: <paramref name="Tick"/> counts steps since the scheduler started and
/// <paramref name="NowUnixMs"/> is sampled once per step, so every stage and region of one step sees the same time.
/// </summary>
public readonly record struct TickContext(long Tick, long NowUnixMs);
