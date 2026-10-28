namespace Fineage.Game;

public sealed record StarterKitEntry(int ItemId, int Count, bool Equipped);

public sealed class StarterKit
{
    public StarterKit(int classId, IReadOnlyList<StarterKitEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ClassId = classId;
        Entries = entries;
    }

    public int ClassId { get; }

    public IReadOnlyList<StarterKitEntry> Entries { get; }

    public bool IsEmpty => Entries.Count == 0;
}

/// <summary>Starter equipment keyed by class id. An unknown class has an empty kit.</summary>
public sealed class StarterKits
{
    readonly Dictionary<int, StarterKit> _byClass;

    public StarterKits(IEnumerable<StarterKit> kits)
    {
        ArgumentNullException.ThrowIfNull(kits);
        var list = new List<StarterKit>();
        var map = new Dictionary<int, StarterKit>();
        foreach (var kit in kits)
        {
            ArgumentNullException.ThrowIfNull(kit);
            if (!map.TryAdd(kit.ClassId, kit))
                throw new ArgumentException($"Duplicate starter kit for class {kit.ClassId}.", nameof(kits));
            list.Add(kit);
        }

        _byClass = map;
        All = list;
    }

    public IReadOnlyList<StarterKit> All { get; }

    public StarterKit ForClass(int classId) =>
        _byClass.TryGetValue(classId, out var kit) ? kit : new StarterKit(classId, []);
}
