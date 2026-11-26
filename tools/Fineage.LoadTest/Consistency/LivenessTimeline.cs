using Fineage.LoadTest.Bot;
using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.LoadTest.Consistency;

/// <summary>
/// Whether one bot's own pawn was alive at a historical instant, from the frames that bot received about itself.
/// End-state <see cref="BotObservationLog.TrackedEntity.Alive"/> cannot answer an attack event that happened before
/// the bot died or revived later.
/// </summary>
public sealed class LivenessTimeline
{
    readonly List<Point> _points = [];

    public readonly record struct Point(DateTimeOffset At, bool Alive);

    public int Count => _points.Count;

    public static LivenessTimeline From(IEnumerable<ObservedFrame> frames, ulong selfId)
    {
        var timeline = new LivenessTimeline();
        if (selfId == 0)
            return timeline;

        foreach (var frame in frames)
        {
            switch (frame.Opcode)
            {
                case NetOpcode.EntitySpawn:
                    {
                        var spawn = NetCodec.DecodeEntitySpawn(frame.Payload);
                        if (spawn.EntityValue == selfId)
                            timeline.Add(frame.At, !spawn.Dead);
                        break;
                    }
                case NetOpcode.EntityDie:
                    {
                        var die = NetCodec.DecodeEntityDie(frame.Payload);
                        if (die.EntityValue == selfId)
                            timeline.Add(frame.At, alive: false);
                        break;
                    }
                case NetOpcode.EntityDespawn:
                    {
                        var despawn = NetCodec.DecodeEntityDespawn(frame.Payload);
                        if (despawn.EntityValue == selfId)
                            timeline.Add(frame.At, alive: false);
                        break;
                    }
                case NetOpcode.StatusUpdate:
                    {
                        var status = NetCodec.DecodeStatusUpdate(frame.Payload);
                        if (status.EntityValue != selfId)
                            break;
                        for (int i = 0; i < status.AttributeIds.Length && i < status.Values.Length; i++)
                        {
                            if (status.AttributeIds[i] != InterludeStatus.CurHp)
                                continue;
                            timeline.Add(frame.At, status.Values[i] > 0);
                            break;
                        }

                        break;
                    }
                case NetOpcode.UserInfo:
                    {
                        var info = NetCodec.DecodeUserInfo(frame.Payload);
                        if (info.EntityValue == selfId)
                            timeline.Add(frame.At, info.CurHp > 0);
                        break;
                    }
                case NetOpcode.Revive:
                    {
                        var revive = NetCodec.DecodeRevive(frame.Payload);
                        if ((ulong)(uint)revive.ObjectId == selfId)
                            timeline.Add(frame.At, alive: true);
                        break;
                    }
            }
        }

        timeline._points.Sort((a, b) => a.At.UtcTicks.CompareTo(b.At.UtcTicks));
        return timeline;
    }

    void Add(DateTimeOffset at, bool alive) => _points.Add(new Point(at, alive));

    /// <summary>Last known liveness at or before <paramref name="at"/>; false before the first observation.</summary>
    public bool TryAt(DateTimeOffset at, out bool alive)
    {
        alive = false;
        long target = at.UtcTicks;
        int lo = 0;
        int hi = _points.Count - 1;
        int found = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (_points[mid].At.UtcTicks <= target)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (found < 0)
            return false;
        alive = _points[found].Alive;
        return true;
    }
}
