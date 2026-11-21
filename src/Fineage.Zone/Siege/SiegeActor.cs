using Fineage.Zone.Simulation;

namespace Fineage.Zone.Siege;

/// <summary>What the siege calendar did to one castle in a step.</summary>
public abstract record SiegeTransition(int CastleId);

/// <summary>The siege time came: <paramref name="Kind"/> says whether it started; the owner and name are from before.</summary>
public sealed record SiegeStartTransition(int CastleId, SiegeStartKind Kind, int OwnerBefore, string Name) : SiegeTransition(CastleId);

/// <summary>The siege ran out of time; <paramref name="Error"/> is the board's refusal, if any.</summary>
public sealed record SiegeEndTransition(int CastleId, string? Error) : SiegeTransition(CastleId);

/// <summary>
/// The castle siege calendar as a global actor: at the step's wall sample it starts every accepted siege whose time has
/// come and ends every siege whose time is up, and returns those transitions. The battle itself (doors, towers, guards,
/// flags, countdowns) is world state, run by the world after it applied the transitions. Registration, schedules and
/// captures are requests on <see cref="Board"/>.
/// </summary>
public sealed class SiegeActor(SiegeBoard board) : GlobalActor<SiegeTransition>(StageName, ActorTime.Calendar)
{
    public const string StageName = "sieges";

    readonly SiegeBoard _board = board ?? throw new ArgumentNullException(nameof(board));

    public SiegeBoard Board
    {
        get
        {
            RequireActorThread();
            return _board;
        }
    }

    protected override void OnStep(long nowUnixMs, List<SiegeTransition> effects)
    {
        foreach (var castle in _board.Castles)
        {
            if (!castle.InProgress && castle.ScheduleAccepted && castle.SiegeUnixMs > 0 && castle.SiegeUnixMs <= nowUnixMs)
            {
                int owner = castle.OwnerClanId;
                string name = castle.Name;
                effects.Add(new SiegeStartTransition(castle.Id, _board.StartSiege(castle.Id, nowUnixMs), owner, name));
            }
            else if (castle.InProgress && castle.EndsUnixMs > 0 && castle.EndsUnixMs <= nowUnixMs)
            {
                effects.Add(new SiegeEndTransition(castle.Id, _board.EndSiege(castle.Id, nowUnixMs)));
            }
        }
    }
}
