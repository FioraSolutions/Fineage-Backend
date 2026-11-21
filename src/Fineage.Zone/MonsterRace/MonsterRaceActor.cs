using Fineage.Zone.Simulation;

namespace Fineage.Zone.MonsterRace;

public sealed class MonsterRaceActor : GlobalActor<MonsterRaceEffect>
{
    public const string StageName = "monster-race";

    MonsterRaceBoard _board;
    Random _random;

    public MonsterRaceActor(MonsterRaceBoard board, Random random)
        : base(StageName, ActorTime.Calendar)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    public MonsterRaceBoard Board
    {
        get
        {
            RequireActorThread();
            return _board;
        }
    }

    public Random Random
    {
        get => _random;
        set => _random = value ?? throw new ArgumentNullException(nameof(value));
    }

    protected override void OnStep(long nowUnixMs, List<MonsterRaceEffect> effects)
    {
        foreach (var batch in _board.Tick(nowUnixMs, _random))
            effects.Add(batch);
    }
}