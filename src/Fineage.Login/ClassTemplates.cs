using Fineage.Game;
using Fineage.Protocol;

namespace Fineage.Login;

/// <summary>
/// Class facts for login and zone call sites. The values come from the datapack
/// (<see cref="ClassCatalog"/>), including level-1 HP/MP after the CON/MEN bonus.
/// </summary>
public static class ClassTemplates
{
    static readonly Lazy<ClassCatalog> LazyCatalog = new(() => GameData.LoadDefault().Classes!);

    public static ClassCatalog Catalog => LazyCatalog.Value;

    public static int HumanFighterHp => Catalog.Get(HumanClasses.Fighter).MaxHp;

    public static int HumanFighterMp => Catalog.Get(HumanClasses.Fighter).MaxMp;

    public static int HumanMysticHp => Catalog.Get(HumanClasses.Mystic).MaxHp;

    public static int HumanMysticMp => Catalog.Get(HumanClasses.Mystic).MaxMp;

    public static bool TryGet(int classId, out ClassTemplate? template) => Catalog.TryGet(classId, out template);

    public static ClassSpawn.Point[] Points(int classId)
    {
        if (!Catalog.TryGet(classId, out var template) || template == null || template.Spawns.Count == 0)
            return [new ClassSpawn.Point(TalkingIslandSpawn.X, TalkingIslandSpawn.Y, TalkingIslandSpawn.Z)];

        var points = new ClassSpawn.Point[template.Spawns.Count];
        for (int i = 0; i < points.Length; i++)
        {
            var point = template.Spawns[i];
            points[i] = new ClassSpawn.Point(point.X, point.Y, point.Z);
        }

        return points;
    }

    public static ClassSpawn.Point Preview(int classId) => Points(classId)[0];

    public static ClassSpawn.Point Pick(int classId, Random random) => ClassSpawn.Pick(Points(classId), random);
}
