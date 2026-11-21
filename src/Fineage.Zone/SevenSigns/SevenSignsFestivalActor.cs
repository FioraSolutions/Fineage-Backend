using Fineage.Zone.Simulation;

namespace Fineage.Zone.SevenSigns;

public sealed class SevenSignsFestivalActor : GlobalActor<SevenSignsFestivalEffect>
{
    public const string StageName = "seven-signs-festival";

    readonly SevenSignsFestivalBoard _board;

    public SevenSignsFestivalActor(SevenSignsFestivalBoard board)
        : base(StageName, ActorTime.Simulation)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
    }

    public SevenSignsFestivalBoard Board
    {
        get
        {
            RequireActorThread();
            return _board;
        }
    }

    protected override void OnStep(long nowUnixMs, List<SevenSignsFestivalEffect> effects)
    {
        for (int partyId = 0; partyId < 4096; partyId++)
        {
            var instance = _board.OfParty(partyId);
            if (instance == null || instance.EndAtUnixMs > nowUnixMs)
                continue;
            effects.Add(new SevenSignsFestivalEffect(partyId, SevenSignsFestivalEffectKind.EndInstance));
        }
    }

    public SevenSignsFestivalInstance StartInstance(
        int partyId,
        int leaderObjectId,
        int tierId,
        SevenSignsCabal cabal,
        IReadOnlyList<int> members,
        int instanceId,
        long offeringScore,
        long nowUnixMs)
    {
        RequireActorThread();
        var instance = new SevenSignsFestivalInstance
        {
            PartyId = partyId,
            LeaderObjectId = leaderObjectId,
            TierId = tierId,
            Cabal = cabal,
            MemberSnapshot = members.ToList(),
            InstanceId = instanceId,
            OfferingScore = offeringScore,
            EndAtUnixMs = nowUnixMs + _board.Catalog.FestivalLengthMs
        };
        _board.Register(instance);
        return instance;
    }

    public void KillInstance(int partyId) => _board.Remove(partyId);
}