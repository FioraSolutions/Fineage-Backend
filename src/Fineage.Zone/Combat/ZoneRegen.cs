namespace Fineage.Zone.Combat;

/// <summary>
/// Standing/sitting HP/MP per simulation tick. Rates scale from saved maxima and level;
/// not full Interlude CON/MEN formulas.
/// </summary>
public static class ZoneRegen
{
    public const int DefaultTickMs = 3000;

    public static int HpPerTick(int maxHp, int level, bool sitting)
    {
        int rate = Math.Max(1, maxHp / 64 + level / 12);
        return sitting ? rate * 2 : rate;
    }

    public static int MpPerTick(int maxMp, int level, bool sitting)
    {
        int rate = Math.Max(1, maxMp / 32 + level / 10);
        return sitting ? rate * 2 : rate;
    }
}
