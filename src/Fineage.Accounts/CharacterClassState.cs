using System.Text.Json;

namespace Fineage.Accounts;

/// <summary>
/// One class slot that is not the active one (Mobius <c>character_subclasses</c> row plus the per-<c>class_index</c>
/// skills, hennas, and shortcuts). Index 0 is the base class while a subclass is active; 1–3 are subclasses.
/// </summary>
public sealed class StoredClassSlot
{
    public int Index { get; set; }
    public int ClassId { get; set; }
    public int Level { get; set; } = 40;
    public long Exp { get; set; }
    public int Sp { get; set; }
    public List<StoredSkill> Skills { get; set; } = [];
    public int HennaDye1 { get; set; }
    public int HennaDye2 { get; set; }
    public int HennaDye3 { get; set; }
    public List<StoredShortcut> Shortcuts { get; set; } = [];
}

/// <summary>
/// Subclass and noblesse state (Mobius <c>characters.base_class</c>, <c>characters.nobless</c>, active
/// <c>class_index</c>, <c>character_subclasses</c>). The <see cref="CharacterRecord"/> columns hold the active class;
/// <see cref="Slots"/> parks every other class. Null on a record means "base class only, not noble".
/// </summary>
public sealed class CharacterClassState
{
    public int BaseClassId { get; set; }
    public int ClassIndex { get; set; }
    public bool Noble { get; set; }
    public List<StoredClassSlot> Slots { get; set; } = [];

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static CharacterClassState? FromJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<CharacterClassState>(json, Json);

    public CharacterClassState Clone() => FromJson(ToJson())!;
}
