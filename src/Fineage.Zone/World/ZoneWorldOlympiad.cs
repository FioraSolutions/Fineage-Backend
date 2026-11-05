using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Olympiad;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public OlympiadBoard Olympiad => _olympiad;

    /// <summary>
    /// Combat hook. <paramref name="winnerObjectId"/> 0 is a draw (the HP-less tie).
    /// A participant id is a decided win and grants the Noblesse Gate Pass.
    /// Packets from this call go out on the next simulation tick.
    /// </summary>
    public string? ReportOlympiadResult(int matchId, int winnerObjectId, DateTimeOffset? now = null)
    {
        lock (_gate)
        {
            var step = _olympiad.Report(matchId, winnerObjectId, (now ?? _clock()).ToUnixTimeMilliseconds());
            if (step.Error != null)
                return step.Error;
            ApplyOlympiad(step, _olympiadHeld);
            return null;
        }
    }

    public bool GrantNoblesse(int characterId)
    {
        lock (_gate)
            return _olympiad.GrantNoblesse(characterId);
    }

    public async Task AdvanceOlympiadAsync(CancellationToken ct = default)
    {
        var outbound = new List<Pending>();
        lock (_gate)
            TickOlympiad(outbound);
        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
    }

    public async Task<string?> TryOlympiadStatusAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        var outbound = new List<Pending>();
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            var step = _olympiad.Advance(now, LookupOlympiad);
            ApplyOlympiad(step, outbound);
            if (!step.RefreshAll && !step.RefreshStatus.Contains(session.ObjectId))
                QueueOlympiadStatus(session, now, outbound);
        }

        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryOlympiadRegisterAsync(ZoneSession session, OlympiadRegisterRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return "bad_request";
        if (!IsLive(session))
            return "world";
        return await RunOlympiadAsync(session, ct, now =>
        {
            var player = _byId[session.ObjectId];
            return _olympiad.Register(ActorFor(player), request.Classed == 1, now);
        }).ConfigureAwait(false);
    }

    public async Task<string?> TryOlympiadUnregisterAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        return await RunOlympiadAsync(session, ct, now =>
            _olympiad.Unregister(ActorFor(_byId[session.ObjectId]), now)).ConfigureAwait(false);
    }

    public async Task<string?> TryOlympiadLeaveAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        return await RunOlympiadAsync(session, ct, now =>
            _olympiad.Leave(session.ObjectId, now, LookupOlympiad)).ConfigureAwait(false);
    }

    public async Task<string?> TryAdminReportOlympiadAsync(ZoneSession session, int matchId, int winnerObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
        {
            var step = _olympiad.Report(matchId, winnerObjectId, _clock().ToUnixTimeMilliseconds());
            error = step.Error;
            if (error == null)
                ApplyOlympiad(step, outbound);
        }

        if (error == null)
            await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return error;
    }

    void TickOlympiad(List<Pending> outbound)
    {
        var step = _olympiad.Advance(_clock().ToUnixTimeMilliseconds(), LookupOlympiad, announce: false);
        ApplyOlympiad(step, outbound);
        if (_olympiadHeld.Count > 0)
        {
            outbound.AddRange(_olympiadHeld);
            _olympiadHeld.Clear();
        }
    }

    void NoteOlympiadDisconnect(ZoneEntity entity, List<Pending> outbound)
    {
        if (entity.Kind != ZoneEntityKind.Player)
            return;
        var step = _olympiad.Disconnect(entity.ObjectId, _clock().ToUnixTimeMilliseconds(), LookupOlympiad);
        ApplyOlympiad(step, outbound);
    }

    async Task<string?> RunOlympiadAsync(ZoneSession session, CancellationToken ct, Func<long, OlympiadStep> action)
    {
        var outbound = new List<Pending>();
        string? error;
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            var step = action(now);
            error = step.Error;
            ApplyOlympiad(step, outbound);
        }

        await FlushOthersAsync(outbound, ct).ConfigureAwait(false);
        return error;
    }

    void ApplyOlympiad(OlympiadStep step, List<Pending> outbound)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var notice in step.Notices)
            QueueNotice(notice, outbound);
        foreach (var move in step.Moves)
            QueueMove(move, outbound);
        foreach (var grant in step.Grants)
            QueueGrant(grant, outbound);
        foreach (var match in step.Matches)
            QueueMatch(match, outbound);
        foreach (var countdown in step.Countdowns)
            QueueCountdown(countdown, outbound);
        foreach (var result in step.Results)
            QueueResult(result, outbound);

        if (step.RefreshAll)
        {
            foreach (var player in Players())
            {
                if (player.Session != null)
                    QueueOlympiadStatus(player.Session, now, outbound);
            }
        }
        else
        {
            foreach (int id in step.RefreshStatus)
            {
                if (_byId.TryGetValue(id, out var player) && player.Session != null)
                    QueueOlympiadStatus(player.Session, now, outbound);
            }
        }
    }

    void QueueMove(OlympiadMove move, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(move.CharacterId, out var entity) || entity.Session == null)
            return;
        int savedX = entity.X;
        int savedY = entity.Y;
        int savedZ = entity.Z;
        var toSelf = new List<Pending>();
        var toOthers = new List<Pending>();
        Relocate(entity, move.X, move.Y, move.Z, toSelf, toOthers, move.InstanceId, move.Persist);
        if (!move.Persist)
            SavePresence(entity.Session, savedX, savedY, savedZ, touchAccess: false);
        outbound.AddRange(toSelf);
        outbound.AddRange(toOthers);
    }

    void QueueGrant(OlympiadGrant grant, List<Pending> outbound)
    {
        if (!_byId.TryGetValue(grant.CharacterId, out var entity) || entity.Session == null || entity.Inventory == null)
            return;
        if (!_game.Items.TryGet(grant.ItemId, out _))
            return;
        var added = entity.Inventory.Add(grant.ItemId, grant.Count);
        var update = InventoryMessages.Update(InventoryChange.Add, added);
        outbound.Add(Pack(entity.Session, MsgType.InventoryUpdate, update));
        // The Gate Pass lands in the bag, so Net clients need the same row the V2 frame carries.
        NetBridge?.FanoutInventoryUpdate([entity.ObjectId], ToNetInventoryUpdate(update));
        SaveInventory(entity.Session, entity.Inventory);
    }

    void QueueNotice(OlympiadNotice notice, List<Pending> outbound)
    {
        var message = new SystemMessageMsg
        {
            Id = notice.MessageId,
            Params = notice.Params,
            Text = notice.Text
        };
        if (notice.CharacterId == 0)
        {
            var live = new List<int>();
            foreach (var player in Players())
            {
                if (player.Session != null)
                {
                    outbound.Add(Pack(player.Session, MsgType.SystemMessage, message));
                    live.Add(player.ObjectId);
                }
            }
            if (live.Count > 0)
                NetBridge?.FanoutSystemMessage(live, ToNetOlympiadLine(message));
            return;
        }

        if (_byId.TryGetValue(notice.CharacterId, out var target) && target.Session != null)
        {
            outbound.Add(Pack(target.Session, MsgType.SystemMessage, message));
            NetBridge?.FanoutSystemMessage([target.ObjectId], ToNetOlympiadLine(message));
        }
    }

    void QueueMatch(OlympiadMatchNote note, List<Pending> outbound)
    {
        var message = new OlympiadMatchMsg
        {
            MatchId = note.MatchId,
            StadiumId = note.StadiumId,
            ZoneId = note.ZoneId,
            Classed = note.Classed,
            Phase = note.Phase,
            PlayerOneId = note.PlayerOneId,
            PlayerTwoId = note.PlayerTwoId,
            PlayerOneName = note.PlayerOneName,
            PlayerTwoName = note.PlayerTwoName,
            InstanceId = note.InstanceId
        };
        SendBoth(note.PlayerOneId, note.PlayerTwoId, MsgType.OlympiadMatch, message, outbound);
        FanOlympiadBoth(note.PlayerOneId, note.PlayerTwoId, match: new Fineage.Net.Contract.OlympiadMatchNetMsg
        {
            MatchId = message.MatchId,
            StadiumId = message.StadiumId,
            ZoneId = message.ZoneId,
            Classed = message.Classed,
            Phase = message.Phase,
            PlayerOneId = message.PlayerOneId,
            PlayerTwoId = message.PlayerTwoId,
            PlayerOneName = message.PlayerOneName,
            PlayerTwoName = message.PlayerTwoName,
            InstanceId = message.InstanceId
        });
    }

    void QueueCountdown(OlympiadCountdownNote note, List<Pending> outbound)
    {
        var packet = new OlympiadCountdownMsg
        {
            MatchId = note.MatchId,
            Kind = note.Kind,
            Seconds = note.Seconds
        };
        var line = new SystemMessageMsg
        {
            Id = OlympiadProtocol.CountdownLine,
            Params = [note.Kind.ToString(), note.Seconds.ToString()],
            Text = OlympiadLines.CountdownText(note.Kind, note.Seconds)
        };
        SendBoth(note.PlayerOneId, note.PlayerTwoId, MsgType.OlympiadCountdown, packet, outbound);
        FanOlympiadBoth(note.PlayerOneId, note.PlayerTwoId, countdown: new Fineage.Net.Contract.OlympiadCountdownNetMsg
        {
            MatchId = packet.MatchId,
            Kind = packet.Kind,
            Seconds = packet.Seconds
        });
        SendBoth(note.PlayerOneId, note.PlayerTwoId, MsgType.SystemMessage, line, outbound);
        FanOlympiadBoth(note.PlayerOneId, note.PlayerTwoId, line: ToNetOlympiadLine(line));
    }

    void QueueResult(OlympiadResultNote note, List<Pending> outbound)
    {
        var message = new OlympiadResultMsg
        {
            MatchId = note.MatchId,
            Outcome = note.Outcome,
            WinnerObjectId = note.WinnerObjectId,
            PointsOne = note.PointsOne,
            PointsTwo = note.PointsTwo,
            DeltaOne = note.DeltaOne,
            DeltaTwo = note.DeltaTwo,
            RewardItemId = note.RewardItemId,
            RewardCount = note.RewardCount,
            CompetitionsDoneOne = note.CompetitionsDoneOne,
            CompetitionsDoneTwo = note.CompetitionsDoneTwo
        };
        SendBoth(note.PlayerOneId, note.PlayerTwoId, MsgType.OlympiadResult, message, outbound);
        FanOlympiadBoth(note.PlayerOneId, note.PlayerTwoId, result: new Fineage.Net.Contract.OlympiadResultNetMsg
        {
            MatchId = message.MatchId,
            Outcome = message.Outcome,
            WinnerObjectId = message.WinnerObjectId,
            PointsOne = message.PointsOne,
            PointsTwo = message.PointsTwo,
            DeltaOne = message.DeltaOne,
            DeltaTwo = message.DeltaTwo,
            RewardItemId = message.RewardItemId,
            RewardCount = message.RewardCount,
            CompetitionsDoneOne = message.CompetitionsDoneOne,
            CompetitionsDoneTwo = message.CompetitionsDoneTwo
        });
    }

    void QueueOlympiadStatus(ZoneSession session, long nowUnixMs, List<Pending> outbound)
    {
        var view = _olympiad.View(session.ObjectId, nowUnixMs);
        var message = new OlympiadStatusMsg
        {
            Period = view.Period,
            Cycle = view.Cycle,
            InCompetition = view.InCompetition,
            CompetitionStartUnixMs = view.CompetitionStartUnixMs,
            CompetitionEndUnixMs = view.CompetitionEndUnixMs,
            OlympiadEndUnixMs = view.OlympiadEndUnixMs,
            ValidationEndUnixMs = view.ValidationEndUnixMs,
            NextWeeklyUnixMs = view.NextWeeklyUnixMs,
            RegistrationOpen = view.RegistrationOpen,
            Noble = view.Noble,
            Points = view.Points,
            CompetitionsDone = view.CompetitionsDone,
            Won = view.Won,
            Lost = view.Lost,
            Drawn = view.Drawn,
            Queue = view.Queue,
            MatchId = view.MatchId,
            StadiumId = view.StadiumId,
            ClassId = view.ClassId
        };
        outbound.Add(Pack(session, MsgType.OlympiadStatus, message));
        NetBridge?.FanoutOlympiadStatus([session.ObjectId], ToNetOlympiadStatus(message));
    }

    static Fineage.Net.Contract.OlympiadStatusNetMsg ToNetOlympiadStatus(OlympiadStatusMsg s) => new()
    {
        Period = s.Period,
        Cycle = s.Cycle,
        InCompetition = s.InCompetition,
        CompetitionStartUnixMs = s.CompetitionStartUnixMs,
        CompetitionEndUnixMs = s.CompetitionEndUnixMs,
        OlympiadEndUnixMs = s.OlympiadEndUnixMs,
        ValidationEndUnixMs = s.ValidationEndUnixMs,
        NextWeeklyUnixMs = s.NextWeeklyUnixMs,
        RegistrationOpen = s.RegistrationOpen,
        Noble = s.Noble,
        Points = s.Points,
        CompetitionsDone = s.CompetitionsDone,
        Won = s.Won,
        Lost = s.Lost,
        Drawn = s.Drawn,
        Queue = s.Queue,
        MatchId = s.MatchId,
        StadiumId = s.StadiumId,
        ClassId = s.ClassId
    };

    static Fineage.Net.Contract.SystemMessageNetMsg ToNetOlympiadLine(SystemMessageMsg line) => new()
    {
        Id = line.Id,
        Text = line.Text,
        Params = line.Params
    };

    /// <summary>Both match participants get one Net message per event, exactly as <c>SendBoth</c> does for V2.</summary>
    void FanOlympiadBoth(
        int one,
        int two,
        Fineage.Net.Contract.OlympiadMatchNetMsg? match = null,
        Fineage.Net.Contract.OlympiadCountdownNetMsg? countdown = null,
        Fineage.Net.Contract.OlympiadResultNetMsg? result = null,
        Fineage.Net.Contract.SystemMessageNetMsg? line = null)
    {
        if (NetBridge == null)
            return;
        var watchers = new List<int>(2);
        if (_byId.TryGetValue(one, out var first) && first.Session is { InWorld: true })
            watchers.Add(one);
        if (two != one && _byId.TryGetValue(two, out var second) && second.Session is { InWorld: true })
            watchers.Add(two);
        if (watchers.Count == 0)
            return;
        if (match != null)
            NetBridge.FanoutOlympiadMatch(watchers, match);
        if (countdown != null)
            NetBridge.FanoutOlympiadCountdown(watchers, countdown);
        if (result != null)
            NetBridge.FanoutOlympiadResult(watchers, result);
        if (line != null)
            NetBridge.FanoutSystemMessage(watchers, line);
    }

    void SendBoth<T>(int one, int two, MsgType type, T message, List<Pending> outbound)
    {
        if (_byId.TryGetValue(one, out var first) && first.Session != null)
            outbound.Add(Pack(first.Session, type, message));
        if (two != one && _byId.TryGetValue(two, out var second) && second.Session != null)
            outbound.Add(Pack(second.Session, type, message));
    }

    OlympiadPresence? LookupOlympiad(int characterId)
    {
        if (!_byId.TryGetValue(characterId, out var entity) || entity.Kind != ZoneEntityKind.Player || entity.Session is not { InWorld: true })
            return null;
        return new OlympiadPresence(
            true,
            entity.Dead,
            entity.ClassId,
            entity.ClassId,
            entity.X,
            entity.Y,
            entity.Z,
            entity.Inventory?.Items.Count ?? 0,
            InventoryLimit(entity),
            HasCursedWeapon(entity.Inventory));
    }

    static OlympiadActor ActorFor(ZoneEntity player) => new(
        player.ObjectId,
        player.Name,
        player.ClassId,
        player.ClassId,
        player.Inventory?.Items.Count ?? 0,
        InventoryLimit(player),
        HasCursedWeapon(player.Inventory));

    static int InventoryLimit(ZoneEntity player) =>
        player.Race == OlympiadProtocol.DwarfRace ? OlympiadProtocol.DwarfInventoryLimit : OlympiadProtocol.InventoryLimit;

    static bool HasCursedWeapon(PlayerInventory? inventory) =>
        inventory != null &&
        (inventory.CountOf(OlympiadProtocol.Zariche) > 0 || inventory.CountOf(OlympiadProtocol.Akamanah) > 0);

    void CollectVisible(ZoneEntity self, HashSet<int> into)
    {
        into.Clear();
        foreach (int id in _aoi.GetVisible(self.ObjectId))
        {
            if (_byId.TryGetValue(id, out var other) && other.InstanceId == self.InstanceId)
                into.Add(id);
        }
    }

    bool SameInstance(int leftId, int rightId)
    {
        if (!_byId.TryGetValue(leftId, out var left) || !_byId.TryGetValue(rightId, out var right))
            return false;
        return left.InstanceId == right.InstanceId;
    }
}
