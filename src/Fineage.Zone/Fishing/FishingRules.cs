namespace Fineage.Zone.Fishing;

/// <summary>
/// Pure fishing rolls after Mobius <c>Player.getRandomFishLvl / getRandomFishGrade / getRandomFishGroup</c>,
/// <c>startLookingForFishTask</c> and the <c>Pumping</c> / <c>Reeling</c> damage. Reimplemented, not copied.
/// </summary>
public static class FishingRules
{
    public const int MaxFishLevel = 27;

    /// <summary>Night lures bite only at night; every other lure only by day.</summary>
    public static bool IsNightLure(int lureId) => lureId is >= 8505 and <= 8513 or 8485;

    /// <summary>Fish grade the lure hooks: 0 beginner, 1 normal, 2 upper.</summary>
    public static int Grade(int lureId) => lureId switch
    {
        7807 or 7808 or 7809 or 8486 => 0,
        8485 or 8506 or 8509 or 8512 => 2,
        _ => 1
    };

    /// <summary>Fish level around the Fishing Expertise level: half the time the same, 35% one less, 15% one more.</summary>
    public static int Level(int expertiseLevel, int roll100)
    {
        if (expertiseLevel <= 0)
            return 1;
        if (roll100 <= 50)
            return expertiseLevel;
        if (roll100 <= 85)
            return Math.Max(1, expertiseLevel - 1);
        return Math.Min(MaxFishLevel, expertiseLevel + 1);
    }

    /// <summary>Fish group (wide 0, swift 1, ugly 2, box 3, easy 4–6, hard 7–9) by lure and one d100 roll.</summary>
    public static int Group(int lureId, int grade, int roll100)
    {
        int r = roll100;
        static int Three(int r, int lo, int a, int mid, int b, int c) => r <= lo ? a : r <= mid ? b : c;
        switch (grade)
        {
            case 0:
                return lureId switch
                {
                    7807 => Three(r, 54, 5, 77, 4, 6),
                    7808 => Three(r, 54, 4, 77, 6, 5),
                    7809 => Three(r, 54, 6, 77, 5, 4),
                    8486 => Three(r, 33, 4, 66, 5, 6),
                    _ => 4
                };
            case 2:
                return lureId switch
                {
                    8506 => Three(r, 54, 8, 77, 7, 9),
                    8509 => Three(r, 54, 7, 77, 9, 8),
                    8512 => Three(r, 54, 9, 77, 8, 7),
                    8485 => Three(r, 33, 7, 66, 8, 9),
                    _ => 7
                };
            default:
                switch (lureId)
                {
                    case >= 7610 and <= 7613:
                        return 3;
                    case 6519 or 8505 or 6520 or 6521 or 8507:
                        return r <= 54 ? 1 : r <= 74 ? 0 : r <= 94 ? 2 : 3;
                    case 6522 or 8508 or 6523 or 6524 or 8510:
                        return r <= 54 ? 0 : r <= 74 ? 1 : r <= 94 ? 2 : 3;
                    case 6525 or 8511 or 6526 or 6527 or 8513:
                        return r <= 55 ? 2 : r <= 74 ? 1 : r <= 94 ? 0 : 3;
                    case 8484:
                        return Three(r, 33, 0, 66, 1, 2);
                    default:
                        return 1;
                }
        }
    }

    /// <summary>
    /// Bite check period: <c>gutsCheckTime</c> scaled by the lure (low-grade 133, regular 100, high-grade 66).
    /// Lures Mobius leaves out of every list would get a zero period there; they use the regular one here.
    /// </summary>
    public static int CheckDelayMs(int lureId, int gutsCheckTime)
    {
        if (lureId is 6519 or 6522 or 6525 or 8505 or 8508 or 8511)
            return gutsCheckTime * 133;
        if (lureId is 6520 or 6523 or 6526 or (>= 8505 and <= 8513) or (>= 7610 and <= 7613) or (>= 7807 and <= 7809)
            or (>= 8484 and <= 8486))
            return gutsCheckTime * 100;
        if (lureId is 6521 or 6524 or 6527 or 8507 or 8510 or 8513)
            return gutsCheckTime * 66;
        return gutsCheckTime * 100;
    }

    /// <summary>
    /// Pump / reel damage: (rod damage + expertise + skill power) · rod level · 0.1, doubled by a fishing shot,
    /// then a 5% penalty when the skill is three or more levels above Fishing Expertise.
    /// </summary>
    public static (int Damage, int Penalty) Damage(double rodDamage, int rodLevel, double expertise, double skillPower,
        bool shot, int expertiseLevel, int skillLevel)
    {
        int dmg = (int)((rodDamage + expertise + skillPower) * (rodLevel * 0.1) * (shot ? 2 : 1));
        int pen = 0;
        if (expertiseLevel < skillLevel - 2)
        {
            pen = (int)(dmg * 0.05);
            dmg -= pen;
        }
        return (dmg, pen);
    }
}
