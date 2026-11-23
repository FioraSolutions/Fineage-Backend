using System.Collections.Concurrent;
using System.Diagnostics;
using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.LoadTest.Bot;

public readonly record struct ObservedFrame(
    DateTimeOffset At,
    ushort Opcode,
    byte[] Payload);

/// <summary>Thread-safe log of every zone frame a bot received (fanout + private).</summary>
public sealed class BotObservationLog
{
    const int MaxAttackRange = 1200;

    readonly ConcurrentQueue<ObservedFrame> _frames = new();
    readonly object _stateGate = new();
    EntityTracker _tracker = new();

    public IReadOnlyList<ObservedFrame> Snapshot() => _frames.ToArray();

    public EntityTracker Tracker
    {
        get
        {
            lock (_stateGate)
                return _tracker.Clone();
        }
    }

    public void Record(ushort opcode, byte[] payload, DateTimeOffset at)
    {
        var copy = payload.ToArray();
        _frames.Enqueue(new ObservedFrame(at, opcode, copy));
        lock (_stateGate)
            _tracker.Apply(opcode, copy, at);
    }

    public void RecordCommandSent(ushort opcode, DateTimeOffset at, int targetObjectId = 0, int skillId = 0)
    {
        lock (_stateGate)
            _tracker.NoteLocalCommand(opcode, at, targetObjectId, skillId);
    }

    public void SetSelfEntity(ulong entityValue)
    {
        lock (_stateGate)
            _tracker.SetSelfEntity(entityValue);
    }

    public int VisibleNpcObjectId() => TryPickAttackTarget(out int target) ? target : 0;

    int _lastPickedTarget;
    public int LastPickedTargetDistance { get; private set; }

    public bool TryPickAttackTarget(out int targetObjectId)
    {
        lock (_stateGate)
        {
            targetObjectId = _tracker.PickAttackableNpc(MaxAttackRange, out int dist);
            _lastPickedTarget = targetObjectId;
            LastPickedTargetDistance = dist;
            return targetObjectId > 0;
        }
    }

    public BotObservationLog(int botId) => BotId = botId;

    /// <summary>Fleet index. Wire identity is the <c>IsSelf</c> spawn, not this id.</summary>
    public int BotId { get; }

    public sealed class EntityTracker
    {
        static readonly TimeSpan PendingTtl = TimeSpan.FromSeconds(3);

        readonly Dictionary<ulong, TrackedEntity> _byValue = new();
        long _spawnOrdinal;
        readonly List<PendingCommand> _pendingCombat = [];
        readonly List<double> _attackCommandToAttackStateMs = [];
        readonly List<double> _useSkillToMagicSkillUseMs = [];
        readonly List<LatencySample> _attackSamples = [];
        readonly List<LatencySample> _skillSamples = [];
        readonly List<LatencySample> _legacyCommandToFanoutSamples = [];
        ulong _selfEntityValue;
        int _attackSampleCount;
        int _skillSampleCount;
        int _attackTimeoutCount;
        int _skillTimeoutCount;
        int _lastCommandTargetDistance;

        public int LastCommandTargetDistance => _lastCommandTargetDistance;
        public int AttackSampleCount => _attackSampleCount;
        public int SkillSampleCount => _skillSampleCount;
        public int AttackTimeoutCount => _attackTimeoutCount;
        public int SkillTimeoutCount => _skillTimeoutCount;

        public IReadOnlyDictionary<ulong, TrackedEntity> Entities => _byValue;

        /// <summary>Legacy aggregate; prefer <see cref="AttackFanoutSamples"/> and <see cref="SkillFanoutSamples"/>.</summary>
        public IReadOnlyList<LatencySample> CommandToFanoutSamples => _legacyCommandToFanoutSamples;

        public IReadOnlyList<LatencySample> AttackFanoutSamples => _attackSamples;
        public IReadOnlyList<LatencySample> SkillFanoutSamples => _skillSamples;

        public void SetSelfEntity(ulong entityValue) => _selfEntityValue = entityValue;

        public EntityTracker Clone()
        {
            var t = new EntityTracker();
            foreach (var (k, v) in _byValue)
                t._byValue[k] = v with { };
            t._spawnOrdinal = _spawnOrdinal;
            t._attackCommandToAttackStateMs.AddRange(_attackCommandToAttackStateMs);
            t._useSkillToMagicSkillUseMs.AddRange(_useSkillToMagicSkillUseMs);
            t._attackSamples.AddRange(_attackSamples);
            t._skillSamples.AddRange(_skillSamples);
            t._legacyCommandToFanoutSamples.AddRange(_legacyCommandToFanoutSamples);
            t._selfEntityValue = _selfEntityValue;
            t._attackSampleCount = _attackSampleCount;
            t._skillSampleCount = _skillSampleCount;
            t._attackTimeoutCount = _attackTimeoutCount;
            t._skillTimeoutCount = _skillTimeoutCount;
            t._lastCommandTargetDistance = _lastCommandTargetDistance;
            return t;
        }

