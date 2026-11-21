using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>Mobius wyvern mount display npc and castle wyvern-manager costs (not collar pets).</summary>
public sealed record MountRules(
    int WyvernDisplayNpcId,
    int WyvernCrystalItemId,
    int WyvernCrystalCount,
    int WyvernMinStriderLevel,
    int WyvernSkillId)
{
    public const string FileName = "mounts.json";

    public static MountRules LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName) ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static MountRules Parse(string json, string source = FileName)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int wyvernNpc = root.GetProperty("wyvernDisplayNpcId").GetInt32();
        var mgr = root.GetProperty("wyvernManager");
        int crystal = mgr.GetProperty("crystalItemId").GetInt32();
        int count = mgr.GetProperty("crystalCount").GetInt32();
        int minLv = mgr.GetProperty("minStriderLevel").GetInt32();
        int skillId = root.TryGetProperty("wyvernSkillId", out var sk) ? sk.GetInt32() : 0;
        if (wyvernNpc <= 0 || crystal <= 0 || count <= 0 || minLv < 1 || skillId <= 0)
            throw new InvalidDataException($"{source} has invalid wyvern manager rules.");
        return new MountRules(wyvernNpc, crystal, count, minLv, skillId);
    }
}