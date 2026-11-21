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
        var catalog = _board.Catalog;
        for (int partyId = 0; partyId < SevenSignsFestivalBoard.MaxPartySlots; partyId++)
        {
            var instance = _board.OfParty(partyId);
            if (instance == null)
                continue;
            if (instance.EndAtUnixMs > 0 && instance.EndAtUnixMs <= nowUnixMs)
            {
                effects.Add(new SevenSignsFestivalEffect(partyId, SevenSignsFestivalEffectKind.EndInstance));
                continue;
            }

            long elapsed = nowUnixMs - instance.StartedAtUnixMs;
            if (!instance.WitchSpawned && elapsed >= catalog.FirstSpawnMs)
            {
                effects.Add(new SevenSignsFestivalEffect(partyId, SevenSignsFestivalEffectKind.SpawnWitch));
                effects.Add(new SevenSignsFestivalEffect(partyId, SevenSignsFestivalEffectKind.SpawnPrimary));
            }
            if (!instance.SecondarySpawned && elapsed >= catalog.SecondSpawnMs)
                effects.Add(new SevenSignsFestivalEffect(partyId, SevenSignsFestivalEffectKind.SpawnSecondary));
            if (!instance.ChestSpawned && elapsed >= catalog.ChestSpawnMs)
                effects.Add(new SevenSignsFestivalEffect(partyId, SevenSignsFestivalEffectKind.SpawnChests));
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
            StartedAtUnixMs = nowUnixMs,
            EndAtUnixMs = nowUnixMs + _board.Catalog.FestivalLengthMs
        };
        _board.Register(instance);
        return instance;
    }

    public void KillInstance(int partyId) => _board.Remove(partyId);
}