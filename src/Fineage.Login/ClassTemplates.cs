using Fineage.Protocol;

namespace Fineage.Login;

/// <summary>
/// Base-class facts needed to create a character. Level-1 HP/MP are the player-facing
/// maxima (class base times the race's base CON/MEN), not a stat formula.
/// Human Fighter is 126/38 and Human Mystic is 99/59. Warrior (class 1) is recorded
/// only so a first-class request fails as a non-base class. This is not a Mobius table.
/// </summary>
public sealed record ClassTemplate(int ClassId, int Race, int ClassLevel, int MaxHp, int MaxMp, ClassSpawn.Point CreationPoint)
{
    public bool IsBase => ClassLevel == 0;
}

public static class ClassTemplates
{
    public const int HumanFighterHp = 126;
    public const int HumanFighterMp = 38;
    public const int HumanMysticHp = 99;
    public const int HumanMysticMp = 59;

    static readonly ClassTemplate[] Templates =
    [
        new(HumanClasses.Fighter, HumanClasses.RaceHuman, 0, HumanFighterHp, HumanFighterMp, ClassSpawn.Preview(HumanClasses.Fighter)),
        new(1, HumanClasses.RaceHuman, 1, 0, 0, ClassSpawn.Preview(HumanClasses.Fighter)),
        new(HumanClasses.Mystic, HumanClasses.RaceHuman, 0, HumanMysticHp, HumanMysticMp, ClassSpawn.Preview(HumanClasses.Mystic))
    ];

    public static bool TryGet(int classId, out ClassTemplate? template)
    {
        foreach (var row in Templates)
        {
            if (row.ClassId == classId)
            {
                template = row;
                return true;
            }
        }

        template = null;
        return false;
    }
}
