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

/// <summary>Starter equipment keyed by class id, from <c>data/starter-kits.json</c>.</summary>
public sealed class StarterKits
{
    readonly Dictionary<int, StarterKit> _byClass;

    public StarterKits(IEnumerable<StarterKit> kits, long mobiusStartingAdena = 0, string sourcePath = "starter-kits.json")
    {
        ArgumentNullException.ThrowIfNull(kits);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (mobiusStartingAdena < 0)
            throw new ArgumentOutOfRangeException(nameof(mobiusStartingAdena));
        MobiusStartingAdena = mobiusStartingAdena;
        SourcePath = sourcePath;
        var list = new List<StarterKit>();
        var map = new Dictionary<int, StarterKit>();
        foreach (var kit in kits)
        {
            ArgumentNullException.ThrowIfNull(kit);
            if (!map.TryAdd(kit.ClassId, kit))
                throw new ArgumentException($"Duplicate starter kit for class {kit.ClassId} in {sourcePath}.", nameof(kits));
            list.Add(kit);
        }

        _byClass = map;
        All = list;
    }

    /// <summary>Mobius CT0 <c>Player.ini StartingAdena</c> when <c>FINEAGE_STARTING_ADENA</c> is unset.</summary>
    public long MobiusStartingAdena { get; }

    /// <summary>Datapack path used in missing-kit exceptions.</summary>
    public string SourcePath { get; }

    public IReadOnlyList<StarterKit> All { get; }

    public bool Contains(int classId) => _byClass.ContainsKey(classId);

    public bool TryForClass(int classId, out StarterKit kit) => _byClass.TryGetValue(classId, out kit!);

    /// <summary>Throws if any required class is missing, naming the class id and datapack path.</summary>
    public void RequireClasses(IEnumerable<int> classIds, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(classIds);
        var source = path ?? SourcePath;
        foreach (var classId in classIds)
        {
            if (!_byClass.ContainsKey(classId))
                throw new InvalidDataException($"{source} has no starter kit for class {classId}.");
        }
    }

    public StarterKit ForClass(int classId)
    {
        if (_byClass.TryGetValue(classId, out var kit))
            return kit;
        throw new InvalidDataException($"Starter kit for class {classId} is missing from {SourcePath}.");
    }
}
