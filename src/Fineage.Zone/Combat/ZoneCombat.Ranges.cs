namespace Fineage.Zone.Combat;

public static partial class ZoneCombat
{
    public static bool InMobMeleeRange(int ax, int ay, int bx, int by) => InMobReach(ax, ay, bx, by, MobMeleeRange);

    /// <summary>
    /// NPC reach with default collisions (NPC <see cref="DefaultNpcCollision"/> + player 9). Prefer
    /// <see cref="AttackReach"/> with the live radii when a target is known.
    /// </summary>
    public static int MobAttackReach(int attackRange) =>
        AttackReach(attackRange, DefaultNpcCollision, PlayerCollisionRadius);

    public static bool InMobReach(int ax, int ay, int bx, int by, int reach)
    {
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)reach * reach;
    }

    /// <summary>Placeholder until NPC pAtk formulas are imported: scales with level.</summary>
    public static int MobMeleeDamage(int level) => Math.Max(1, 4 + level * 2);

    /// <summary>Mobius <c>AttackableAI.thinkActive</c> / <c>autoAttackCondition</c>: 2D radius from template <c>aggroRange</c> and |dz| ≤ <see cref="AggroMaxZDelta"/>.</summary>
    public static bool WithinProactiveAggroRange(int ax, int ay, int az, int bx, int by, int bz, int aggroRange)
    {
        if (aggroRange <= 0)
            return false;
        if (Math.Abs(az - bz) > AggroMaxZDelta)
            return false;
        long dx = ax - (long)bx;
        long dy = ay - (long)by;
        return dx * dx + dy * dy <= (long)aggroRange * aggroRange;
    }

    /// <summary>Combat leash / hate forget (Mobius pursuit), not proactive acquisition — always <see cref="AggroForgetRange"/>.</summary>
    public static bool WithinAggroForgetRange(int ax, int ay, int bx, int by)
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
