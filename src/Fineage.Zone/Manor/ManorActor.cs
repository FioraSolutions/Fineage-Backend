using Fineage.Protocol;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.Manor;

/// <summary>The manor day cycle moved from <paramref name="From"/> to <paramref name="To"/> (<c>ManorProtocol.Mode*</c>).</summary>
public sealed record ManorModeChange(int From, int To);

/// <summary>
/// The manor day cycle after Mobius <c>CastleManorManager.changeMode</c> as a global actor on the calendar: 20:00
/// maintenance, 20:06 editable, 04:30 approved in the configured offset. The step moves the mode when its time has come
/// and returns the change; the world closes or approves every castle's period. Seed sales, crop purchases and the
/// lord's settings are requests on <see cref="Board"/>.
/// </summary>
public sealed class ManorActor(ManorBoard board) : GlobalActor<ManorModeChange>(StageName, ActorTime.Calendar)
{
    public const string StageName = "manor";

    readonly ManorBoard _board = board ?? throw new ArgumentNullException(nameof(board));

    public ManorBoard Board
    {
        get
        {
            RequireActorThread();
            return _board;
        }
    }

    /// <summary>Moves to the next mode now (when due, or the admin hook); null when the board is disabled.</summary>
    public ManorModeChange? Advance(long nowUnixMs)
    {
        RequireActorThread();
        int from = _board.Mode;
        int? to = from switch
        {
            ManorProtocol.ModeApproved => ManorProtocol.ModeMaintenance,
            ManorProtocol.ModeMaintenance => ManorProtocol.ModeModifiable,
            ManorProtocol.ModeModifiable => ManorProtocol.ModeApproved,
            _ => null
        };
        if (to is not { } next)
            return null;

        _board.Mode = next;
        _board.NextChangeMs = ManorRules.NextChange(next, nowUnixMs, _board.Settings.UtcOffsetMinutes);
        return new ManorModeChange(from, next);
    }

    protected override void OnStep(long nowUnixMs, List<ManorModeChange> effects)
    {
        if (_board.Disabled || _board.NextChangeMs <= 0 || nowUnixMs < _board.NextChangeMs)
            return;
        if (Advance(nowUnixMs) is { } change)
            effects.Add(change);
    }
}
