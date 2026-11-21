namespace Fineage.Zone.Simulation;

/// <summary>
/// The fixed step a stage runs in. <paramref name="NowUnixMs"/> is derived from <paramref name="Tick"/>
/// (<see cref="SimulationClock.TimeOf"/>), so every stage and region of one step sees the same simulation time.
/// <paramref name="WallUnixMs"/> is the wall clock sampled when the step started, for real-world schedules (sieges,
/// auctions, lottery draws) that follow the calendar rather than the simulation.
/// </summary>
public readonly record struct TickContext(long Tick, long NowUnixMs, long WallUnixMs);
