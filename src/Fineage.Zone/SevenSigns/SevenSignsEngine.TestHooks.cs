namespace Fineage.Zone.SevenSigns;

public sealed partial class SevenSignsEngine
{
    /// <summary>Test-only period override (mercenary tickets require seal validation).</summary>
    internal void ForcePeriodForTests(SevenSignsPeriod period)
    {
        lock (_gate)
            _period = period;
    }
}