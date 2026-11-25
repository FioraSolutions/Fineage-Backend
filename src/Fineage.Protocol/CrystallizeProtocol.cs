namespace Fineage.Protocol;

/// <summary>Inventory crystallize (retail RequestCrystallizeItem) and the system messages it sends.</summary>
public static class CrystallizeProtocol
{
    public const string NotCrystallizable = "crystallize_item";
    public const string SkillTooLow = "crystallize_skill";
    public const string StoreOpen = "crystallize_store";
    public const string MissingCrystal = "crystallize_crystal";

    public const int Earned = 53;
    public const int SkillLow = 562;
    public const int StoreBlocked = 1065;
    public const int Crystallized = 1258;
}
