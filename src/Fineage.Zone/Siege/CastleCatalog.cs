namespace Fineage.Zone.Siege;

/// <summary>
/// Nine Interlude castles. Ids and names match Mobius <c>castle.sql</c>.
/// Day and hour match <c>SiegeSchedule.xml</c> (Sunday 16:00 or 20:00, five at a time).
/// </summary>
public static class CastleCatalog
{
    public static readonly CastleTemplate[] Interlude =
    [
        new(1, "Gludio", DayOfWeek.Sunday, 16, 5),
        new(2, "Dion", DayOfWeek.Sunday, 20, 5),
        new(3, "Giran", DayOfWeek.Sunday, 16, 5),
        new(4, "Oren", DayOfWeek.Sunday, 20, 5),
        new(5, "Aden", DayOfWeek.Sunday, 16, 5),
        new(6, "Innadril", DayOfWeek.Sunday, 20, 5),
        new(7, "Goddard", DayOfWeek.Sunday, 16, 5),
        new(8, "Rune", DayOfWeek.Sunday, 20, 5),
        new(9, "Schuttgart", DayOfWeek.Sunday, 16, 5)
    ];

    public static CastleTemplate? Find(int castleId)
    {
        foreach (var castle in Interlude)
        {
            if (castle.Id == castleId)
                return castle;
        }

        return null;
    }
}

public readonly record struct CastleTemplate(int Id, string Name, DayOfWeek Day, int Hour, int MaxConcurrent);