        public void Apply(ushort opcode, byte[] payload, DateTimeOffset at)
        {
            ExpirePending(at);
            switch (opcode)
            {
                case NetOpcode.EntitySpawn:
                    {
                        var s = NetCodec.DecodeEntitySpawn(payload);
                        int oid = (int)s.EntityValue;
                        var previous = _byValue.GetValueOrDefault(s.EntityValue);
                        // A despawn closes the life. The next spawn starts a new open run.
                        // SpawnCount stays lifetime-cumulative; the oracle reads the open run,
                        // otherwise a respawn (gremlin decay + 15s) looks like a spawn with no despawn.
                        int openRun = previous == null || previous.Despawned
                            ? 1
                            : previous.SpawnsWithoutDespawn + 1;
                        _byValue[s.EntityValue] = new TrackedEntity
                        {
                            ObjectId = oid,
                            EntityGeneration = s.EntityGeneration,
                            Kind = s.Kind,
                            Name = s.Name,
                            Attackable = s.Attackable,
                            Alive = !s.Dead,
                            X = s.X,
                            Y = s.Y,
                            Z = s.Z,
                            MaxHp = s.MaxHp,
                            CurHp = s.CurHp > 0 ? s.CurHp : s.MaxHp,
                            LastSpawnOrdinal = ++_spawnOrdinal,
                            LastDespawnOrdinal = previous?.LastDespawnOrdinal ?? 0,
                            SpawnCount = (previous?.SpawnCount ?? 0) + 1,
                            SpawnsWithoutDespawn = openRun,
                            Despawned = false
                        };
                        // The entering client's own pawn is the only spawn marked IsSelf.
                        // Target range and AttackState/MagicSkillUse pairing both key off this id.
                        if (s.IsSelf)
                            _selfEntityValue = s.EntityValue;
                        break;
                    }
                case NetOpcode.EntityDespawn:
                    {
                        var d = NetCodec.DecodeEntityDespawn(payload);
                        if (_byValue.TryGetValue(d.EntityValue, out var e))
                        {
                            e.Despawned = true;
                            e.LastDespawnOrdinal = ++_spawnOrdinal;
                            _byValue[d.EntityValue] = e;
                        }

                        break;
                    }
                case NetOpcode.MoveState:
                    {
                        var m = NetCodec.DecodeMoveState(payload);
                        ulong key = m.EntityValue;
                        if (_byValue.TryGetValue(key, out var e))
                        {
                            e.X = m.DestX;
                            e.Y = m.DestY;
                            e.Z = m.DestZ;
                            e.Despawned = false;
                            _byValue[key] = e;
                        }
                        else
                        {
                            _byValue[key] = new TrackedEntity
                            {
                                ObjectId = (int)m.EntityValue,
                                EntityGeneration = m.EntityGeneration,
                                X = m.DestX,
                                Y = m.DestY,
                                Z = m.DestZ
                            };
                        }

                        break;
                    }
                case NetOpcode.StatusUpdate:
                    {
                        var st = NetCodec.DecodeStatusUpdate(payload);
                        ulong key = st.EntityValue;
                        if (_byValue.TryGetValue(key, out var e))
                        {
                            for (int i = 0; i < st.AttributeIds.Length && i < st.Values.Length; i++)
                            {
                                if (st.AttributeIds[i] == InterludeStatus.CurHp)
                                    e.CurHp = st.Values[i];
                                else if (st.AttributeIds[i] == InterludeStatus.MaxHp)
                                    e.MaxHp = st.Values[i];
                            }

                            _byValue[key] = e;
                        }

                        break;
                    }
                case NetOpcode.AttackState:
                    {
                        var a = NetCodec.DecodeAttackState(payload);
                        if (_selfEntityValue != 0 && a.AttackerValue == _selfEntityValue)
                        {
                            TryClosePending(NetOpcode.AttackState, at, p => p.CommandOpcode == NetOpcode.AttackRequest);
                        }

                        break;
                    }
                case NetOpcode.MagicSkillUse:
                    {
                        var c = NetCodec.DecodeMagicSkillUse(payload);
                        if (_selfEntityValue != 0 && (ulong)(uint)c.CasterObjectId == (ulong)(uint)_selfEntityValue)
                        {
                            TryClosePending(NetOpcode.MagicSkillUse, at,
                                p => p.CommandOpcode == NetOpcode.UseSkill && (p.SkillId == 0 || p.SkillId == c.SkillId));
                        }

                        break;
                    }
                case NetOpcode.SocialAction:
                    {
                        var sa = NetCodec.DecodeSocialAction(payload);
                        if (_byValue.TryGetValue(sa.EntityValue, out var e))
                        {
                            e.LastSocialAction = sa.ActionId;
                            _byValue[sa.EntityValue] = e;
                        }

                        break;
                    }
            }
        }

