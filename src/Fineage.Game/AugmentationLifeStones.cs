namespace Fineage.Game;

/// <summary>Mobius <c>AbstractRefinePacket</c> life stones (Interlude 8723–8762) and gemstone rules.</summary>
public static class AugmentationLifeStones
{
    public const int GradeNone = 0;
    public const int GradeMid = 1;
    public const int GradeHigh = 2;
    public const int GradeTop = 3;
    public const int GradeAcc = 4;

    public const int GemstoneD = 2130;
    public const int GemstoneC = 2131;
    public const int GemstoneB = 2132;

    static readonly int[] PlayerLevels =
    [
        46, 49, 52, 55, 58, 61, 64, 67, 70, 76,
        80, 82, 84, 85
    ];

    static readonly Dictionary<int, LifeStoneRow> Stones = BuildStones();

    public readonly record struct LifeStoneRow(int Grade, int Level, int MinPlayerLevel);

    public static bool TryGet(int itemId, out LifeStoneRow stone) => Stones.TryGetValue(itemId, out stone);

    public static bool IsLifeStone(int itemId) => Stones.ContainsKey(itemId);

    public static int GetGemStoneId(CrystalGrade itemGrade) => itemGrade switch
    {
        CrystalGrade.C or CrystalGrade.B => GemstoneD,
        CrystalGrade.A or CrystalGrade.S => GemstoneC,
        _ => 0
    };

    public static int GetGemStoneCount(CrystalGrade itemGrade, int lifeStoneGrade) =>
        lifeStoneGrade == GradeAcc ? GemCountAccessory(itemGrade) : GemCountWeapon(itemGrade);

    static int GemCountWeapon(CrystalGrade grade) => grade switch
    {
        CrystalGrade.C => 20,
        CrystalGrade.B => 30,
        CrystalGrade.A => 20,
        CrystalGrade.S => 25,
        _ => 0
    };

    static int GemCountAccessory(CrystalGrade grade) => grade switch
    {
        CrystalGrade.C => 200,
        CrystalGrade.B => 300,
        CrystalGrade.A => 200,
        CrystalGrade.S => 250,
        _ => 0
    };

    static Dictionary<int, LifeStoneRow> BuildStones()
    {
        var map = new Dictionary<int, LifeStoneRow>();
        void AddRange(int baseId, int grade)
        {
            for (int i = 0; i < 10; i++)
            {
                int id = baseId + i;
                int level = i;
                int minLevel = PlayerLevels[Math.Min(level, PlayerLevels.Length - 1)];
                map[id] = new LifeStoneRow(grade, level, minLevel);
            }
        }

        AddRange(8723, GradeNone);
        AddRange(8733, GradeMid);
        AddRange(8743, GradeHigh);
        AddRange(8753, GradeTop);
        return map;
    }
}