namespace Fineage.Protocol;

public sealed class VariationResultMsg
{
    public int Option1 { get; set; }
    public int Option2 { get; set; }
    public int Success { get; set; }
}

public sealed class VariationMakeWindowMsg
{
}

public sealed class VariationCancelWindowMsg
{
}

public sealed class VariationPutItemMsg
{
    public int TargetObjectId { get; set; }
    public int ItemId { get; set; }
    public int Result { get; set; } = 1;
}

public sealed class VariationPutRefinerMsg
{
    public int RefinerObjectId { get; set; }
    public int ItemId { get; set; }
    public int RefinerId { get; set; }
    public int Result { get; set; } = 1;
}

public sealed class VariationPutGemMsg
{
    public int GemstoneObjectId { get; set; }
    public int RefinerItemId { get; set; }
    public long GemstoneCount { get; set; }
    public int Result { get; set; } = 1;
}

public sealed class VariationCancelItemMsg
{
    public int TargetObjectId { get; set; }
    public int ItemId { get; set; }
    public int Result { get; set; }
}

public sealed class VariationCancelResultMsg
{
    public int RefundAdena { get; set; }
}

public sealed class AugmentConfirmTargetMsg
{
    public int TargetObjectId { get; set; }
}

public sealed class AugmentConfirmRefinerMsg
{
    public int TargetObjectId { get; set; }
    public int RefinerObjectId { get; set; }
}

public sealed class AugmentConfirmGemMsg
{
    public int TargetObjectId { get; set; }
    public int RefinerObjectId { get; set; }
    public int GemstoneObjectId { get; set; }
    public long GemstoneCount { get; set; }
}

public sealed class AugmentRefineMsg
{
    public int TargetObjectId { get; set; }
    public int RefinerObjectId { get; set; }
    public int GemstoneObjectId { get; set; }
    public long GemstoneCount { get; set; }
}

public sealed class AugmentRefineCancelMsg
{
    public int TargetObjectId { get; set; }
}

public sealed class AugmentCancelWindowMsg
{
}