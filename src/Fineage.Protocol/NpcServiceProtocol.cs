namespace Fineage.Protocol;

/// <summary>
/// Gatekeeper, Newbie Helper, and private warehouse.
/// Layout: <c>docs/npc-services-v2-protocol.md</c>.
/// Client requests sit at 105–109. Replies sit at 1105–1109.
/// System message ids on <see cref="MsgType.SystemMessage"/> are 190–209.
/// Shops are reserved at 96–99 / 1095–1098 / 180–189. Olympiad is reserved at 60–64 / 1070–1073 / 160–179.
/// </summary>
public static class NpcServiceProtocol
{
    public const ushort TeleportSelect = 105;
    public const ushort WarehouseDeposit = 106;
    public const ushort WarehouseWithdraw = 107;
    public const ushort SupportMagic = 108;
    public const ushort GiveBlessing = 109;

    public const ushort TeleportList = 1105;
    public const ushort WarehouseDepositList = 1106;
    public const ushort WarehouseWithdrawList = 1107;
    public const ushort NewbieBuffResult = 1108;
    public const ushort BlessingResult = 1109;

    /// <summary>
    /// A destination is on a loaded map when some spawned NPC is within this 2D radius.
    /// Talking Island local spots sit inside it once the village spawn is loaded.
    /// A town square sits inside it once that town's spawn map is enabled.
    /// </summary>
    public const int LoadedZoneRadius = 8000;

    /// <summary>Mobius <c>MaxOffsetOnTeleport</c>. The server default is this value unless <c>FINEAGE_TELEPORT_OFFSET</c> overrides it.</summary>
    public const int DefaultTeleportOffset = 50;

    public const int NotEnoughAdena = 190;
    public const int DestinationUnloaded = 191;
    public const int SiegeBlocked = 192;
    public const int Teleported = 193;
    public const int WarehouseFull = 194;
    public const int InventoryFull = 195;
    public const int WeightLimit = 196;
    public const int WarehouseAdena = 197;
    public const int TransactionComplete = 198;
    public const int BuffLevelLow = 199;
    public const int BuffLevelHigh = 200;
    public const int BuffNotNewbie = 201;
    public const int BuffThirdClass = 202;
    public const int BlessingRefused = 203;
    public const int UnknownTeleport = 204;
    public const int NoblesDeferred = 205;
    public const int CannotStore = 206;
    public const int FreightDeferred = 207;
    public const int BuffNone = 208;
    public const int EnchantOpen = 209;

    public const string NotEnoughAdenaText = "You do not have enough adena.";
    public const string DestinationUnloadedText = "That destination is not on a loaded map.";
    public const string SiegeBlockedText = "You cannot teleport to a village that is in a siege.";
    public const string TeleportedText = "You have been teleported.";
    public const string WarehouseFullText = "Your warehouse is full.";
    public const string InventoryFullText = "Your inventory is full.";
    public const string WeightLimitText = "You have exceeded the weight limit.";
    public const string TransactionCompleteText = "The transaction is complete.";
    public const string BuffLevelLowText = "You are too low level for supplemental magic.";
    public const string BuffLevelHighText = "You are too high level for supplemental magic.";
    public const string BuffNotNewbieText = "Only new characters may receive supplemental magic.";
    public const string BuffThirdClassText = "Only adventurers who have not completed their 3rd class transfer may receive these buffs.";
    public const string BlessingRefusedText = "The Blessing of Protection is only for characters under level 40 who have not completed their second class transfer.";
    public const string UnknownTeleportText = "That teleport is not available.";
    public const string NoblesDeferredText = "Noblesse teleport is not available yet.";
    public const string CannotStoreText = "That item cannot be stored.";
    public const string BuffNoneText = "There is no supplemental magic for your level.";
    public const string EnchantOpenText = "Finish enchanting before using the warehouse.";
    public const string NotWarehouseText = "Only a warehouse keeper can store items.";
    public const string NotEnoughFeeText = "You do not have enough of the teleport fee.";

    public static bool InLoadedZone(int x, int y, IEnumerable<(int X, int Y)> anchors, int radius = LoadedZoneRadius)
    {
        long limit = (long)radius * radius;
        foreach (var (ax, ay) in anchors)
        {
            long dx = ax - (long)x;
            long dy = ay - (long)y;
            if (dx * dx + dy * dy <= limit)
                return true;
        }

        return false;
    }
}

/// <summary>Bypass intents the world applies. Handlers do not touch zone state.</summary>
public static class NpcServiceIntent
{
    public const string TeleportList = "teleport-list";
    public const string Teleport = "teleport";
    public const string Deposit = "deposit";
    public const string Withdraw = "withdraw";
    public const string SupportMagic = "support-magic";
    public const string SupportBlessing = "support-blessing";
    public const string GiveBlessing = "give-blessing";
}
