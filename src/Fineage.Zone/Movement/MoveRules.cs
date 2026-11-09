namespace Fineage.Zone.Movement;

/// <summary>
/// Pure movement helpers (stub geo Z clamp). World owns geo engine, doors, and timed segments.
/// </summary>
public static class MoveRules
{
    /// <summary>Max Z change allowed per unit of horizontal travel (≈ 45° slope) before client Z is treated as bogus.</summary>
    public const double MaxZPerXy = 1.0;

    /// <summary>Flat Z tolerance (stairs / small steps) on top of the slope allowance.</summary>
    public const int MaxZFlat = 96;

    /// <summary>Stub geo: client dest Z far off a sane slope from the current ground is replaced with current Z.</summary>
    public static int SanitizeDestZ(int curX, int curY, int curZ, int destX, int destY, int destZ)
    {
        double xy = Math.Sqrt(
            (double)(destX - curX) * (destX - curX) + (double)(destY - curY) * (destY - curY));
        double allowed = MaxZFlat + xy * MaxZPerXy;
        return Math.Abs(destZ - curZ) > allowed ? curZ : destZ;
    }
}
