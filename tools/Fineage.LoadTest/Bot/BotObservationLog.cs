using System.Collections.Concurrent;
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

    public void RecordCommandSent(ushort opcode, DateTimeOffset at) =>
        _tracker.NoteLocalCommand(opcode, at);

    public int VisibleNpcObjectId()
    {
        lock (_stateGate)
            return _tracker.PickAttackableNpc(_rngSeed);
    }

    readonly int _rngSeed;

    public BotObservationLog(int botId) => _rngSeed = botId * 31 + 7;

    public sealed class EntityTracker
    {
        readonly Dictionary<ulong, TrackedEntity> _byValue = new();
        long _spawnOrdinal;
        readonly List<PendingBroadcast> _pendingCombat = [];
        readonly List<double> _commandToFanoutMs = [];

        public IReadOnlyDictionary<ulong, TrackedEntity> Entities => _byValue;
        public IReadOnlyList<double> CommandToFanoutLatenciesMs => _commandToFanoutMs;

        public EntityTracker Clone()
        {
            var t = new EntityTracker();
            foreach (var (k, v) in _byValue)
                t._byValue[k] = v with { };
            t._spawnOrdinal = _spawnOrdinal;
            t._commandToFanoutMs.AddRange(_commandToFanoutMs);
            return t;
        }

        public void Apply(ushort opcode, byte[] payload, DateTimeOffset at)
        {
            switch (opcode)
            {
                case NetOpcode.EntitySpawn:
                    {
                        var s = NetCodec.DecodeEntitySpawn(payload);
                        int oid = (int)s.EntityValue;
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
                            LastDespawnOrdinal = 0,
                            SpawnCount = (_byValue.GetValueOrDefault(s.EntityValue)?.SpawnCount ?? 0) + 1,
                            Despawned = false
                        };
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
                        int oid = (int)m.EntityValue;
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
                                ObjectId = oid,
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
                        ClosePendingCombat(at, a.AttackerValue);
                        break;
                    }
                case NetOpcode.MagicSkillUse:
                    {
                        var c = NetCodec.DecodeMagicSkillUse(payload);
                        ClosePendingCombat(at, (ulong)(uint)c.CasterObjectId);
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

        void ClosePendingCombat(DateTimeOffset at, ulong actorValue)
        {
            for (int i = _pendingCombat.Count - 1; i >= 0; i--)
            {
                var p = _pendingCombat[i];
                if (at - p.SentAt > TimeSpan.FromSeconds(3))
                {
                    _pendingCombat.RemoveAt(i);
                    continue;
                }

                if (p.Opcode is NetOpcode.AttackRequest or NetOpcode.UseSkill)
                {
                    _commandToFanoutMs.Add((at - p.SentAt).TotalMilliseconds);
                    _pendingCombat.RemoveAt(i);
                    break;
                }
            }
        }

        public void NoteLocalCommand(ushort opcode, DateTimeOffset at)
        {
            if (opcode is NetOpcode.AttackRequest or NetOpcode.UseSkill or NetOpcode.MoveInput)
                _pendingCombat.Add(new PendingBroadcast(opcode, at));
        }

        public int PickAttackableNpc(int seed)
        {
            var npcs = _byValue.Values
                .Where(e => !e.Despawned && e.Attackable && e.Kind == EntityKindNet.Npc && e.Alive)
                .OrderBy(e => e.ObjectId)
                .ToList();
            if (npcs.Count == 0)
                return 0;
            return npcs[seed % npcs.Count].ObjectId;
        }

        readonly record struct PendingBroadcast(ushort Opcode, DateTimeOffset SentAt);
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
        public int SpawnCount { get; set; }
        public int LastSocialAction { get; set; }
    }
}