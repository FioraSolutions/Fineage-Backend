using Fineage.Zone.Simulation;

namespace Fineage.Zone.DimensionalRift;

/// <summary>Jump and spawn timers for active dimensional rift instances.</summary>
public sealed class DimensionalRiftActor : GlobalActor<DimensionalRiftEffect>
{
    public const string StageName = "dimensional-rift";

    readonly DimensionalRiftBoard _board;
    readonly DimensionalRiftSettings _settings;
    readonly Random _rng;

    public DimensionalRiftActor(DimensionalRiftBoard board, DimensionalRiftSettings? settings = null, Random? random = null)
        : base(StageName, ActorTime.Simulation)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _settings = settings ?? DimensionalRiftSettings.Default;
        _rng = random ?? new Random();
    }

    public DimensionalRiftBoard Board
    {
        get
        {
            RequireActorThread();
            return _board;
        }
    }

    public DimensionalRiftInstance StartInstance(int partyId, int leaderObjectId, int type, int room, IReadOnlyList<int> members, long nowUnixMs)
    {
        RequireActorThread();
        var instance = new DimensionalRiftInstance
        {
            PartyId = partyId,
            LeaderObjectId = leaderObjectId,
            Type = type,
            CurrentRoom = room,
            MaxJumps = _settings.MaxJumps is >= 1 and <= 8 ? _settings.MaxJumps : 4,
            MemberSnapshot = members.ToList(),
            BossRoom = _board.Catalog.Room(type, room).Boss
        };
        ScheduleJump(instance, nowUnixMs, manual: false);
        instance.SpawnAtUnixMs = nowUnixMs + _settings.SpawnDelayMs;
        _board.Register(instance);
        return instance;
    }

    public void ScheduleJump(DimensionalRiftInstance instance, long nowUnixMs, bool manual)
    {
        RequireActorThread();
        int min = _settings.AutoJumpMinSec;
        int max = Math.Max(min, _settings.AutoJumpMaxSec);
        long jumpMs = _rng.Next(min, max + 1) * 1000L;
        if (instance.BossRoom)
            jumpMs = (long)(jumpMs * _settings.BossRoomTimeMultiply);
        instance.JumpAtUnixMs = nowUnixMs + jumpMs;
        instance.EarthquakeAtUnixMs = instance.JumpAtUnixMs - 7000;
        instance.WaitingReviveTeleport = manual;
    }

    public void KillInstance(int partyId)
    {
        RequireActorThread();
        _board.Remove(partyId);
    }

    protected override void OnStep(long nowUnixMs, List<DimensionalRiftEffect> effects)
    {
        foreach (var instance in SnapshotInstances())
        {
            if (!instance.Spawned && instance.SpawnAtUnixMs > 0 && nowUnixMs >= instance.SpawnAtUnixMs)
                effects.Add(new DimensionalRiftEffect(DimensionalRiftEffectKind.SpawnRoom, instance.PartyId));

            if (instance.EarthquakeAtUnixMs > 0 && nowUnixMs >= instance.EarthquakeAtUnixMs)
            {
                instance.EarthquakeAtUnixMs = 0;
                effects.Add(new DimensionalRiftEffect(DimensionalRiftEffectKind.EarthquakeWarning, instance.PartyId));
            }

            if (instance.JumpAtUnixMs > 0 && nowUnixMs >= instance.JumpAtUnixMs)
                effects.Add(new DimensionalRiftEffect(DimensionalRiftEffectKind.Jump, instance.PartyId));
        }
    }

    List<DimensionalRiftInstance> SnapshotInstances()
    {
        var list = new List<DimensionalRiftInstance>();
        foreach (int partyId in _board.ActivePartyIds)
        {
            if (_board.OfParty(partyId) is { } instance)
                list.Add(instance);
        }

        return list;
    }
}