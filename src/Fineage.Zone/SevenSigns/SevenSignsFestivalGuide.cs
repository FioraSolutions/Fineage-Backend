using Fineage.Protocol;

namespace Fineage.Zone.SevenSigns;

/// <summary>Mobius <c>FestivalGuide</c> npc id → tier, cabal, seal stone signup costs.</summary>
public readonly record struct FestivalGuideProfile(
    int TierId,
    SevenSignsCabal Cabal,
    int BlueStones,
    int GreenStones,
    int RedStones);

public static class SevenSignsFestivalGuide
{
    public const int MinPartySize = 5;

    static readonly Dictionary<int, FestivalGuideProfile> ByNpcId = Build();

    public static bool TryProfile(int npcId, out FestivalGuideProfile profile) => ByNpcId.TryGetValue(npcId, out profile);

    public static bool IsGuide(int npcId) => ByNpcId.ContainsKey(npcId);

    public static bool IsWitch(int npcId) => npcId is >= 31132 and <= 31136 or >= 31142 and <= 31146;

    public static int StoneCost(in FestivalGuideProfile profile, int stoneItemId) => stoneItemId switch
    {
        SevenSignsProtocol.BlueSealStoneId => profile.BlueStones,
        SevenSignsProtocol.GreenSealStoneId => profile.GreenStones,
        SevenSignsProtocol.RedSealStoneId => profile.RedStones,
        _ => -1
    };

    public static int MaxLevelForTier(int tierId) => tierId switch
    {
        0 => 31,
        1 => 42,
        2 => 53,
        3 => 64,
        4 => int.MaxValue,
        _ => 0
    };

    public static string FestivalName(int tierId) => tierId switch
    {
        0 => "31",
        1 => "42",
        2 => "53",
        3 => "64",
        _ => "No Level Limit"
    };

    static Dictionary<int, FestivalGuideProfile> Build()
    {
        var d = new Dictionary<int, FestivalGuideProfile>();
        void Dawn(int tier, int npc, int b, int g, int r) =>
            d[npc] = new FestivalGuideProfile(tier, SevenSignsCabal.Dawn, b, g, r);
        void Dusk(int tier, int npc, int b, int g, int r) =>
            d[npc] = new FestivalGuideProfile(tier, SevenSignsCabal.Dusk, b, g, r);

        Dawn(0, 31127, 900, 540, 270);
        Dawn(1, 31128, 1500, 900, 450);
        Dawn(2, 31129, 3000, 1800, 900);
        Dawn(3, 31130, 4500, 2700, 1350);
        Dawn(4, 31131, 6000, 3600, 1800);
        Dusk(0, 31137, 900, 540, 270);
        Dusk(1, 31138, 1500, 900, 450);
        Dusk(2, 31139, 3000, 1800, 900);
        Dusk(3, 31140, 4500, 2700, 1350);
        Dusk(4, 31141, 6000, 3600, 1800);
        // In-arena witches share tier ids with dawn/dusk festival instances.
        Dawn(0, 31132, 0, 0, 0);
        Dawn(1, 31133, 0, 0, 0);
        Dawn(2, 31134, 0, 0, 0);
        Dawn(3, 31135, 0, 0, 0);
        Dawn(4, 31136, 0, 0, 0);
        Dusk(0, 31142, 0, 0, 0);
        Dusk(1, 31143, 0, 0, 0);
        Dusk(2, 31144, 0, 0, 0);
        Dusk(3, 31145, 0, 0, 0);
        Dusk(4, 31146, 0, 0, 0);
        return d;
    }
}