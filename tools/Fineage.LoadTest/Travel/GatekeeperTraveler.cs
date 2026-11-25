using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.LoadTest.Travel;

/// <summary>One frame the bot must send for its next travel step.</summary>
public readonly record struct TravelCommand(ushort Opcode, byte[] Payload, string Describe);

/// <summary>
/// A bot that travels town to town the way a player does: walk to the village gatekeeper, talk,
/// open the teleport list, pick another town, verify the TeleportToLocation lands there, repeat.
/// Every refusal must carry a server reason code (a gatekeeper SystemMessage); anything else —
/// a Reject frame, a timeout, a missing teleport link, or landing somewhere else — is unexplained
/// and fails the run.
/// </summary>
public sealed class GatekeeperTraveler
{
    /// <summary>
    /// Mobius <c>Npc.INTERACTION_DISTANCE</c>: talk and bypass are refused outside this 3D radius, so the bot must
    /// be this close before it talks. Matches <c>NpcHtmlProtocol.InteractionDistance</c>.
    /// </summary>
    public const int TalkRange = 250;
    /// <summary>A gatekeeper this close must be in the knownlist (AOI 3×3 cells of 3000).</summary>
    public const int SightRange = 6000;
    /// <summary>
    /// Step length for one walk frame. Small enough that a hop never overshoots the talk radius.
    /// </summary>
    public const int WalkHop = 1500;
    /// <summary>Slower than any Interlude run speed, so a hop is never re-issued before the pawn arrives.</summary>
    public const float MinRunSpeed = 80f;
    public const int MaxPagesPerTalk = 6;
    public static readonly TimeSpan PageTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan TeleportTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan TalkSlack = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromSeconds(20);

    /// <summary>Teleport offset (zone <c>teleportOffset</c>) plus position rounding.</summary>
    public static readonly int ArrivalTolerance = NpcServiceProtocol.DefaultTeleportOffset * 2 + 100;

    enum Phase
    {
        Seek,
        Wait,
        Talking,
        Teleporting
    }

    readonly object _gate = new();
    readonly GatekeeperDirectory _directory;
    readonly Random _rng;
    readonly TimeSpan _tripCooldown;
    readonly Queue<(ushort Opcode, byte[] Payload)> _inbox = new();
    readonly Dictionary<ulong, (int ObjectId, int NpcId, int X, int Y, int Z)> _visibleGatekeepers = new();
    readonly HashSet<string> _followed = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Unvisited links from every page of this talk, breadth-first (an info page may be a dead end).</summary>
    readonly List<GatekeeperLink> _pending = [];

    Phase _phase = Phase.Seek;
    DateTimeOffset _until;
    ulong _self;
    bool _selfKnown;
    int _x, _y, _z;
    float _runSpeed;
    int _talkObjectId;
    int _talkNpcId;
    int _pages;
    TeleportDestination _expected;

    public GatekeeperTraveler(GatekeeperDirectory directory, int seed, TimeSpan tripCooldown, TravelStats stats)
    {
        _directory = directory;
        _rng = new Random(seed);
        _tripCooldown = tripCooldown;
        Stats = stats;
    }

    public TravelStats Stats { get; }

    public (int X, int Y, int Z) Position
    {
        get
        {
            lock (_gate)
                return (_x, _y, _z);
        }
    }

