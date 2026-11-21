using Fineage.Zone.Simulation;
namespace Fineage.Zone.Fishing;
public sealed class FishingChampionshipActor : GlobalActor<bool>
{
    public const string StageName = "fishing-championship";
    FishingChampionshipBoard _board;
    public FishingChampionshipActor(FishingChampionshipBoard board) : base(StageName, ActorTime.Calendar) => _board = board;
    public FishingChampionshipBoard Board { get { RequireActorThread(); return _board; } }
    public void Use(FishingChampionshipBoard board) { RequireActorThread(); _board = board; }
    protected override void OnStep(long nowUnixMs, List<bool> effects) { if (_board.Tick(nowUnixMs)) effects.Add(true); }
}
