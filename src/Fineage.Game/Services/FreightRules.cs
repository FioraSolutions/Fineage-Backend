namespace Fineage.Game.Services;

/// <summary>
/// Account freight (Mobius <c>Player.ini</c> <c>MaximumFreightSlots</c> / <c>FreightPrice</c>, Interlude defaults 200 / 1000).
/// </summary>
public static class FreightRules
{
    public const int Slots = 200;
    public const int FeePerLine = 1000;

    public static int DepositFee(int lines)
    {
        if (lines < 0)
            throw new ArgumentOutOfRangeException(nameof(lines));
        return checked(lines * FeePerLine);
    }
}