    /// <summary>Receive thread: every zone frame the bot got.</summary>
    public void OnFrame(ushort opcode, byte[] payload)
    {
        lock (_gate)
        {
            switch (opcode)
            {
                case NetOpcode.EntitySpawn:
                    {
                        var s = NetCodec.DecodeEntitySpawn(payload);
                        if (s.IsSelf)
                        {
                            _self = s.EntityValue;
                            _selfKnown = true;
                            (_x, _y, _z) = (s.X, s.Y, s.Z);
                            _runSpeed = s.RunSpeed;
                        }
                        else if (s.Kind == EntityKindNet.Npc && _directory.NpcIds.Contains(s.TemplateId))
                        {
                            _visibleGatekeepers[s.EntityValue] = ((int)s.EntityValue, s.TemplateId, s.X, s.Y, s.Z);
                        }

                        return;
                    }
                case NetOpcode.EntityDespawn:
                    _visibleGatekeepers.Remove(NetCodec.DecodeEntityDespawn(payload).EntityValue);
                    return;
                case NetOpcode.MoveState:
                    {
                        var m = NetCodec.DecodeMoveState(payload);
                        if (_selfKnown && m.EntityValue == _self)
                            (_x, _y, _z) = (m.DestX, m.DestY, m.DestZ);
                        return;
                    }
                case NetOpcode.PositionCorrection:
                    {
                        var c = NetCodec.DecodePositionCorrection(payload);
                        if (_selfKnown && c.EntityValue == _self)
                            (_x, _y, _z) = (c.X, c.Y, c.Z);
                        return;
                    }
                case NetOpcode.TeleportToLocation:
                    {
                        var t = NetCodec.DecodeTeleportToLocation(payload);
                        if (!_selfKnown || (uint)t.ObjectId != (uint)_self)
                            return;
                        // The new town's spawns can arrive before this frame, so the gatekeeper list is kept;
                        // the old town's entries are despawned or simply out of range now.
                        (_x, _y, _z) = (t.X, t.Y, t.Z);
                        _inbox.Enqueue((opcode, payload));
                        return;
                    }
                case NetOpcode.NpcHtml:
                case NetOpcode.SystemMessage:
                case NetOpcode.Reject:
                    _inbox.Enqueue((opcode, payload));
                    return;
            }
        }
    }

