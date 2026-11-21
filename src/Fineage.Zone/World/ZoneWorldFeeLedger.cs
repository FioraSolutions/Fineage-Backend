using Fineage.Game.Economy;

namespace Fineage.Zone.World;

/// <summary>P1-5d: fee sinks post against <see cref="SystemAccount.Fees"/>.</summary>
public sealed partial class ZoneWorld
{
    static readonly LedgerAccount Fees = LedgerAccount.Of(SystemAccount.Fees);
}