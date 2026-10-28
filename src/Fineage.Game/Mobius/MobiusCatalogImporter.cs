using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fineage.Game.Mobius;

public sealed class MobiusImportOptions
{
    public string XmlRoot { get; init; } = "";
    public string OutputDirectory { get; init; } = "data";
    public bool WriteNpcs { get; init; }
    public bool WriteItems { get; init; }
    public bool WriteSpawns { get; init; }
    public bool WriteSkills { get; init; }
    public IReadOnlyList<int> ItemIds { get; init; } = [];
    public IReadOnlyList<int> NpcIds { get; init; } = [];
    public IReadOnlyList<string> SpawnRelativePaths { get; init; } = [];
}

public static class MobiusCatalogImporter
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static void LogRootStatus(TextWriter output)
    {
        if (!MobiusXmlRoot.TryResolve(null, out var root, out var error))
        {
            output.WriteLine("[MOBIUS] " + error);
            return;
        }

        int itemFiles = CountXml(MobiusDataPaths.ItemsDir(root));
        int npcFiles = CountXml(MobiusDataPaths.NpcsDir(root));
        int skillFiles = CountXml(MobiusDataPaths.SkillsDir(root));
        output.WriteLine($"[MOBIUS] xml root={root} itemFiles={itemFiles} npcFiles={npcFiles} skillFiles={skillFiles}");
    }

    public static MobiusImportResult Run(MobiusImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.XmlRoot) || !Directory.Exists(options.XmlRoot))
            throw new DirectoryNotFoundException("Mobius XML root is missing: " + options.XmlRoot);

        Directory.CreateDirectory(options.OutputDirectory);
        var written = new List<string>();

        if (options.WriteNpcs)
        {
            var path = WriteNpcs(options);
            written.Add(path);
        }

        if (options.WriteItems)
        {
            var path = WriteItems(options);
            written.Add(path);
        }

        if (options.WriteSpawns)
        {
            written.AddRange(WriteSpawns(options));
        }

        if (options.WriteSkills)
        {
            var path = WriteSkillsStub(options);
            written.Add(path);
        }

        return new MobiusImportResult(written);
    }

    public static MobiusImportResult RunFromEnvironment(string outputDirectory, MobiusImportTargets targets)
    {
        var root = MobiusXmlRoot.Resolve();
        return Run(new MobiusImportOptions
        {
            XmlRoot = root,
            OutputDirectory = outputDirectory,
            WriteNpcs = targets.HasFlag(MobiusImportTargets.Npcs),
            WriteItems = targets.HasFlag(MobiusImportTargets.Items),
            WriteSpawns = targets.HasFlag(MobiusImportTargets.Spawns),
            WriteSkills = targets.HasFlag(MobiusImportTargets.Skills),
            NpcIds = targets.HasFlag(MobiusImportTargets.Npcs) ? [20001] : [],
            ItemIds = targets.HasFlag(MobiusImportTargets.Items) ? [57, 2369] : [],
            SpawnRelativePaths = targets.HasFlag(MobiusImportTargets.Spawns)
                ? ["TalkingIsland/TalkingIslandMonsters.xml"]
                : []
        });
    }

    static string WriteNpcs(MobiusImportOptions options)
    {
        var npcsDir = MobiusDataPaths.NpcsDir(options.XmlRoot);
        var ids = options.NpcIds.Count > 0 ? options.NpcIds : [20001];
        var rows = new List<object>();
        foreach (int id in ids)
        {
            var row = MobiusNpcXmlReader.TryReadById(npcsDir, id)
                ?? throw new InvalidDataException($"NPC {id} not found under {npcsDir}");
            rows.Add(new
            {
                id = row.Id,
                name = row.Name,
                level = row.Level,
                mobiusType = row.Type,
                race = row.Race?.ToLowerInvariant(),
                maxHp = row.MaxHp,
                walkSpeed = row.WalkSpeed,
                runSpeed = row.RunSpeed,
                attackable = row.Attackable
            });
        }

        var document = new
        {
            reference = "Converted from Mobius CT0 Interlude stats/npcs XML via Fineage MobiusCatalogImporter. Copy or convert from XML before inventing stats.",
            npcs = rows
        };

        var path = Path.Combine(options.OutputDirectory, MobiusDataPaths.FineageNpcsJson);
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine);
        return path;
    }

    static string WriteItems(MobiusImportOptions options)
    {
        var itemsDir = MobiusDataPaths.ItemsDir(options.XmlRoot);
        var ids = options.ItemIds.Count > 0 ? options.ItemIds : [57];
        var imported = new List<object>();
        foreach (int id in ids)
        {
            var row = MobiusItemXmlReader.TryReadById(itemsDir, id)
                ?? throw new InvalidDataException($"Item {id} not found under {itemsDir}");
            imported.Add(ToFineageItem(row));
        }

        string path = Path.Combine(options.OutputDirectory, MobiusDataPaths.FineageItemsJson);
        var merged = MergeItems(path, imported);

        var document = new
        {
            reference = "Fineage item templates merged from Mobius stats/items XML. equip and extended stats remain hand-tuned where Mobius XML is incomplete for Fineage.",
            items = merged
        };
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine);
        return path;
    }

    static List<Dictionary<string, object?>> MergeItems(string existingPath, IReadOnlyList<object> imported)
    {
        var byId = new Dictionary<int, Dictionary<string, object?>>();
        if (File.Exists(existingPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(existingPath));
            if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int id))
                        byId[id] = JsonElementToDictionary(item);
                }
            }
        }

        foreach (var row in imported)
        {
            var json = JsonSerializer.SerializeToElement(row, JsonOptions);
            if (json.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int id))
                byId[id] = JsonElementToDictionary(json);
        }

        return byId.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
    }

    static Dictionary<string, object?> JsonElementToDictionary(JsonElement element)
    {
        var map = new Dictionary<string, object?>();
        foreach (var prop in element.EnumerateObject())
            map[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt32(out var i) ? i : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Object => JsonElementToDictionary(prop.Value),
                JsonValueKind.Array => prop.Value.EnumerateArray().Select(e => JsonElementToDictionary(e)).ToList(),
                _ => null
            };
        return map;
    }

    static object ToFineageItem(MobiusItemRow row)
    {
        var map = new Dictionary<string, object?>
        {
            ["id"] = row.Id,
            ["name"] = row.Name,
            ["kind"] = row.Kind,
            ["icon"] = row.Icon
        };
        if (row.BodySlot != null)
            map["bodySlot"] = row.BodySlot;
        if (row.WeaponType != null)
            map["weaponType"] = row.WeaponType;
        if (row.ArmorType != null)
            map["armorType"] = row.ArmorType;
        if (row.Stackable)
            map["stackable"] = true;
        if (row.Magic)
            map["magic"] = true;
        if (row.PDef > 0)
            map["pDef"] = row.PDef;
        return map;
    }

    static IReadOnlyList<string> WriteSpawns(MobiusImportOptions options)
    {
        var spawnRoot = MobiusDataPaths.SpawnsDir(options.XmlRoot);
        var paths = new List<string>();
        var relativePaths = options.SpawnRelativePaths.Count > 0
            ? options.SpawnRelativePaths
            : ["TalkingIsland/TalkingIslandMonsters.xml"];

        foreach (string relative in relativePaths)
        {
            string xmlPath = Path.Combine(spawnRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(xmlPath))
                throw new FileNotFoundException("Spawn XML not found", xmlPath);

            var territories = MobiusSpawnXmlReader.ReadFile(xmlPath);
            var document = new
            {
                reference = $"Converted from Mobius spawns/{relative}. Territory spawns; fixed coordinates are a later slice.",
                source = relative.Replace('\\', '/'),
                territories = territories.Select(t => new
                {
                    zone = t.ZoneName,
                    minZ = t.MinZ,
                    maxZ = t.MaxZ,
                    nodes = t.Nodes.Select(n => new { x = n.X, y = n.Y }),
                    npcs = t.Npcs.Select(n => new
                    {
                        npcId = n.NpcId,
                        count = n.Count,
                        respawnDelaySeconds = n.RespawnDelaySeconds,
                        respawnRandomSeconds = n.RespawnRandomSeconds
                    })
                })
            };

            string outDir = Path.Combine(options.OutputDirectory, MobiusDataPaths.FineageSpawnsDir);
            Directory.CreateDirectory(outDir);
            string fileName = Path.GetFileNameWithoutExtension(relative).ToLowerInvariant() + ".json";
            string outPath = Path.Combine(outDir, fileName);
            File.WriteAllText(outPath, JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine);
            paths.Add(outPath);
        }

        return paths;
    }

    static string WriteSkillsStub(MobiusImportOptions options)
    {
        var skillsDir = MobiusDataPaths.SkillsDir(options.XmlRoot);
        var sampleFiles = Directory.Exists(skillsDir)
            ? Directory.GetFiles(skillsDir, "*.xml", SearchOption.TopDirectoryOnly).Take(1).ToList()
            : [];

        var skills = sampleFiles.Count == 0
            ? Array.Empty<object>()
            : MobiusSkillXmlReader.ReadFile(sampleFiles[0]).Take(3).Select(s => (object)new
            {
                id = s.Id,
                name = s.Name,
                level = s.Level
            }).ToArray();

        var document = new
        {
            reference = "Stub header from Mobius stats/skills; expand with MobiusCatalogImporter once combat skills are wired.",
            sampleSource = sampleFiles.Count > 0 ? Path.GetFileName(sampleFiles[0]) : null,
            skills
        };

        var path = Path.Combine(options.OutputDirectory, MobiusDataPaths.FineageSkillsJson);
        File.WriteAllText(path, JsonSerializer.Serialize(document, JsonOptions) + Environment.NewLine);
        return path;
    }

    static int CountXml(string dir) =>
        Directory.Exists(dir) ? Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories).Length : 0;
}

[Flags]
public enum MobiusImportTargets
{
    None = 0,
    Npcs = 1,
    Items = 2,
    Spawns = 4,
    Skills = 8
}

public sealed record MobiusImportResult(IReadOnlyList<string> WrittenPaths);
