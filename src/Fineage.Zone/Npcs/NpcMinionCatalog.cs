using System.Text.Json;

namespace Fineage.Zone.Npcs;

/// <summary>One Mobius <c>minions name="Privates"</c> row: template id and how many to spawn.</summary>
public readonly record struct NpcMinionSpawn(int NpcId, int Count);

/// <summary>Loads <c>data/npc-minions.json</c> (Mobius CT0 private minions).</summary>
public sealed class NpcMinionCatalog
{
    public const string FileName = "npc-minions.json";

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    readonly Dictionary<int, NpcMinionSpawn[]> _byMaster;

    NpcMinionCatalog(Dictionary<int, NpcMinionSpawn[]> byMaster) => _byMaster = byMaster;

    public static NpcMinionCatalog Empty { get; } = new([]);

    public bool TryGet(int masterNpcId, out IReadOnlyList<NpcMinionSpawn> spawns)
    {
        if (_byMaster.TryGetValue(masterNpcId, out var rows))
        {
            spawns = rows;
            return true;
        }

        spawns = [];
        return false;
    }

    public static NpcMinionCatalog LoadDefault()
    {
        var directory = Spawns.GameDataPaths.FindDataDirectory(AppContext.BaseDirectory)
            ?? Spawns.GameDataPaths.FindDataDirectory(Directory.GetCurrentDirectory());
        if (directory == null)
            return Empty;
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
            return Empty;
        return Read(path);
    }

    static NpcMinionCatalog Read(string path)
    {
        var file = JsonSerializer.Deserialize<FileDto>(File.ReadAllText(path), JsonOptions);
        var map = new Dictionary<int, NpcMinionSpawn[]>();
        if (file?.Masters == null)
            return new NpcMinionCatalog(map);
        foreach (var (key, rows) in file.Masters)
        {
            if (!int.TryParse(key, out int master) || rows == null)
                continue;
            var spawns = new List<NpcMinionSpawn>();
            foreach (var row in rows)
            {
                if (row.Id <= 0 || row.Count <= 0)
                    continue;
                spawns.Add(new NpcMinionSpawn(row.Id, row.Count));
            }

            if (spawns.Count > 0)
                map[master] = spawns.ToArray();
        }

        return new NpcMinionCatalog(map);
    }

    sealed class FileDto
    {
        public Dictionary<string, List<RowDto>>? Masters { get; set; }
    }

    sealed class RowDto
    {
        public int Id { get; set; }
        public int Count { get; set; }
    }
}
