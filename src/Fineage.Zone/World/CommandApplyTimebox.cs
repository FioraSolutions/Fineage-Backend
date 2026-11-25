namespace Fineage.Zone.World;

/// <summary>
/// Wall-clock credit for the command stage. Each step refills <see cref="NominalMs"/>. Unused nominal time
/// is banked (capped at one step) and spent only while a backlog remains. The caller still clamps the grant
/// to the tick deadline.
/// </summary>
internal sealed class CommandApplyTimebox
{
    public CommandApplyTimebox(double nominalMs, double stepMs)
    {
        if (nominalMs <= 0 || stepMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(nominalMs),
                $"command apply timebox needs a positive nominal ({nominalMs}) and step ({stepMs}).");
        NominalMs = nominalMs;
        StepMs = stepMs;
    }

    public double NominalMs { get; }
    public double StepMs { get; }
    public double CreditMs { get; private set; }

    /// <summary>Milliseconds the stage may run this step before the tick deadline clamps it.</summary>
    public double GrantMilliseconds(bool backlog)
    {
        double grant = NominalMs;
        if (backlog && CreditMs > 0)
            grant += CreditMs;
        return Math.Min(StepMs, grant);
    }

    public void Account(double usedMs)
    {
        if (double.IsNaN(usedMs) || double.IsInfinity(usedMs) || usedMs < 0)
            usedMs = 0;
        CreditMs = Math.Min(StepMs, Math.Max(0, CreditMs + NominalMs - usedMs));
    }
}
