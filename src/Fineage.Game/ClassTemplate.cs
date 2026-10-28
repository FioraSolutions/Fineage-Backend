namespace Fineage.Game;

/// <summary>One sex's collision cylinder from the class template.</summary>
public readonly record struct CollisionBox(double Radius, double Height);

/// <summary>A world point. Creation uses the first spawn; the zone picks among them until a position is saved.</summary>
public readonly record struct MapPoint(int X, int Y, int Z);

/// <summary>
/// One Interlude class. HP/MP/CP arrays are the Mobius level-up table (index 0 is level 1).
/// <see cref="MaxHp"/>, <see cref="MaxMp"/>, and <see cref="MaxCp"/> are the level-1 player-facing
/// maxima: table value times the CON or MEN bonus, truncated toward zero.
/// </summary>
public sealed class ClassTemplate
{
    readonly double[] _hp;
    readonly double[] _mp;
    readonly double[] _cp;
    double _conBonus = 1;
    double _menBonus = 1;

    internal ClassTemplate(
        int classId,
        string name,
        int parentClassId,
        int race,
        int str,
        int dex,
        int con,
        int intel,
        int wit,
        int men,
        int pAtk,
        int mAtk,
        int pDef,
        int mDef,
        int accuracy,
        int critRate,
        int pAtkSpd,
        int mAtkSpd,
        int runSpeed,
        int walkSpeed,
        CollisionBox collisionMale,
        CollisionBox collisionFemale,
        IReadOnlyList<MapPoint> spawns,
        double[] hp,
        double[] mp,
        double[] cp)
    {
        ClassId = classId;
        Name = name;
        ParentClassId = parentClassId;
        Race = race;
        Str = str;
        Dex = dex;
        Con = con;
        Int = intel;
        Wit = wit;
        Men = men;
        PAtk = pAtk;
        MAtk = mAtk;
        PDef = pDef;
        MDef = mDef;
        Accuracy = accuracy;
        CritRate = critRate;
        PAtkSpd = pAtkSpd;
        MAtkSpd = mAtkSpd;
        RunSpeed = runSpeed;
        WalkSpeed = walkSpeed;
        CollisionMale = collisionMale;
        CollisionFemale = collisionFemale;
        Spawns = spawns;
        _hp = hp;
        _mp = mp;
        _cp = cp;
    }

    public int ClassId { get; }

    public string Name { get; }

    /// <summary>-1 when this is a root class.</summary>
    public int ParentClassId { get; }

    public int Race { get; }

    /// <summary>0 base, 1 first profession, 2 second, 3 third.</summary>
    public int ClassLevel { get; private set; }

    public bool IsBase => ClassLevel == 0;

    public int Str { get; }

    public int Dex { get; }

    public int Con { get; }

    public int Int { get; }

    public int Wit { get; }

    public int Men { get; }

    public int PAtk { get; }

    public int MAtk { get; }

    public int PDef { get; }

    public int MDef { get; }

    /// <summary>Level-1 physical accuracy: sqrt(DEX) * 6 + 1, truncated, no weapon bonus.</summary>
    public int Accuracy { get; }

    public int CritRate { get; }

    public int PAtkSpd { get; }

    public int MAtkSpd { get; }

    public int RunSpeed { get; }

    public int WalkSpeed { get; }

    public CollisionBox CollisionMale { get; }

    public CollisionBox CollisionFemale { get; }

    public IReadOnlyList<MapPoint> Spawns { get; }

    public int LevelCount => _hp.Length;

    /// <summary>Level-1 HP after the CON bonus.</summary>
    public int MaxHp { get; private set; }

    /// <summary>Level-1 MP after the MEN bonus.</summary>
    public int MaxMp { get; private set; }

    /// <summary>Level-1 CP after the CON bonus.</summary>
    public int MaxCp { get; private set; }

    public double HpGain(int level) => Gain(_hp, level);

    public double MpGain(int level) => Gain(_mp, level);

    public double CpGain(int level) => Gain(_cp, level);

    public int ModifiedHp(int level) => Truncate(HpGain(Clamp(level)) * _conBonus);

    public int ModifiedMp(int level) => Truncate(MpGain(Clamp(level)) * _menBonus);

    public int ModifiedCp(int level) => Truncate(CpGain(Clamp(level)) * _conBonus);

    public CollisionBox Collision(int sex) => sex == 1 ? CollisionFemale : CollisionMale;

    internal void Finish(int classLevel, double conBonus, double menBonus)
    {
        ClassLevel = classLevel;
        _conBonus = conBonus;
        _menBonus = menBonus;
        MaxHp = ModifiedHp(1);
        MaxMp = ModifiedMp(1);
        MaxCp = ModifiedCp(1);
    }

    int Clamp(int level) => Math.Clamp(level, 1, _hp.Length);

    static double Gain(double[] table, int level)
    {
        if (level < 1 || level > table.Length)
            throw new ArgumentOutOfRangeException(nameof(level), level, $"Level must be 1..{table.Length}.");
        return table[level - 1];
    }

    static int Truncate(double value) => (int)value;
}
