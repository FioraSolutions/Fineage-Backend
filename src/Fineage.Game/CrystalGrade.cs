namespace Fineage.Game;

/// <summary>
/// Crystal type level used by the Interlude expertise penalty.
/// The numeric value is the level Mobius stores on the crystal type
/// (none 0, D 1, C 2, B 3, A 4, S 5). S80 and S84 are not Interlude grades.
/// </summary>
public enum CrystalGrade
{
    None = 0,
    D = 1,
    C = 2,
    B = 3,
    A = 4,
    S = 5
}
