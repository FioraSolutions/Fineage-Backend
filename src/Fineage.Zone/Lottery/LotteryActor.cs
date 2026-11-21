using Fineage.Zone.Simulation;

namespace Fineage.Zone.Lottery;

/// <summary>
/// The weekly lottery as a global actor: the draw runs at the step's wall sample (Mobius draws on a weekday and hour)
/// and rolls the winning numbers on the actor's stream. Ticket sales and claims are requests on <see cref="Board"/>
/// from commands. The step returns the announcements for everyone online.
/// </summary>
public sealed class LotteryActor : GlobalActor<LotteryNotice>
{
    public const string StageName = "lottery";

    LotteryBoard _board;
    Random _random;

    public LotteryActor(LotteryBoard board, Random random)
        : base(StageName, ActorTime.Calendar)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public LotteryBoard Board
    {
        get
        {
            RequireActorThread();
            return _board;
        }
    }

    /// <summary>The actor's stream; tests script the draw by replacing it.</summary>
    public Random Random
    {
        get => _random;
        set => _random = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Swaps in the persisted board (host: <c>lottery.json</c> in the state directory).</summary>
    public void Use(LotteryBoard board)
    {
        RequireActorThread();
        _board = board ?? throw new ArgumentNullException(nameof(board));
    }

    protected override void OnStep(long nowUnixMs, List<LotteryNotice> effects) => _board.Tick(nowUnixMs, _random, effects);
}
