namespace Fineage.Game.Skills;

/// <summary>Per-level row transcribed from Mobius skill XML tables.</summary>
public sealed class SkillLevelData
{
    public SkillLevelData(
        int level,
        int mpConsume,
        int power,
        int abnormalLevel = 1,
        int magicLevel = 0,
        int mpInitialConsume = 0,
        int itemConsumeId = 0,
        int itemConsumeCount = 0)
    {
        Level = level;
        MpConsume = mpConsume;
        Power = power;
        AbnormalLevel = Math.Max(0, abnormalLevel);
        MagicLevel = Math.Max(0, magicLevel);
        MpInitialConsume = Math.Max(0, mpInitialConsume);
        ItemConsumeId = Math.Max(0, itemConsumeId);
        ItemConsumeCount = ItemConsumeId > 0 ? Math.Max(0, itemConsumeCount) : 0;
    }

    public int Level { get; }

    public int MpConsume { get; }

    /// <summary>
    /// Mobius <c>mpInitialConsume</c>: taken at cast start. Toggles that carry only this (Vicious Stance, Shield
    /// Fortress, Soul Cry …) pay it once and then drain MP per tick through their effect.
    /// </summary>
    public int MpInitialConsume { get; }

    public int Power { get; }

    /// <summary>
    /// Mobius <c>itemConsumeId</c>: the item <c>useMagic</c> destroys before the effect lands. The cast is refused
    /// up front with SysMsg 351 when the bag holds fewer than <see cref="ItemConsumeCount"/>.
    /// </summary>
    public int ItemConsumeId { get; }

    /// <summary>Mobius <c>itemConsumeCount</c> for <see cref="ItemConsumeId"/>.</summary>
    public int ItemConsumeCount { get; }

    /// <summary>
    /// Mobius <c>capsuled_items_skill</c> → <c>RestorationRandom</c> roulette for this level. Empty when the skill
    /// does not extract items. Each group keeps Mobius' own order: the roll walks the segments in that order.
    /// </summary>
    public IReadOnlyList<ExtractableGroup> Extractable { get; init; } = [];

    /// <summary>Mobius <c>abnormalLevel</c> for wire <see cref="Fineage.Protocol.AbnormalEffectMsg.Value"/>.</summary>
    public int AbnormalLevel { get; }

    /// <summary>Mobius <c>magicLevel</c> for calcSkillSuccess / Lethal. 0 = unknown.</summary>
    public int MagicLevel { get; }

    /// <summary>
    /// Per-level <c>reuseDelay</c> when the Mobius table varies by rank. Null means use the template
    /// <see cref="SkillDefinition.ReuseDelayMs"/>.
    /// </summary>
    public int? ReuseDelayMs { get; init; }
}

/// <summary>One Mobius <c>ExtractableProductItem</c>: the items one roulette segment hands out and its width.</summary>
public sealed record ExtractableGroup(IReadOnlyList<ExtractableItem> Items, double Chance);

/// <summary>One extracted item line: id and count (Mobius <c>ItemHolder</c>).</summary>
public readonly record struct ExtractableItem(int Id, int Count);
