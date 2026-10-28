namespace Fineage.Zone.Bosses;

/// <summary>Raid bosses use the spawn-list window. Grand bosses use GrandBoss.ini hours.</summary>
public enum BossKind
{
    Raid = 0,
    Grand = 1
}

/// <summary>
/// One status space for raid and grand bosses.
/// Raid rows use <see cref="Alive"/> and <see cref="Dead"/>.
/// Grand rows also use <see cref="Waiting"/> and <see cref="Fighting"/>.
/// </summary>
public enum BossLifeStatus
{
    Alive = 0,
    Waiting = 1,
    Fighting = 2,
    Dead = 3
}
