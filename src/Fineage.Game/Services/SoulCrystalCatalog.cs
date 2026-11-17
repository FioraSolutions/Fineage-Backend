using System.Text.Json;

namespace Fineage.Game.Services;

/// <summary>Mobius <c>AbsorbCrystalType</c>: who among the party gets a roll when the monster dies.</summary>
public enum SoulAbsorbMode { LastHit, PartyOneRandom, PartyRandom, FullParty }

public sealed record SoulCrystalStep(int ItemId, int Level, int NextItemId);

public sealed record SoulAbsorbRule(int Chance, bool SkillNeeded, SoulAbsorbMode Mode);

/// <summary>
/// Soul crystal levelling for quest 350 Enhance Your Weapon (data/soul-crystals.json, transcribed from Mobius
/// LevelUpCrystalData): the crystal ladder and, per monster, which crystal levels it raises and how.
/// </summary>
public sealed class SoulCrystalCatalog
{
    public const string FileName = "soul-crystals.json";
    public const int SoulCrystalSkillId = 2096;
    public const int QuestId = 350;

    readonly Dictionary<int, SoulCrystalStep> _crystals;
    readonly Dictionary<int, Dictionary<int, SoulAbsorbRule>> _npcs;

    SoulCrystalCatalog(Dictionary<int, SoulCrystalStep> crystals, Dictionary<int, Dictionary<int, SoulAbsorbRule>> npcs)
    {
        _crystals = crystals;
        _npcs = npcs;
    }

    public bool IsCrystal(int itemId) => _crystals.ContainsKey(itemId);

    public bool HasRules(int npcId) => _npcs.ContainsKey(npcId);

    public SoulAbsorbRule? Rule(int npcId, int crystalLevel) =>
        _npcs.TryGetValue(npcId, out var rows) && rows.TryGetValue(crystalLevel, out var rule) ? rule : null;

    /// <summary>Mobius <c>isPartyLevelingMonster</c>: some level is levelled by a party mode.</summary>
    public bool IsPartyLeveling(int npcId) =>
        _npcs.TryGetValue(npcId, out var rows) && rows.Values.Any(static r => r.Mode != SoulAbsorbMode.LastHit);

    /// <summary>Mobius <c>getSCForPlayer</c>: the one levelable crystal in the bag; none or two or more → null.</summary>
    public SoulCrystalStep? Single(PlayerInventory inventory)
    {
        SoulCrystalStep? found = null;
        foreach (var item in inventory.Items)
        {
            if (!_crystals.TryGetValue(item.ItemId, out var step))
                continue;
            if (found != null || item.Count > 1)
                return null;
            found = step;
        }

        return found;
    }

    public static SoulCrystalCatalog LoadDefault()
    {
        var path = TeleportCatalog.FindFile(FileName) ?? throw new FileNotFoundException($"Could not find data/{FileName}.");
        return Parse(File.ReadAllText(path), path);
    }

    public static SoulCrystalCatalog Parse(string json, string source = FileName)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"{source} is empty.");
        var crystals = new Dictionary<int, SoulCrystalStep>();
        foreach (var c in dto.Crystals ?? [])
        {
            if (c.ItemId <= 0 || c.Next <= 0 || c.Level is < 0 or > 12)
                throw new InvalidDataException($"{source}: bad crystal row {c.ItemId}.");
            crystals[c.ItemId] = new SoulCrystalStep(c.ItemId, c.Level, c.Next);
        }

        var npcs = new Dictionary<int, Dictionary<int, SoulAbsorbRule>>();
        foreach (var n in dto.Npcs ?? [])
        {
            if (n.NpcId <= 0 || n.Chance is < 0 or > 100 || n.Levels is not { Count: > 0 })
                throw new InvalidDataException($"{source}: bad monster row {n.NpcId}.");
            var mode = n.Absorb switch
            {
                "LAST_HIT" or null => SoulAbsorbMode.LastHit,
                "PARTY_ONE_RANDOM" => SoulAbsorbMode.PartyOneRandom,
                "PARTY_RANDOM" => SoulAbsorbMode.PartyRandom,
                "FULL_PARTY" => SoulAbsorbMode.FullParty,
                _ => throw new InvalidDataException($"{source}: monster {n.NpcId} absorb '{n.Absorb}' is unknown.")
            };
            if (!npcs.TryGetValue(n.NpcId, out var rows))
                npcs[n.NpcId] = rows = [];
            foreach (int level in n.Levels)
                rows[level] = new SoulAbsorbRule(n.Chance, n.Skill, mode);
        }

        return new SoulCrystalCatalog(crystals, npcs);
    }

    sealed class Dto
    {
        public List<CrystalDto>? Crystals { get; set; }
        public List<NpcDto>? Npcs { get; set; }
    }

    sealed class CrystalDto
    {
        public int ItemId { get; set; }
        public int Level { get; set; }
        public int Next { get; set; }
    }

    sealed class NpcDto
    {
        public int NpcId { get; set; }
        public int Chance { get; set; }
        public bool Skill { get; set; }
        public string? Absorb { get; set; }
        public List<int>? Levels { get; set; }
    }
}
