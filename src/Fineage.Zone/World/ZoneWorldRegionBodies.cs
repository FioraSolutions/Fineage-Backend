using System.Diagnostics;
using Fineage.Zone.Combat;
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

/// <summary>
/// Skill damage a combat body rolled against a target it does not own, applied to the target at the stage barrier
/// (Mobius <c>onMagicHitTimer</c>). The body validates and rolls; HP, hate, death and rewards land here in ascending
/// region order, so the sequential and parallel executors produce the same world.
/// </summary>
internal sealed record SkillDamageMessage(
    int AttackerObjectId, int TargetObjectId, int Damage, int SkillId, int Level, long NowUnixMs)
    : ZoneRegionMessage(TargetObjectId);

/// <summary>
/// A debuff-nuke abnormal landing with its skill damage (Mobius <c>getEffects</c> at hitTime): the body checked the
/// template applies one; the barrier runs the land roll and the application on the target.
/// </summary>
internal sealed record AbnormalApplyMessage(
    int AttackerObjectId, int TargetObjectId, int SkillId, int Level, long NowUnixMs)
    : ZoneRegionMessage(TargetObjectId);

/// <summary>
/// A <see cref="PendingSkillHit"/> whose <see cref="PendingSkillHit.Apply"/> effect (heal, buff, debuff, quest
/// effect, NPC suicide blast) touches entities the resolving body does not own: the body ran the launch checks;
/// the delegate runs at the barrier. In-process only — delegates cannot cross a region transport (step 13+).
/// </summary>
internal sealed record SkillEffectMessage(int HolderObjectId, PendingSkillHit Hit, long NowUnixMs)
    : ZoneRegionMessage(Hit.TargetId);

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
            case SkillDamageMessage hit:
                if (!_byId.TryGetValue(hit.AttackerObjectId, out var hitter))
                {
                    ZoneLog.Info($"[region] {message} from region {source?.ToString() ?? "world"} dropped: attacker {hit.AttackerObjectId} left the world");
                    break;
                }
                if (target.CurHp <= 0 || target.Dead)
                {
                    ZoneLog.Info($"[region] skill damage from {hit.AttackerObjectId} on {hit.TargetObjectId} dropped: target already dead");
                    break;
                }
                if (hitter.Kind == ZoneEntityKind.Npc)
                    ApplyNpcSkillDamage(hitter, target, hit.Damage, hit.NowUnixMs);
                else
                    ApplyPlayerSkillDamage(hitter, target, hit.Damage, hit.SkillId, hit.Level, hit.NowUnixMs);
                break;
            case AbnormalApplyMessage apply:
                if (!_byId.TryGetValue(apply.AttackerObjectId, out var caster))
                {
                    ZoneLog.Info($"[region] {message} from region {source?.ToString() ?? "world"} dropped: attacker {apply.AttackerObjectId} left the world");
                    break;
                }
                if (_game.Skills.Find(apply.SkillId) is not { } abnormalTemplate ||
                    !abnormalTemplate.TryLevelData(apply.Level, out var abnormalLevel))
                {
                    ZoneLog.Error($"[region] abnormal {apply.SkillId} lv{apply.Level} from {apply.AttackerObjectId} on {apply.TargetObjectId} dropped: skill or level missing from the catalog");
                    break;
                }
                // Sequentially the abnormal landed on the NPC corpse only to be wiped by RemoveDeadNpc a line
                // later; skip it here. A dead player keeps it (death cleared abnormals before it re-landed).
                if (target.Kind == ZoneEntityKind.Npc && (target.Dead || target.CurHp <= 0))
                {
                    ZoneLog.Info($"[region] abnormal {apply.SkillId} lv{apply.Level} from {apply.AttackerObjectId} on {apply.TargetObjectId} dropped: target died with the hit");
                    break;
                }
                ApplyAbnormalFromSkill(abnormalTemplate, abnormalLevel, target, apply.NowUnixMs, attacker: caster);
                break;
            case SkillEffectMessage effect:
                if (!_byId.TryGetValue(effect.HolderObjectId, out var holder))
                {
                    ZoneLog.Info($"[region] {message} from region {source?.ToString() ?? "world"} dropped: caster {effect.HolderObjectId} left the world");
                    break;
                }
                if (effect.Hit.Apply is not { } applyDelegate)
                    throw new InvalidOperationException($"skill effect message for holder {effect.HolderObjectId} carries no Apply delegate");
                // DeferredEffect (player) and Action<ZoneEntity,long> (NPC support) share the (target, now) shape.
                applyDelegate.DynamicInvoke(target, effect.NowUnixMs);
                break;
            default:
                throw new InvalidOperationException($"region message {message.GetType().Name} has no handler");
        }
    }

    /// <summary>
    /// Global AOI grid update. Region bodies defer it to <see cref="RegionBodyScope.Commit"/> so parallel stages do not
    /// race on <see cref="Interest.GridAoi"/>; commit runs on the scheduler thread in ascending region order.
    /// </summary>
    void MoveAoi(int objectId, int x, int y)
    {
        if (RegionBodyScope.Of(this) is { } scope)
            scope.ScheduleAoiMove(objectId, x, y);
        else
            MoveAoiNow(objectId, x, y);
    }

    internal void MoveAoiNow(int objectId, int x, int y) => _aoi.Move(objectId, x, y);

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
        readonly Dictionary<int, (int X, int Y)> _aoiMoves = [];

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

        internal void ScheduleAoiMove(int objectId, int x, int y) => _aoiMoves[objectId] = (x, y);

        public void Commit()
        {
            _world._fanout.AbsorbRegion(_fanout);
            foreach (int id in _aoiMoves.Keys.Order())
            {
                var (x, y) = _aoiMoves[id];
                _world.MoveAoiNow(id, x, y);
            }

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
