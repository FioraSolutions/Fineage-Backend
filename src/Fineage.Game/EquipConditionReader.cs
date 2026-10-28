using System.Text.Json;

namespace Fineage.Game;

/// <summary>
/// Reads an item <c>equip</c> object. Several fields on one object are all required.
/// <c>all</c>, <c>any</c>, and <c>not</c> nest the same object.
/// </summary>
static class EquipConditionReader
{
    public static EquipRequirement? Read(JsonElement element, string path, int itemId)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{path} item {itemId} equip must be an object.");

        var parts = new List<EquipRequirement>();
        foreach (var property in element.EnumerateObject())
        {
            var part = ReadProperty(property, path, itemId);
            if (part != null)
                parts.Add(part);
        }

        if (parts.Count == 0)
            return null;
        if (parts.Count == 1)
            return parts[0];
        return new EquipRequirement.AllOf(parts);
    }

    static EquipRequirement? ReadProperty(JsonProperty property, string path, int itemId)
    {
        var value = property.Value;
        switch (property.Name)
        {
            case "races":
                return new EquipRequirement.Races(ReadIds(value, path, itemId, property.Name, ParseRace));
            case "classes":
                return new EquipRequirement.Classes(ReadInts(value, path, itemId, property.Name));
            case "sex":
                return new EquipRequirement.SexIs(ParseSex(value, path, itemId));
            case "hero":
                return new EquipRequirement.HeroIs(ReadBool(value, path, itemId, property.Name));
            case "clanLeader":
                return new EquipRequirement.ClanLeaderIs(ReadBool(value, path, itemId, property.Name));
            case "pvp":
                return new EquipRequirement.PvpIs(ReadBool(value, path, itemId, property.Name));
            case "minLevel":
                return new EquipRequirement.MinLevel(ReadInt(value, path, itemId, property.Name));
            case "pledgeClass":
                return new EquipRequirement.PledgeAtLeast(ReadInt(value, path, itemId, property.Name));
            case "castle":
                return new EquipRequirement.CastleIs(ReadInt(value, path, itemId, property.Name));
            case "clanHalls":
                return new EquipRequirement.ClanHalls(ReadInts(value, path, itemId, property.Name));
            case "maxPk":
                return new EquipRequirement.PkAtMost(ReadInt(value, path, itemId, property.Name));
            case "expertise":
                return new EquipRequirement.ExpertiseIs(ReadInt(value, path, itemId, property.Name));
            case "all":
                return new EquipRequirement.AllOf(ReadGroup(value, path, itemId, property.Name));
            case "any":
                return new EquipRequirement.AnyOf(ReadGroup(value, path, itemId, property.Name));
            case "not":
            {
                var inner = Read(value, path, itemId);
                if (inner == null)
                    throw new InvalidDataException($"{path} item {itemId} equip.not is empty.");
                return new EquipRequirement.Negate(inner);
            }
            default:
                throw new InvalidDataException($"{path} item {itemId} has unknown equip field '{property.Name}'.");
        }
    }

    static IReadOnlyList<EquipRequirement> ReadGroup(JsonElement value, string path, int itemId, string field)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            throw new InvalidDataException($"{path} item {itemId} equip.{field} must be a non-empty array.");

        var parts = new List<EquipRequirement>();
        foreach (var entry in value.EnumerateArray())
        {
            var part = Read(entry, path, itemId);
            if (part == null)
                throw new InvalidDataException($"{path} item {itemId} equip.{field} has an empty object.");
            parts.Add(part);
        }

        return parts;
    }

    static IReadOnlyList<int> ReadIds(JsonElement value, string path, int itemId, string field, Func<string, int?> parseNamed)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            throw new InvalidDataException($"{path} item {itemId} equip.{field} must be a non-empty array.");

        var ids = new List<int>();
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Number)
            {
                ids.Add(entry.GetInt32());
                continue;
            }

            if (entry.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"{path} item {itemId} equip.{field} has a value that is not an id.");
            int? parsed = parseNamed(entry.GetString() ?? "");
            if (parsed == null)
                throw new InvalidDataException($"{path} item {itemId} equip.{field} has unknown value '{entry.GetString()}'.");
            ids.Add(parsed.Value);
        }

        return ids;
    }

    static IReadOnlyList<int> ReadInts(JsonElement value, string path, int itemId, string field)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            throw new InvalidDataException($"{path} item {itemId} equip.{field} must be a non-empty array.");

        var ids = new List<int>();
        foreach (var entry in value.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Number)
                throw new InvalidDataException($"{path} item {itemId} equip.{field} values must be integers.");
            ids.Add(entry.GetInt32());
        }

        return ids;
    }

    static int? ParseRace(string text) => text.Trim().ToLowerInvariant() switch
    {
        "human" => 0,
        "elf" => 1,
        "dark_elf" or "darkelf" => 2,
        "orc" => 3,
        "dwarf" => 4,
        _ => null
    };

    static int ParseSex(JsonElement value, string path, int itemId)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            int sex = value.GetInt32();
            if (sex is 0 or 1)
                return sex;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            switch ((value.GetString() ?? "").Trim().ToLowerInvariant())
            {
                case "male":
                case "0":
                    return 0;
                case "female":
                case "1":
                    return 1;
            }
        }

        throw new InvalidDataException($"{path} item {itemId} equip.sex must be male or female.");
    }

    static bool ReadBool(JsonElement value, string path, int itemId, string field)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"{path} item {itemId} equip.{field} must be true or false.");
        return value.GetBoolean();
    }

    static int ReadInt(JsonElement value, string path, int itemId, string field)
    {
        if (value.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException($"{path} item {itemId} equip.{field} must be an integer.");
        return value.GetInt32();
    }
}
