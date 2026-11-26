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
    /// Where the bot stops walking and talks. Mobius <c>Npc.INTERACTION_DISTANCE</c> (250) is a hard 3D gate on both
    /// <c>NpcTalk</c> and every <c>bypass</c> link (<c>Npc.canInteract</c>), so talking from further out gets a "range"
    /// refusal and no dialog. Kept well inside it, and measured in 3D: a gatekeeper on a ledge is inside 250 XY but
    /// hundreds of units away once the height is counted, which is exactly the refusal a flat check never predicts.
    /// </summary>
    public const int ApproachRange = 200;
    /// <summary>A gatekeeper this close must be in the knownlist (AOI 3×3 cells of 3000).</summary>
    public const int SightRange = 6000;
    public const int WalkHop = 1500;
    /// <summary>Slower than any Interlude run speed, so a hop is never re-issued before the pawn arrives.</summary>
    public const float MinRunSpeed = 80f;
    public const int MaxPagesPerTalk = 6;
    /// <summary>Hops without any progress toward the walk target before the travel is reported as unexplained.</summary>
    public const int MaxStalledHops = 8;
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
    /// <summary>Interpolated own position, the way the Interlude client animates a MoveToLocation leg.</summary>
    int _x, _y, _z;
    /// <summary>Live leg: the server's last <c>MoveState</c> origin/dest, walked at run speed.</summary>
    int _legFromX, _legFromY, _legFromZ, _legToX, _legToY, _legToZ;
    DateTimeOffset _legStarted;
    bool _legActive;
    float _runSpeed;
    int _talkObjectId;
    int _talkNpcId;
    int _pages;
    TeleportDestination _expected;
    /// <summary>Target of the current walk, used to detect a hop that no longer closes the distance.</summary>
    int _walkTargetNpcId = -1;
    double _walkBestDist3d = double.MaxValue;
    int _walkStalledHops;

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
                        {
                            // The zone re-acknowledges a leg only while the pawn stays inside one AOI cell. Use it when
                            // it comes (it may be truncated by geo), but never wait for it: see BeginLeg.
                            _legFromX = m.OriginX;
                            _legFromY = m.OriginY;
                            _legFromZ = m.OriginZ;
                            _legToX = m.DestX;
                            _legToY = m.DestY;
                            _legToZ = m.DestZ;
                            _legStarted = DateTimeOffset.UtcNow;
                            _legActive = true;
                            (_x, _y, _z) = (m.OriginX, m.OriginY, m.OriginZ);
                        }

                        return;
                    }
                case NetOpcode.PositionCorrection:
                    {
                        var c = NetCodec.DecodePositionCorrection(payload);
                        if (_selfKnown && c.EntityValue == _self)
                        {
                            (_x, _y, _z) = (c.X, c.Y, c.Z);
                            _legActive = false;
                        }

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
                        _legActive = false;
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
            AdvanceLeg(now);
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

    /// <summary>
    /// Start walking a leg the way the client does. The zone only acknowledges a leg with <c>MoveState</c> while the
    /// pawn stays inside one AOI cell, so the bot cannot wait for an ack: it interpolates from the last known origin
    /// at its run speed and lets a later <c>MoveState</c> / <c>PositionCorrection</c> / <c>TeleportToLocation</c>
    /// resync it. Geo-truncated destinations are then discovered, not assumed away.
    /// </summary>
    void BeginLeg(int destX, int destY, int destZ, DateTimeOffset now)
    {
        _legFromX = _x;
        _legFromY = _y;
        _legFromZ = _z;
        _legToX = destX;
        _legToY = destY;
        _legToZ = destZ;
        _legStarted = now;
        _legActive = true;
    }

    /// <summary>Walk the current leg at the live run speed, exactly like the client.</summary>
    void AdvanceLeg(DateTimeOffset now)
    {
        if (!_legActive)
            return;
        float speed = Math.Max(_runSpeed, MinRunSpeed);
        double elapsed = (now - _legStarted).TotalSeconds;
        if (elapsed <= 0)
            return;
        double dx = _legToX - _legFromX, dy = _legToY - _legFromY, dz = _legToZ - _legFromZ;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double travelled = speed * elapsed;
        if (len < 1 || travelled >= len)
        {
            (_x, _y, _z) = (_legToX, _legToY, _legToZ);
            _legActive = false;
            return;
        }

        double k = travelled / len;
        _x = _legFromX + (int)Math.Round(dx * k);
        _y = _legFromY + (int)Math.Round(dy * k);
        _z = _legFromZ + (int)Math.Round(dz * k);
    }

    TravelCommand? Seek(DateTimeOffset now)
    {
        _inbox.Clear();
        float speed = Math.Max(_runSpeed, MinRunSpeed);
        var near = _visibleGatekeepers.Values
            .OrderBy(g => GatekeeperDirectory.DistSq(g.X, g.Y, _x, _y))
            .FirstOrDefault();
        if (near.ObjectId != 0 && InTalkReach(near.X, near.Y, near.Z))
        {
            double dist = Math.Sqrt(GatekeeperDirectory.DistSq(near.X, near.Y, _x, _y));
            _talkObjectId = near.ObjectId;
            _talkNpcId = near.NpcId;
            _pages = 0;
            _followed.Clear();
            _pending.Clear();
            _phase = Phase.Talking;
            _until = now + TimeSpan.FromSeconds(dist / speed) + TalkSlack;
            Stats.Talks++;
            return new TravelCommand(
                NetOpcode.NpcTalk,
                NetCodec.EncodeNpcTalk(new NpcTalkNetMsg { NpcObjectId = near.ObjectId }),
                $"talk npc={near.NpcId}");
        }

        // Walk toward the nearest gatekeeper in sight, else the nearest one in the datapack, one hop at a time.
        bool inSight = near.ObjectId != 0 && GatekeeperDirectory.Within(near.X, near.Y, _x, _y, SightRange);
        var target = inSight
            ? new GatekeeperSpawn(near.NpcId, near.X, near.Y, near.Z)
            : _directory.Nearest(_x, _y);
        if (!inSight && GatekeeperDirectory.Within(target.X, target.Y, _x, _y, SightRange) && InTalkReach(target.X, target.Y, target.Z))
        {
            return Unexplained(now,
                $"gatekeeper npc={target.NpcId} spawned at {target.X},{target.Y} is not in the knownlist at {_x},{_y}");
        }

        double dx = target.X - _x;
        double dy = target.Y - _y;
        double dz = target.Z - _z;
        double len = Math.Sqrt(dx * dx + dy * dy);
        // Mobius moveToLocation folds the height into the offset (offset -= |dz|), so a gatekeeper on a ledge keeps
        // the pawn walking underneath it and the ground does the climbing. Stopping at the flat circle left the pawn
        // 475 units away in 3D and every talk was refused for range.
        double dist3d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (TrackWalkProgress(target.NpcId, dist3d) is { } stalled)
            return stalled;
        double step = Math.Max(1, Math.Min(WalkHop, dist3d - ApproachRange / 2.0));
        int destX = len < 1 ? _x : _x + (int)(dx / len * Math.Min(step, len));
        int destY = len < 1 ? _y : _y + (int)(dy / len * Math.Min(step, len));
        _phase = Phase.Wait;
        _until = now + TimeSpan.FromSeconds(step / speed) + TimeSpan.FromMilliseconds(500);
        Stats.WalkHops++;
        return WalkTo(destX, destY, target.Z, now);
    }

    /// <summary>
    /// A hop that does not close the distance is a walk that geo refuses (a wall between the pawn and the
    /// gatekeeper). Repeating it forever is what a soak must never do: report it instead of spinning.
    /// </summary>
    TravelCommand? TrackWalkProgress(int npcId, double dist3d)
    {
        if (_walkTargetNpcId != npcId || dist3d < _walkBestDist3d - 1)
        {
            _walkTargetNpcId = npcId;
            _walkBestDist3d = dist3d;
            _walkStalledHops = 0;
            return null;
        }

        if (++_walkStalledHops < MaxStalledHops)
            return null;

        _walkStalledHops = 0;
        _walkBestDist3d = double.MaxValue;
        return Unexplained(DateTimeOffset.UtcNow,
            $"cannot close on gatekeeper npc={npcId}: still {dist3d:0} units away at {_x},{_y},{_z} after {MaxStalledHops} hops");
    }

    TravelCommand WalkTo(int destX, int destY, int destZ, DateTimeOffset now)
    {
        BeginLeg(destX, destY, destZ, now);
        return new TravelCommand(
            NetOpcode.MoveInput,
            NetCodec.EncodeMoveInput(new MoveInputMsg { DestX = destX, DestY = destY, DestZ = destZ }),
            $"walk toward npc={_walkTargetNpcId}");
    }

    /// <summary>
    /// One walk hop toward the gatekeeper that just refused for range, keeping it as the talk target so the next
    /// dialog attempt happens from inside the interaction sphere.
    /// </summary>
    TravelCommand? WalkTowardTalkTarget(DateTimeOffset now)
    {
        if (!_visibleGatekeepers.TryGetValue((ulong)_talkObjectId, out var gk))
            return Seek(now);

        double dx = gk.X - _x;
        double dy = gk.Y - _y;
        double dz = gk.Z - _z;
        double len = Math.Sqrt(dx * dx + dy * dy);
        float speed = Math.Max(_runSpeed, MinRunSpeed);
        double dist3d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (TrackWalkProgress(gk.NpcId, dist3d) is { } stalled)
            return stalled;
        double step = Math.Max(1, Math.Min(WalkHop, dist3d - ApproachRange / 2.0));
        int destX = len < 1 ? _x : _x + (int)(dx / len * Math.Min(step, len));
        int destY = len < 1 ? _y : _y + (int)(dy / len * Math.Min(step, len));
        _until = now + TimeSpan.FromSeconds(step / speed) + TimeSpan.FromMilliseconds(500);
        Stats.WalkHops++;
        _walkTargetNpcId = gk.NpcId;
        return WalkTo(destX, destY, gk.Z, now);
    }

    /// <summary>
    /// Mobius <c>Npc.canInteract</c> / <c>canTalk</c> is a 3D sphere at <c>INTERACTION_DISTANCE</c>, not a flat circle:
    /// a gatekeeper standing on a platform can be 250 units away in XY and refused because of the height.
    /// </summary>
    bool InTalkReach(int x, int y, int z)
    {
        long dx = x - _x, dy = y - _y, dz = z - _z;
        return dx * dx + dy * dy + dz * dz <= (long)ApproachRange * ApproachRange;
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
                            // The server's interaction sphere refused: keep the target and walk into it instead of
                            // re-issuing the same talk from the same spot on the next slot.
                            Stats.TalkRangeRetries++;
                            _phase = Phase.Wait;
                            var approach = WalkTowardTalkTarget(now);
                            return approach;
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
                        _legActive = false;
                        _walkBestDist3d = double.MaxValue;
                        _walkStalledHops = 0;
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
