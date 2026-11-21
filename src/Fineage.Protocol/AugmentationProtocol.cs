namespace Fineage.Protocol;

/// <summary>Mobius item variation (life stone refine) zone/Net codes.</summary>
public static class AugmentationProtocol
{
    public const string Disabled = "augment_disabled";
    public const string NotSuitable = "not_suitable";
    public const string AlreadyAugmented = "already_augmented";
    public const string NotAugmented = "not_augmented";
    public const string NoAdena = "no_adena";
    public const string GemCount = "gem_count";
    public const string Store = "augment_store";
    public const string Dead = "augment_dead";
    public const string Busy = "augment_busy";

    public const int VariationSuccess = 1;
    public const int VariationFail = 0;
}