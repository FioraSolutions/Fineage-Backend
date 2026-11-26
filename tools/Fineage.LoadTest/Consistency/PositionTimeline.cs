using Fineage.LoadTest.Bot;
using Fineage.Net.Contract;

namespace Fineage.LoadTest.Consistency;

/// <summary>
/// Where one bot's own pawn was over the run, from the frames it received about itself
/// (self EntitySpawn, MoveState destination, PositionCorrection, TeleportToLocation).
/// Gatekeeper travellers cross the world, so checks on timed frames must use the position at
/// that time, not the end-of-run position.
/// </summary>
public sealed class PositionTimeline
{
    readonly List<Point> _points = [];

    public readonly record struct Point(DateTimeOffset At, int X, int Y);

    public int Count => _points.Count;

    /// <summary>Every position sample in receive-time order. Callers that compare two bots must use the same instant.</summary>
    public IReadOnlyList<Point> Points => _points;

    public DateTimeOffset? FirstAt => _points.Count == 0 ? null : _points[0].At;

    /// <summary>The newest position frame this bot received about itself.</summary>
    public DateTimeOffset? LastAt => _points.Count == 0 ? null : _points[^1].At;

    public static PositionTimeline From(IEnumerable<ObservedFrame> frames)
    {
        var t = new PositionTimeline();
        var self = new HashSet<ulong>();
        foreach (var f in frames)
        {
            switch (f.Opcode)
            {
                case NetOpcode.EntitySpawn:
                    {
                        var s = NetCodec.DecodeEntitySpawn(f.Payload);
                        if (!s.IsSelf)
                            break;
                        self.Add(s.EntityValue);
                        t.Add(f.At, s.X, s.Y);
                        break;
                    }
                case NetOpcode.MoveState:
                    {
                        var m = NetCodec.DecodeMoveState(f.Payload);
                        // Origin is the server's actual pawn position for this leg; Dest may be an unreached tip.
                        if (self.Contains(m.EntityValue))
                            t.Add(f.At, m.OriginX, m.OriginY);
                        break;
                    }
                case NetOpcode.PositionCorrection:
                    {
                        var c = NetCodec.DecodePositionCorrection(f.Payload);
                        if (self.Contains(c.EntityValue))
                            t.Add(f.At, c.X, c.Y);
                        break;
                    }
                case NetOpcode.TeleportToLocation:
                    {
                        var tp = NetCodec.DecodeTeleportToLocation(f.Payload);
                        if (self.Contains((ulong)(uint)tp.ObjectId))
                            t.Add(f.At, tp.X, tp.Y);
                        break;
                    }
            }
        }

        t._points.Sort((a, b) => a.At.UtcTicks.CompareTo(b.At.UtcTicks));
        return t;
    }

    public void Add(DateTimeOffset at, int x, int y) => _points.Add(new Point(at, x, y));

    /// <summary>Last known position at or before <paramref name="at"/>; false before the first point.</summary>
    public bool TryAt(DateTimeOffset at, out int x, out int y)
    {
        bool found = TryAt(at, out x, out y, out _);
        return found;
    }

    /// <summary>
    /// Last known position at or before <paramref name="at"/> and the timestamp of that sample. A comparison
    /// against a sample that is minutes old is a comparison across an unobserved movement interval, not a
    /// position divergence at a common instant.
    /// </summary>
    public bool TryAt(DateTimeOffset at, out int x, out int y, out DateTimeOffset sampleAt)
    {
        x = 0;
        y = 0;
        sampleAt = default;
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
        (sampleAt, x, y) = _points[found];
        return true;
    }
}
