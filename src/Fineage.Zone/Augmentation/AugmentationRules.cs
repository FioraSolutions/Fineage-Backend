using Fineage.Zone.Parity;

namespace Fineage.Zone.Augmentation;

/// <summary>
/// Item variation (life stone refine). Persistence hook will land on <c>StoredItem</c> when Net wires
/// <c>ExVariationResult</c> and inventory updates carry <c>option1/option2</c>.
/// </summary>
public static class AugmentationRules
{
    public static bool CanRefine => ClientHeavyFeatures.AugmentationEnabled;

    /// <summary>Mobius packs option id into a single 32-bit variation id; decode when persisting.</summary>
    public static (int Option1, int Option2) SplitVariationId(int variationId) =>
        (variationId & 0xFFFF, variationId >> 16);
}