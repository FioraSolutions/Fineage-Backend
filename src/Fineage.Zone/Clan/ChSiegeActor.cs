using Fineage.Zone.Simulation;

namespace Fineage.Zone.Clan;

public abstract record ChSiegeTransition(int HallId);

public sealed record ChCloseRegistrationTransition(int HallId, string Name) : ChSiegeTransition(HallId);

public sealed record ChSiegeStartTransition(int HallId, ChSiegeStartKind Kind, string Name) : ChSiegeTransition(HallId);

public sealed record ChSiegeEndTransition(int HallId, int WinnerClanId, string Name, bool Won) : ChSiegeTransition(HallId);

/// <summary>Mobius <c>CHSiegeManager</c> schedule on the world calendar.</summary>
public sealed class ChSiegeActor(ChSiegeBoard board, ChSiegeSettings settings) : GlobalActor<ChSiegeTransition>(StageName, ActorTime.Calendar)
{
    public const string StageName = "clan.ch-sieges";

    readonly ChSiegeBoard _board = board;
    readonly ChSiegeSettings _settings = settings;

    public ChSiegeBoard Board
    {
        get
        {
            RequireActorThread();
            return _board;
        }
    }

    protected override void OnStep(long nowUnixMs, List<ChSiegeTransition> effects)
    {
        foreach (var hall in _board.Halls)
        {
            if (!hall.InProgress)
            {
                if (!hall.RegistrationClosed
                    && hall.NextSiegeUnixMs > 0
                    && nowUnixMs >= hall.NextSiegeUnixMs - _settings.RegistrationCloseBeforeMs)
                {
                    if (_board.CloseRegistration(hall.Id, nowUnixMs))
                        effects.Add(new ChCloseRegistrationTransition(hall.Id, hall.Name));
                }

                if (hall.NextSiegeUnixMs > 0 && nowUnixMs >= hall.NextSiegeUnixMs)
                {
                    var kind = _board.StartSiege(hall.Id, nowUnixMs);
                    effects.Add(new ChSiegeStartTransition(hall.Id, kind, hall.Name));
                }
            }
            else if (hall.EndsUnixMs > 0 && nowUnixMs >= hall.EndsUnixMs)
            {
                if (_board.EndSiege(hall.Id, nowUnixMs, out int winner))
                {
                    effects.Add(new ChSiegeEndTransition(hall.Id, winner, hall.Name, winner > 0));
                }
            }
        }
    }
}