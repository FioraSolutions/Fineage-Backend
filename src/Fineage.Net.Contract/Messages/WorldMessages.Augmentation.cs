namespace Fineage.Net.Contract;

public sealed class AugmentConfirmTargetNetMsg
{
    public int TargetObjectId { get; set; }
}

public sealed class AugmentConfirmRefinerNetMsg
{
    public int TargetObjectId { get; set; }
    public int RefinerObjectId { get; set; }
}

public sealed class AugmentConfirmGemNetMsg
{
    public int TargetObjectId { get; set; }
    public int RefinerObjectId { get; set; }
    public int GemstoneObjectId { get; set; }
    public long GemstoneCount { get; set; }
}

public sealed class AugmentRefineNetMsg
{
    public int TargetObjectId { get; set; }
    public int RefinerObjectId { get; set; }
    public int GemstoneObjectId { get; set; }
    public long GemstoneCount { get; set; }
}

public sealed class AugmentRefineCancelNetMsg
{
    public int TargetObjectId { get; set; }
}

public sealed class VariationResultNetMsg
{
    public int Option1 { get; set; }
    public int Option2 { get; set; }
    public int Success { get; set; }
}

public sealed class VariationPutItemNetMsg
{
    public int TargetObjectId { get; set; }
    public int ItemId { get; set; }
    public int Result { get; set; } = 1;
}

public sealed class VariationPutRefinerNetMsg
{
    public int RefinerObjectId { get; set; }
    public int ItemId { get; set; }
    public int RefinerId { get; set; }
    public int Result { get; set; } = 1;
}

public sealed class VariationPutGemNetMsg
{
    public int GemstoneObjectId { get; set; }
    public int RefinerItemId { get; set; }
    public long GemstoneCount { get; set; }
    public int Result { get; set; } = 1;
}

public sealed class VariationCancelItemNetMsg
{
    public int TargetObjectId { get; set; }
    public int ItemId { get; set; }
    public int Result { get; set; }
}

public sealed class VariationCancelResultNetMsg
{
    public int RefundAdena { get; set; }
}