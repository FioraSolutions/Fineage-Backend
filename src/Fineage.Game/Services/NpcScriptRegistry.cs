using System.Text.Json;

namespace Fineage.Game.Services;

public sealed class NpcScriptRegistry
{
    public const string FileName = "npc-scripts.json";
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
    readonly Dictionary<string, NpcScriptEntry> _byId;
    NpcScriptRegistry(Dictionary<string, NpcScriptEntry> byId) => _byId = byId;
    public bool Knows(string scriptId) => !string.IsNullOrWhiteSpace(scriptId) && _byId.ContainsKey(scriptId.Trim());
    public bool TryGet(string scriptId, out NpcScriptEntry entry)
    {
        entry = default!;
        if (string.IsNullOrWhiteSpace(scriptId)) return false;
        return _byId.TryGetValue(scriptId.Trim(), out entry);
    }
    public static NpcScriptRegistry LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName) ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }
    public static NpcScriptRegistry Parse(string json, string source)
    {
        var file = JsonSerializer.Deserialize<NpcScriptFile>(json, JsonOptions) ?? throw new InvalidDataException($"{source} was empty.");
        var map = new Dictionary<string, NpcScriptEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in file.Scripts ?? [])
        {
            map[row.Id!.Trim()] = new NpcScriptEntry(row.Id.Trim(), row.Pipeline!.Trim(), row.Status ?? "implemented", row.MobiusRef ?? "");
        }
        return new NpcScriptRegistry(map);
    }
}
public readonly record struct NpcScriptEntry(string Id, string Pipeline, string Status, string MobiusRef);
sealed class NpcScriptFile { public List<NpcScriptRow>? Scripts { get; set; } }
sealed class NpcScriptRow { public string? Id { get; set; } public string? Pipeline { get; set; } public string? Status { get; set; } public string? MobiusRef { get; set; } }
