namespace Fineage.Zone;

/// <summary>
/// Plausibility clamp for a single client move request, in L2 units (1 Unity m ≈ 52.5 u).
/// <para>
/// This is a "is this packet sane" guard, not a region boundary: click-to-move crosses a whole village,
/// and every spawn map shares it. It used to live on <see cref="TalkingIsland"/> as
/// <c>MaxStep = 8000</c>, a leftover from the Cedric training-hall smoke scene, which clipped legitimate
/// long clicks and looked like a movement bug.
/// </para>
/// <para>
/// Real teleports and server-driven approaches do not pass through here; only player-issued move input does.
/// </para>
/// </summary>
public static class MoveLimits
{
    /// <summary>Largest distance one move request may cover, in L2 units (30 000 ≈ 570 m).</summary>
    public const int MaxStep = 30000;

    public static bool WithinStep(int fromX, int fromY, int toX, int toY)
    {
        long dx = fromX - (long)toX;
        long dy = fromY - (long)toY;
        return dx * dx + dy * dy <= (long)MaxStep * MaxStep;
    }
}
