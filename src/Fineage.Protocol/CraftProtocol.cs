namespace Fineage.Protocol;

/// <summary>Self-craft (#281) + manufacture shop (#283).</summary>
public static class CraftProtocol
{
    /// <summary>Mobius has no hard manufacture slot cap in Config; keep listings bounded for Net.</summary>
    public const int ManufactureSlots = 50;

    public const string Unknown = "craft_unknown";
    public const string NotLearned = "craft_not_learned";
    public const string Skill = "craft_skill";
    public const string Materials = "craft_materials";
    public const string Mp = "craft_mp";
    public const string Busy = "craft_busy";
    public const string Fail = "craft_fail";
    public const string Already = "craft_already";
    public const string Closed = "craft_closed";
    public const string Range = "craft_range";
    public const string Adena = "craft_adena";
    public const string Weight = "craft_weight";
    public const string Slots = "craft_slots";
    public const string Limit = "craft_limit";
}
