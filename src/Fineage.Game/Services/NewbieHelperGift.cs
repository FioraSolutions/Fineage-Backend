namespace Fineage.Game.Services;

/// <summary>
/// Mobius <c>ai.others.NewbieHelper</c> first-talk gift (CT0 Interlude).
/// Guides 30598–30602 grant beginner shots, travel tokens, and Scroll: Recovery
/// once while the character is under level 18. Tutorial Ex==4 is skipped here —
/// Fineage does not run Q00255 yet; the one-time journal flag matches Mobius qs1.
/// </summary>
public static class NewbieHelperGift
{
    public const int QuestId = 256;
    public const string QuestName = "Newbie Helper Gift";
    public const int MaxLevelExclusive = 18;

    public const int SoulshotNovice = 5789;
    public const int SpiritshotNovice = 5790;
    public const int TravelToken = 8542;
    public const int ScrollRecovery = 8594;

    public const int SoulshotCount = 200;
    public const int SpiritshotCount = 100;
    public const int TokenCount = 12;
    public const int ScrollCount = 2;

    /// <summary>Mobius <c>addStartNpc</c> / <c>addFirstTalkId</c> for Newbie Guides.</summary>
    public static readonly int[] GuideNpcIds = [30598, 30599, 30600, 30601, 30602];

    public static bool IsGuide(int npcId)
    {
        foreach (int id in GuideNpcIds)
        {
            if (id == npcId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Shot id and count for this class. Orc mystic (49) uses soulshots in Mobius
    /// because it is not a <c>BEGINNER_MAGE</c> for this gift.
    /// </summary>
    public static (int ItemId, int Count) ShotForClass(NewbieBuffCatalog buffs, int classId)
    {
        ArgumentNullException.ThrowIfNull(buffs);
        if (buffs.IsMage(classId))
            return (SpiritshotNovice, SpiritshotCount);
        return (SoulshotNovice, SoulshotCount);
    }
}
