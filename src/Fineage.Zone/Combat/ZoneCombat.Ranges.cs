namespace Fineage.Zone.Combat;

public static partial class ZoneCombat
{
    public static bool InMobMeleeRange(int ax, int ay, int bx, int by) => InMobReach(ax, ay, bx, by, MobMeleeRange);

    /// <summary>NPC reach for its template atkRange (Mobius physRange + collisions): max(40, range) + collision allowance.</summary>
    public static int MobAttackReach(int attackRange) => PlayerAttackReach(attackRange);

    public static bool InMobReach(int ax, int ay, int bx, int by, int reach)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)reach * reach;
    }

    /// <summary>Placeholder until NPC pAtk formulas are imported: scales with level.</summary>
    public static int MobMeleeDamage(int level) => Math.Max(1, 4 + level * 2);

    public static bool InAggroRange(int ax, int ay, int bx, int by)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)AggroForgetRange * AggroForgetRange;
    }

    public static bool InMeleeRange(int ax, int ay, int bx, int by)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)MeleeRange * MeleeRange;
    }

    public static bool InPickupRange(int ax, int ay, int bx, int by)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)PickupRange * PickupRange;
    }

    public static bool InDropRange(int ax, int ay, int az, int bx, int by, int bz)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        long dz = az - (long)bz;
        return Math.Abs(dz) <= DropMaxZDelta && dx * dx + dy * dy + dz * dz <= (long)DropRange * DropRange;
    }

    public static bool InCastRange(int ax, int ay, int bx, int by, int castRange)
    {
        int range = Math.Max(40, castRange);
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)range * range;
    }

    public static bool Within(int ax, int ay, int bx, int by, int range)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)range * range;
    }

    /// <summary>Distance from point P to segment AB (approach path).</summary>
    public static double DistanceToSegment(int px, int py, int ax, int ay, int bx, int by)
    {
        double vx = bx - ax, vy = by - ay;
        double len2 = vx * vx + vy * vy;
        double t = len2 <= 0 ? 0 : Math.Clamp(((px - ax) * vx + (py - ay) * vy) / len2, 0, 1);
        double cx = ax + t * vx - px, cy = ay + t * vy - py;
        return Math.Sqrt(cx * cx + cy * cy);
    }
}