        public void NoteLocalCommand(ushort opcode, DateTimeOffset at, int targetObjectId, int skillId)
        {
            ExpirePending(at);
            if (opcode == NetOpcode.AttackRequest)
            {
                _lastCommandTargetDistance = DistanceToTarget(targetObjectId);
                _pendingCombat.Add(new PendingCommand(
                    NetOpcode.AttackRequest,
                    NetOpcode.AttackState,
                    targetObjectId,
                    SkillId: 0,
                    Stopwatch.GetTimestamp(),
                    at));
            }
            else if (opcode == NetOpcode.UseSkill)
            {
                _lastCommandTargetDistance = DistanceToTarget(targetObjectId);
                _pendingCombat.Add(new PendingCommand(
                    NetOpcode.UseSkill,
                    NetOpcode.MagicSkillUse,
                    targetObjectId,
                    skillId,
                    Stopwatch.GetTimestamp(),
                    at));
            }
        }

        int DistanceToTarget(int targetObjectId)
        {
            if (targetObjectId <= 0 || !_byValue.TryGetValue(_selfEntityValue, out var self))
                return -1;
            foreach (var ent in _byValue.Values)
            {
                if (ent.ObjectId != targetObjectId || ent.Despawned)
                    continue;
                return Distance(self.X, self.Y, ent.X, ent.Y);
            }

            return -1;
        }

        void TryClosePending(ushort responseOpcode, DateTimeOffset at, Func<PendingCommand, bool> match)
        {
            for (int i = _pendingCombat.Count - 1; i >= 0; i--)
            {
                var p = _pendingCombat[i];
                if (p.ExpectedResponseOpcode != responseOpcode || !match(p))
                    continue;
                double ms = (at - p.SentAt).TotalMilliseconds;
                RecordSample(p.CommandOpcode, ms, at);
                _pendingCombat.RemoveAt(i);
                return;
            }
        }

        void RecordSample(ushort commandOpcode, double ms, DateTimeOffset at)
        {
            var sample = new LatencySample(ms, at);
            _legacyCommandToFanoutSamples.Add(sample);
            if (commandOpcode == NetOpcode.AttackRequest)
            {
                _attackCommandToAttackStateMs.Add(ms);
                _attackSamples.Add(sample);
                _attackSampleCount++;
            }
            else if (commandOpcode == NetOpcode.UseSkill)
            {
                _useSkillToMagicSkillUseMs.Add(ms);
                _skillSamples.Add(sample);
                _skillSampleCount++;
            }
        }

        void ExpirePending(DateTimeOffset now)
        {
            for (int i = _pendingCombat.Count - 1; i >= 0; i--)
            {
                if (now - _pendingCombat[i].SentAt <= PendingTtl)
                    continue;
                if (_pendingCombat[i].CommandOpcode == NetOpcode.AttackRequest)
                    _attackTimeoutCount++;
                else if (_pendingCombat[i].CommandOpcode == NetOpcode.UseSkill)
                    _skillTimeoutCount++;
                _pendingCombat.RemoveAt(i);
            }
        }

        /// <summary>
        /// Nearest living attackable NPC within <paramref name="maxRange"/> of the observer.
        /// The observer is the entity whose spawn was marked <c>IsSelf</c>.
        /// </summary>
        public int PickAttackableNpc(int maxRange, out int distance)
        {
            distance = -1;
            if (_selfEntityValue == 0 || !_byValue.TryGetValue(_selfEntityValue, out var self))
                return 0;

            int bestId = 0;
            int bestDist = int.MaxValue;
            foreach (var ent in _byValue.Values)
            {
                if (ent.Despawned || !ent.Attackable || ent.Kind != EntityKindNet.Npc || !ent.Alive)
                    continue;
                int dist = Distance(self.X, self.Y, ent.X, ent.Y);
                if (dist > maxRange)
                    continue;
                if (dist < bestDist || (dist == bestDist && (bestId == 0 || ent.ObjectId < bestId)))
                {
                    bestDist = dist;
                    bestId = ent.ObjectId;
                }
            }

            if (bestId == 0)
                return 0;

            distance = bestDist;
            return bestId;
        }

        static int Distance(int x1, int y1, int x2, int y2)
        {
            long dx = x1 - x2;
            long dy = y1 - y2;
            return (int)Math.Sqrt(dx * dx + dy * dy);
        }

        readonly record struct PendingCommand(
            ushort CommandOpcode,
            ushort ExpectedResponseOpcode,
            int TargetObjectId,
            int SkillId,
            long SentAtMonotonicTicks,
            DateTimeOffset SentAt);

        public readonly record struct LatencySample(double Milliseconds, DateTimeOffset ObservedAt);
    }

    public sealed record TrackedEntity
    {
        public int ObjectId { get; init; }
        public uint EntityGeneration { get; init; }
        public EntityKindNet Kind { get; init; }
        public string Name { get; init; } = "";
        public bool Attackable { get; init; }
        public bool Alive { get; init; } = true;
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int CurHp { get; set; }
        public int MaxHp { get; set; }
        public bool Despawned { get; set; }
        public long LastSpawnOrdinal { get; set; }
        public long LastDespawnOrdinal { get; set; }
        /// <summary>Lifetime EntitySpawn count, including lives closed by EntityDespawn.</summary>
        public int SpawnCount { get; set; }
        /// <summary>EntitySpawns since the last EntityDespawn. 1 on the first spawn of a life.</summary>
        public int SpawnsWithoutDespawn { get; set; }
        public int LastSocialAction { get; set; }
    }
}