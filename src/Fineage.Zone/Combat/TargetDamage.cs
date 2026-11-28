namespace Fineage.Zone.Combat;

/// <summary>
/// Actual vitals removed by one incoming hit, split exactly like Mobius
/// <c>PlayerStatus.reduceHp</c>: CP first while a playable attacker is credited, then HP.
/// </summary>
public readonly record struct DamageSplit(int CpLoss, int HpLoss)
{
    public static readonly DamageSplit None = new(0, 0);

    public int TotalLoss => CpLoss + HpLoss;
}

/// <summary>
/// Pure CP barrier from Mobius CT0 <c>PlayerStatus.reduceHp</c> (<c>PlayerStatus.java:212-222</c>):
/// <code>
/// if (!ignoreCP &amp;&amp; (attacker.isPlayable() || attacker.isFakePlayer())) {
///     if (_currentCp &gt;= amount) { _currentCp -= amount; amount = 0; }
///     else { amount -= _currentCp; setCurrentCp(0); }
/// }
/// </code>
/// The caller decides whether the target owns a CP pool (players only in Interlude) and whether the
/// attacker is playable (player or summon). NPC attackers and missing attackers never reach the CP branch.
/// </summary>
public static class TargetDamage
{
    /// <summary>
    /// Split <paramref name="amount"/> into CP and HP loss. <paramref name="cpAbsorbs"/> is the resolved
    /// Mobius <c>attacker.isPlayable() &amp;&amp; !ignoreCP</c> condition for a player target.
    /// </summary>
    public static DamageSplit Split(int amount, int currentCp, int currentHp, bool cpAbsorbs, bool ignoreCp = false)
    {
        if (amount <= 0)
            return DamageSplit.None;

        int cpLoss = 0;
        int remaining = amount;
        if (cpAbsorbs && !ignoreCp)
        {
            cpLoss = Math.Min(Math.Max(0, currentCp), amount);
            remaining -= cpLoss;
        }

        int hpLoss = Math.Min(Math.Max(0, currentHp), remaining);
        return new DamageSplit(cpLoss, hpLoss);
    }
}
