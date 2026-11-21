using System.Diagnostics;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// A message a region body sends to an entity, applied by the scheduler thread at the stage barrier (section 3.5 of
/// <c>docs/architecture/zone-simulation.md</c>). Bodies never write another entity directly; whatever they want to
/// happen to one, owned by any region, travels as a message.
/// </summary>
internal abstract record ZoneRegionMessage(int TargetObjectId);

/// <summary>Mobius <c>addDamageHate</c> from an aggro decision: <see cref="TargetObjectId"/> starts hating the attacker.</summary>
internal sealed record HateMessage(int NpcObjectId, int AttackerObjectId, long Hate, bool AllowNpcAttacker = false)
    : ZoneRegionMessage(NpcObjectId);

public sealed partial class ZoneWorld
{
    readonly List<RandomPurpose> _sharedStreams = [];

    /// <summary>
    /// World bookkeeping a region body changes (the party vitals queue): deferred to the barrier inside a body, where
    /// other regions may be running, and done at once anywhere else.
    /// </summary>
    void AtBarrier(Action write)
    {
        if (RegionBodyScope.Of(this) is { } scope)
            scope.Defer(write);
        else
            write();
    }

    /// <summary>Sends <paramref name="message"/> from the running region body, or applies it at once outside one.</summary>
    void Post(ZoneRegionMessage message)
    {
        if (RegionBodyScope.Of(this) is { } scope)
            scope.Post(message);
        else
            Deliver(null, message);
    }

    void Deliver(RegionId? source, ZoneRegionMessage message)
    {
        if (!_byId.TryGetValue(message.TargetObjectId, out var target))
        {
            ZoneLog.Info($"[region] {message} from region {source?.ToString() ?? "world"} dropped: entity {message.TargetObjectId} left the world");
            return;
        }

        switch (message)
        {
            case HateMessage hate:
                if (_byId.TryGetValue(hate.AttackerObjectId, out var attacker))
                    AddHate(target, attacker, hate.Hate, hate.AllowNpcAttacker);
                else
                    ZoneLog.Info($"[region] {message} from region {source?.ToString() ?? "world"} dropped: attacker {hate.AttackerObjectId} left the world");
                break;
            default:
                throw new InvalidOperationException($"region message {message.GetType().Name} has no handler");
        }
    }

    /// <summary>
    /// Parallel region bodies draw from their own region's streams; an injected <see cref="Random"/> is one generator
    /// every region shares, which only a sequential executor may use.
    /// </summary>
    void RequireRegionStreamsForExecutor()
    {
        if (_simulationTicks.Executor is SequentialRegionExecutor || _sharedStreams.Count == 0)
            return;
        throw new InvalidOperationException(
            $"injected random generators ({string.Join(", ", _sharedStreams)}) are shared by every region; " +
            "a parallel region executor needs the world's seeded region streams");
    }

    /// <summary>DEBUG: a region body may write only entities its region owns.</summary>
    [Conditional("DEBUG")]
    void AssertBodyOwns(ZoneEntity entity)
    {
        if (RegionBodyScope.Of(this) is not { } scope)
            return;
        var owner = _byId.OwnerOf(entity).Region;
        if (owner != scope.Region)
        {
            Debug.Fail($"region {scope.Region}'s body wrote entity {entity.ObjectId}, owned by region {owner}");
            throw new InvalidOperationException(
                $"region {scope.Region}'s body wrote entity {entity.ObjectId}, owned by region {owner}");
        }
    }

    /// <summary>
    /// The scope one region body runs in: the directory refuses cross-region writes, fan-out records into the
    /// region's own batch, and world bookkeeping and messages wait for the barrier. <see cref="Commit"/> runs on the
    /// scheduler thread in ascending region order, so the result is the same whichever threads ran the bodies.
    /// </summary>
    sealed class RegionScopes(ZoneWorld world) : IRegionScopes
    {
        public IRegionScope Open(string stage, TickContext tick, RegionId region) => new RegionBodyScope(world, stage, region);
    }

    sealed class RegionBodyScope(ZoneWorld world, string stage, RegionId region) : IRegionScope
    {
        [ThreadStatic] static RegionBodyScope? t_current;

        readonly ZoneWorld _world = world;
        readonly RegionId _region = region;
        readonly WorldFanoutBatch _fanout = new();
        readonly List<Action> _deferred = [];
        readonly List<ZoneRegionMessage> _messages = [];

        public static RegionBodyScope? Of(ZoneWorld world) =>
            t_current is { } scope && ReferenceEquals(scope._world, world) ? scope : null;

        internal RegionId Region => _region;

        public IDisposable Enter()
        {
            if (t_current != null)
                throw new InvalidOperationException(
                    $"region {_region} ({stage}) entered while region {t_current._region} runs on this thread");
            var body = _world._byId.EnterBody(_region);
            try
            {
                var recording = _world._fanout.RecordRegion(_fanout);
                t_current = this;
                return new Entered(body, recording);
            }
            catch
            {
                // A half-entered scope must not leak the thread-static directory body onto a pooled thread:
                // every later region body scheduled on it would fail "a body runs on this thread" forever.
                body.Dispose();
                throw;
            }
        }

        public void Defer(Action write) => _deferred.Add(write);

        public void Post(ZoneRegionMessage message) => _messages.Add(message);

        public void Commit()
        {
            _world._fanout.AbsorbRegion(_fanout);
            foreach (var write in _deferred)
                write();
            foreach (var message in _messages)
                _world.Deliver(_region, message);
        }

        sealed class Entered(RegionDirectory<ZoneEntity>.BodyScope body, IDisposable recording) : IDisposable
        {
            public void Dispose()
            {
                t_current = null;
                recording.Dispose();
                body.Dispose();
            }
        }
    }
}