    /// <summary>Behavior thread: advance one step; returns the frame to send, or null to idle this slot.</summary>
    public TravelCommand? Step(DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!_selfKnown)
                return null;
            return _phase switch
            {
                Phase.Wait => now < _until ? Drain() : Seek(now),
                Phase.Seek => Seek(now),
                Phase.Talking => Talking(now),
                Phase.Teleporting => Teleporting(now),
                _ => null
            };
        }
    }

    TravelCommand? Drain()
    {
        _inbox.Clear();
        return null;
    }

    TravelCommand? Seek(DateTimeOffset now)
    {
        _inbox.Clear();
        float speed = Math.Max(_runSpeed, MinRunSpeed);
        var near = _visibleGatekeepers.Values
            .OrderBy(g => GatekeeperDirectory.DistSq(g.X, g.Y, _x, _y))
            .FirstOrDefault();
        // Talk only once inside the server's interact radius. Talking from 2500 units away is refused with
        // "range" and the retry loop used to re-enter Seek at the same spot, so the bot never actually walked
        // (0 walk hops, one refused talk per tick). Mobius walks to the NPC first, then interacts.
        if (near.ObjectId != 0 && GatekeeperDirectory.Within(near.X, near.Y, _x, _y, SightRange))
        {
            double dist = Math.Sqrt(GatekeeperDirectory.DistSq(near.X, near.Y, _x, _y));
            if (dist <= TalkRange)
            {
                _talkObjectId = near.ObjectId;
                _talkNpcId = near.NpcId;
                _pages = 0;
                _followed.Clear();
                _pending.Clear();
                _phase = Phase.Talking;
                _until = now + TalkSlack;
                Stats.Talks++;
                return new TravelCommand(
                    NetOpcode.NpcTalk,
                    NetCodec.EncodeNpcTalk(new NpcTalkNetMsg { NpcObjectId = near.ObjectId }),
                    $"talk npc={near.NpcId}");
            }

            var walk = WalkToward(near.X, near.Y, near.Z, speed, now);
            if (walk != null)
                return walk;
            // Standing on the NPC and still out of the 3D gate: the height differs, retry the talk and let the
            // server answer instead of spinning in Seek.
            _talkObjectId = near.ObjectId;
            _talkNpcId = near.NpcId;
            _pages = 0;
            _followed.Clear();
            _pending.Clear();
            _phase = Phase.Talking;
            _until = now + TalkSlack;
            Stats.Talks++;
            return new TravelCommand(
                NetOpcode.NpcTalk,
                NetCodec.EncodeNpcTalk(new NpcTalkNetMsg { NpcObjectId = near.ObjectId }),
                $"talk npc={near.NpcId}");
        }

        // Walk toward the nearest gatekeeper in sight, else the nearest one in the datapack, one hop at a time.
        var target = near.ObjectId != 0
            ? new GatekeeperSpawn(near.NpcId, near.X, near.Y, near.Z)
            : _directory.Nearest(_x, _y);
        if (near.ObjectId == 0 && GatekeeperDirectory.Within(target.X, target.Y, _x, _y, TalkRange))
        {
            return Unexplained(now,
                $"gatekeeper npc={target.NpcId} spawned at {target.X},{target.Y} is not in the knownlist at {_x},{_y}");
        }

        double ddx = target.X - _x;
        double ddy = target.Y - _y;
        double len = Math.Sqrt(ddx * ddx + ddy * ddy);
        if (len <= TalkRange)
        {
            return Unexplained(now,
                $"gatekeeper npc={target.NpcId} at {target.X},{target.Y} not visible from {_x},{_y} inside the talk radius");
        }

        return WalkToward(target.X, target.Y, target.Z, speed, now);
    }

    /// <summary>
    /// One walk frame toward <paramref name="tx"/>,<paramref name="ty"/>. Stops at <see cref="TalkRange"/> (and
    /// never overshoots), so the next Seek talks instead of walking into the NPC column.
    /// </summary>
    TravelCommand? WalkToward(int tx, int ty, int tz, float speed, DateTimeOffset now)
    {
        double dx = tx - _x;
        double dy = ty - _y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len <= TalkRange)
            return null;

        double stop = Math.Max(1, len - TalkRange + 50); // aim just inside the gate
        double step = Math.Min(WalkHop, stop);
        int destX = _x + (int)Math.Round(dx / len * step);
        int destY = _y + (int)Math.Round(dy / len * step);
        if (destX == _x && destY == _y)
            return null;

        _phase = Phase.Wait;
        _until = now + TimeSpan.FromSeconds(step / speed) + TimeSpan.FromMilliseconds(500);
        Stats.WalkHops++;
        return new TravelCommand(
            NetOpcode.MoveInput,
            NetCodec.EncodeMoveInput(new MoveInputMsg { DestX = destX, DestY = destY, DestZ = tz }),
            $"walk toward {tx},{ty}");
    }

    TravelCommand? Talking(DateTimeOffset now)
    {
        while (_inbox.TryDequeue(out var frame))
        {
            switch (frame.Opcode)
            {
                case NetOpcode.Reject:
                    {
                        var r = NetCodec.DecodeReject(frame.Payload);
                        if (r.Code == "range")
                        {
                            // The server's 3D gate refused: our 2D distance said "close enough" but the height
                            // differs (elevated platform, stairs). Walk to the NPC column, then talk again.
                            Stats.TalkRangeRetries++;
                            _phase = Phase.Seek;
                            return Seek(now);
                        }

                        return Unexplained(now, $"talk npc={_talkNpcId} reject {r.Code}: {r.Message}");
                    }
                case NetOpcode.SystemMessage:
                    if (Refusal(frame.Payload, now, out var refused))
                        return refused;
                    break;
                case NetOpcode.NpcHtml:
                    {
                        var page = NetCodec.DecodeNpcHtml(frame.Payload);
                        if (page.NpcObjectId != _talkObjectId)
                            break;
                        return OnPage(page.Html, now);
                    }
            }
        }

        return now >= _until
            ? Unexplained(now, $"talk npc={_talkNpcId} no dialog after {_pages} page(s)")
            : null;
    }

    TravelCommand? OnPage(string html, DateTimeOffset now)
    {
        _pages++;
        var links = GatekeeperHtml.Links(html);
        var towns = links
            .Where(l => l.Kind == GatekeeperLinkKind.Teleport)
            .Select(l => _directory.TryDestination(_talkNpcId, l.List, l.Id, out var d) ? d : (TeleportDestination?)null)
            .Where(d => d is { } t && _directory.IsTown(t.X, t.Y) &&
                        !GatekeeperDirectory.Within(t.X, t.Y, _x, _y, GatekeeperDirectory.TownRadius))
            .Select(d => d!.Value)
            .ToList();
        if (towns.Count > 0)
        {
            _expected = towns[_rng.Next(towns.Count)];
            _phase = Phase.Teleporting;
            _until = now + TeleportTimeout;
            Stats.Attempted++;
            return Bypass($"teleport {_expected.List} {_expected.Id}");
        }

        foreach (var link in links.Where(l => l.Kind != GatekeeperLinkKind.Teleport))
        {
            if (!_followed.Contains(link.Command) && !_pending.Any(p => p.Command.Equals(link.Command, StringComparison.OrdinalIgnoreCase)))
                _pending.Add(link);
        }

        // showTeleports opens the list directly; Chat pages are searched in the order they were offered.
        int pick = _pending.FindIndex(l => l.Kind == GatekeeperLinkKind.ShowTeleports);
        if (pick < 0 && _pending.Count > 0)
            pick = 0;
        if (pick < 0 || _pages >= MaxPagesPerTalk)
        {
            return Unexplained(now,
                $"npc={_talkNpcId} offers no town teleport after {_pages} page(s) (visited: {string.Join(", ", _followed)})");
        }

        var next = _pending[pick];
        _pending.RemoveAt(pick);
        _followed.Add(next.Command);
        _until = now + PageTimeout;
        return Bypass(next.Command);
    }

    TravelCommand? Teleporting(DateTimeOffset now)
    {
        while (_inbox.TryDequeue(out var frame))
        {
            switch (frame.Opcode)
            {
                case NetOpcode.Reject:
                    {
                        var r = NetCodec.DecodeReject(frame.Payload);
                        return Unexplained(now, $"teleport npc={_talkNpcId} -> {_expected.Name} reject {r.Code}: {r.Message}");
                    }
                case NetOpcode.SystemMessage:
                    if (Refusal(frame.Payload, now, out var refused))
                        return refused;
                    break;
                case NetOpcode.TeleportToLocation:
                    {
                        var t = NetCodec.DecodeTeleportToLocation(frame.Payload);
                        if (!GatekeeperDirectory.Within(t.X, t.Y, _expected.X, _expected.Y, ArrivalTolerance))
                        {
                            return Unexplained(now,
                                $"teleport npc={_talkNpcId} -> {_expected.Name} landed at {t.X},{t.Y},{t.Z}, expected {_expected.X},{_expected.Y},{_expected.Z}");
                        }

                        Stats.Succeeded++;
                        _phase = Phase.Wait;
                        _until = now + _tripCooldown;
                        return null;
                    }
            }
        }

        return now >= _until
            ? Unexplained(now, $"teleport npc={_talkNpcId} -> {_expected.Name}: no TeleportToLocation in {TeleportTimeout.TotalSeconds:0}s")
            : null;
    }

    bool Refusal(byte[] payload, DateTimeOffset now, out TravelCommand? next)
    {
        next = null;
        var msg = NetCodec.DecodeSystemMessage(payload);
        if (!NpcServiceProtocol.TryTeleportRefusalCode(msg.Id, out string code))
            return false;
        Stats.Rejected(code);
        _phase = Phase.Wait;
        _until = now + FailureBackoff;
        return true;
    }

    TravelCommand? Unexplained(DateTimeOffset now, string detail)
    {
        Stats.Fail(detail);
        _phase = Phase.Wait;
        _until = now + FailureBackoff;
        return null;
    }

    TravelCommand Bypass(string command) => new(
        NetOpcode.BypassRequest,
        NetCodec.EncodeBypassRequest(new BypassRequestNetMsg
        {
            NpcObjectId = _talkObjectId,
            Command = $"npc_{_talkObjectId}_{command}"
        }),
        $"bypass npc={_talkNpcId} {command}");
}
