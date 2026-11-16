namespace Fineage.Protocol;

/// <summary>
/// Fishing after L2J Mobius CT_0 Interlude (<c>Fishing</c> / <c>Pumping</c> / <c>Reeling</c> effects,
/// <c>Player.startFishing</c>, the <c>Fishing</c> combat, <c>FishShots</c>, <c>Fisherman.showFishSkillList</c>).
/// Rules are reimplemented, not copied.
/// </summary>
public static class FishingProtocol
{
    public const int FishingSkillId = 1312;
    public const int PumpingSkillId = 1313;
    public const int ReelingSkillId = 1314;
    public const int ExpertiseSkillId = 1315;

    /// <summary>Mobius <c>AcquireSkillType.FISHING</c> ordinal.</summary>
    public const int AcquireSkillTypeFishing = 1;

    public const int MinBaitDistance = 90;
    public const int MaxBaitDistance = 250;
    /// <summary>The first bite check runs 10 s after the cast; the line gives up 10 s after the fish's start time.</summary>
    public const int FirstCheckMs = 10_000;
    public const int GiveUpExtraMs = 10_000;
    public const int CombatTickMs = 1000;

    public const int FishShotFirst = 6535;
    public const int FishShotLast = 6540;
    /// <summary>Fishing shot item skills (Mobius items 6535–6540 cast 2181–2186).</summary>
    public const int FishShotSkillFirst = 2181;

    // ---- Net event kinds (FishingEventNetMsg.Kind) ----
    public const int EventStart = 0;
    public const int EventCombat = 1;
    public const int EventHp = 2;
    public const int EventEnd = 3;

    // ---- System messages ----
    public const int CombatBite = 1449;
    public const int CombatTimeUp = 1450;
    public const int CombatEscaped = 1451;
    public const int BaitLost = 1452;
    public const int NoRod = 1453;
    public const int NoLure = 1454;
    public const int UnderWater = 1455;
    public const int CantFishHere = 1457;
    public const int Cancelled = 1458;
    public const int NotEnoughBait = 1459;
    public const int StopFishing = 1460;
    public const int CastLine = 1461;
    public const int PumpOnlyFishing = 1462;
    public const int ReelOnlyFishing = 1463;
    public const int FishResisted = 1464;
    public const int PumpSuccess = 1465;
    public const int PumpFailed = 1466;
    public const int ReelSuccess = 1467;
    public const int ReelFailed = 1468;
    public const int CaughtSomething = 1469;
    public const int CannotWhileFishingDrop = 1470;
    public const int CannotWhileFishingUse = 1471;
    public const int ShotGradeMismatch = 1479;
    public const int CannotFishInStore = 1638;
    public const int CaughtMonster = 1655;
    public const int NothingInside = 1669;
    public const int ExpertisePenalty = 1670;
    public const int ReelSuccessPenalty = 1671;
    public const int PumpSuccessPenalty = 1672;

    // ---- Refusal codes ----
    public const string NotFishing = "fishing_not_fishing";
    public const string Refused = "fishing_refused";
    public const string Busy = "fishing_busy";

    public static bool IsFishShot(int itemId) => itemId is >= FishShotFirst and <= FishShotLast;

    public static int FishShotSkill(int itemId) => FishShotSkillFirst + (itemId - FishShotFirst);
}
