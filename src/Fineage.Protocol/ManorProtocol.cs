namespace Fineage.Protocol;

/// <summary>
/// Castle manor after L2J Mobius CT_0 Interlude (<c>CastleManorManager</c>, <c>Sow</c> / <c>Harvesting</c> effects,
/// the <c>Seed</c> / <c>Harvester</c> item handlers, <c>RequestBuySeed</c>, <c>RequestProcureCropList</c>,
/// <c>RequestSetSeed</c>, <c>RequestSetCrop</c>) and <c>General.ini</c> manor defaults. Rules are reimplemented, not copied.
/// </summary>
public static class ManorProtocol
{
    public const int SowingSkillId = 2097;
    public const int HarvestingSkillId = 2098;
    public const int HarvesterItemId = 5125;

    /// <summary>Mobius <c>AltManorRefreshTime</c> / <c>AltManorRefreshMin</c>: the period closes at 20:00.</summary>
    public const int RefreshHour = 20;
    public const int RefreshMinute = 0;
    /// <summary>Mobius <c>AltManorApproveTime</c> / <c>AltManorApproveMin</c>: next period settings lock at 04:30.</summary>
    public const int ApproveHour = 4;
    public const int ApproveMinute = 30;
    /// <summary>Mobius <c>AltManorMaintenanceMin</c>: 6 minutes of maintenance after the refresh.</summary>
    public const int MaintenanceMinutes = 6;

    /// <summary>Mobius <c>ManorMode</c>.</summary>
    public const int ModeDisabled = 0;
    public const int ModeModifiable = 1;
    public const int ModeMaintenance = 2;
    public const int ModeApproved = 3;

    /// <summary>Crops bought by the castle mature into the owner clan warehouse at 90% (Mobius changeMode APPROVED).</summary>
    public const double MatureShare = 0.9;
    /// <summary>Selling a crop to another castle's procure list costs 5% of the price (RequestProcureCropList fee).</summary>
    public const double ForeignCastleFee = 0.05;

    // System messages (Mobius SystemMessageId).
    public const int ObtainedOne = 30;
    public const int ObtainedMany = 29;
    public const int InvalidTarget = 109;
    public const int NotEnoughAdena = 279;
    public const int AdenaDisappeared = 672;
    public const int SeedNotHere = 872;
    public const int UnderMaintenance = 879;
    public const int ManorUpdated = 884;
    public const int SowSuccess = 889;
    public const int SowFailed = 890;
    public const int NoHarvestRights = 891;
    public const int HarvestFailed = 892;
    public const int HarvestNotSown = 893;
    public const int ManorNotOperating = 935;
    public const int PartyHarvestedMany = 1137;
    public const int PartyHarvestedOne = 1138;
    public const int TradedCrop = 1490;
    public const int FailedCropTrade = 1491;
    public const int TargetNotSeedable = 1516;
    public const int OnlyThisManorSeeds = 1605;

    // Error codes.
    public const string Disabled = "manor_disabled";
    public const string Maintenance = "manor_maintenance";
    public const string Period = "manor_period";
    public const string Castle = "manor_castle";
    public const string Manager = "manor_manager";
    public const string Rights = "manor_rights";
    public const string Product = "manor_product";
    public const string Adena = "manor_adena";
    public const string Inventory = "manor_inventory";
    public const string Target = "manor_target";
}
