namespace Fineage.Protocol;

/// <summary>Mobius <c>ClanHall.FUNC_*</c> ids and lease calendar (Feature.ini).</summary>
public static class HallFunctionProtocol
{
    public const int FuncTeleport = 1;
    public const int FuncItemCreate = 2;
    public const int FuncRestoreHp = 3;
    public const int FuncRestoreMp = 4;
    public const int FuncRestoreExp = 5;
    public const int FuncSupport = 6;
    public const int FuncDecoFront = 7;
    public const int FuncDecoCurtains = 8;

    /// <summary>Auctionable hall rent period (<c>CH_RATE</c> / default Feature.ini).</summary>
    public const long HallLeasePeriodMs = 604800000L;

    public const int ManagerRange = 250;
}