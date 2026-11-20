using Fineage.Zone.Simulation;

namespace Fineage.Zone.Olympiad;

/// <summary>
/// The Grand Olympiad as a global actor: periods, matchmaking (drawn on the actor's stream), arena ports and match
/// clocks advance on the tick time. Who is online and where comes from <paramref name="presence"/>, a read-only query
/// the world answers on the scheduler thread. Registrations, results and noblesse are requests on
/// <see cref="Board"/>; every step and request returns an <see cref="OlympiadStep"/> the world applies.
/// </summary>
public sealed class OlympiadActor(OlympiadBoard board, Func<int, OlympiadPresence?> presence, Random random)
    : GlobalActor<OlympiadStep>(StageName, ActorTime.Simulation)
{
    public const string StageName = "olympiad";

    readonly OlympiadBoard _board = board ?? throw new ArgumentNullException(nameof(board));
    readonly Func<int, OlympiadPresence?> _presence = presence ?? throw new ArgumentNullException(nameof(presence));
    readonly Random _random = random ?? throw new ArgumentNullException(nameof(random));

    public OlympiadBoard Board
    {
        get
        {
            RequireActorThread();
            return _board;
        }
    }

    /// <summary>A status request: advances like the scheduled step, with the announcements.</summary>
    public OlympiadStep Advance(long nowUnixMs) => Board.Advance(nowUnixMs, _presence, _random);

    public OlympiadStep Leave(int characterId, long nowUnixMs) => Board.Leave(characterId, nowUnixMs, _presence);

    public OlympiadStep Disconnect(int characterId, long nowUnixMs) => Board.Disconnect(characterId, nowUnixMs, _presence);

    protected override void OnStep(long nowUnixMs, List<OlympiadStep> effects) =>
        effects.Add(_board.Advance(nowUnixMs, _presence, _random, announce: false));
}
