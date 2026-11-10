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
            ApplyOlympiad(step);
            return null;
        }
    }

    public bool GrantNoblesse(int characterId)
    {
        lock (_gate)
            return _olympiad.GrantNoblesse(characterId);
    }

    public Task AdvanceOlympiadAsync(CancellationToken ct = default)
    {
        lock (_gate)
            TickOlympiad();
        return Task.CompletedTask;
    }

    public Task<string?> TryOlympiadStatusAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        lock (_gate)
        {
            long now = _clock().ToUnixTimeMilliseconds();
            var step = _olympiad.Advance(now, LookupOlympiad);
            ApplyOlympiad(step);
            if (!step.RefreshAll && !step.RefreshStatus.Contains(session.ObjectId))
                QueueOlympiadStatus(session, now);
        }

        return Task.FromResult<string?>(null);
    }

    public Task<string?> TryOlympiadRegisterAsync(ZoneSession session, OlympiadRegisterRequestMsg request, CancellationToken ct)
    {
        if (request == null)
            return Task.FromResult<string?>("bad_request");
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        return RunOlympiadAsync(now =>
        {
            var player = _byId[session.ObjectId];
            return _olympiad.Register(ActorFor(player), request.Classed == 1, now);
        });
    }

    public Task<string?> TryOlympiadUnregisterAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        return RunOlympiadAsync(now => _olympiad.Unregister(ActorFor(_byId[session.ObjectId]), now));
    }

    public Task<string?> TryOlympiadLeaveAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        return RunOlympiadAsync(now => _olympiad.Leave(session.ObjectId, now, LookupOlympiad));
    }

    public Task<string?> TryAdminReportOlympiadAsync(ZoneSession session, int matchId, int winnerObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        // Rewards may SaveInventory → flush after unlock (B8).
        return Task.FromResult(WithDeferredInventoryPersist(() =>
        {
            var step = _olympiad.Report(matchId, winnerObjectId, _clock().ToUnixTimeMilliseconds());
            if (step.Error == null)
                ApplyOlympiad(step);
            return step.Error;
        }));
    }

    void TickOlympiad()
    {
        var step = _olympiad.Advance(_clock().ToUnixTimeMilliseconds(), LookupOlympiad, announce: false);
        ApplyOlympiad(step);
    }

    void NoteOlympiadDisconnect(ZoneEntity entity)
    {
        if (entity.Kind != ZoneEntityKind.Player)
            return;
        var step = _olympiad.Disconnect(entity.ObjectId, _clock().ToUnixTimeMilliseconds(), LookupOlympiad);
        ApplyOlympiad(step);
    }

    Task<string?> RunOlympiadAsync(Func<long, OlympiadStep> action)
    {
        string? error = WithDeferredInventoryPersist(() =>
        {
            long now = _clock().ToUnixTimeMilliseconds();
            var step = action(now);
            ApplyOlympiad(step);
            return step.Error;
        });

        return Task.FromResult(error);
    }

    void ApplyOlympiad(OlympiadStep step)
    {
        long now = _clock().ToUnixTimeMilliseconds();
        foreach (var notice in step.Notices)
            QueueNotice(notice);
        foreach (var move in step.Moves)
            QueueMove(move);
        foreach (var grant in step.Grants)
            QueueGrant(grant);
        foreach (var match in step.Matches)
            QueueMatch(match);
        foreach (var countdown in step.Countdowns)
            QueueCountdown(countdown);
        foreach (var result in step.Results)
            QueueResult(result);

        if (step.RefreshAll)
        {
            foreach (var player in Players())
            {
                if (player.Session != null)
                    QueueOlympiadStatus(player.Session, now);
            }
        }
        else
        {
            foreach (int id in step.RefreshStatus)
            {
                if (_byId.TryGetValue(id, out var player) && player.Session != null)
                    QueueOlympiadStatus(player.Session, now);
            }
        }
    }

    void QueueMove(OlympiadMove move)
    {
        if (!_byId.TryGetValue(move.CharacterId, out var entity) || entity.Session == null)
            return;
        int savedX = entity.X;
        int savedY = entity.Y;
        int savedZ = entity.Z;
        Relocate(entity, move.X, move.Y, move.Z, move.InstanceId, move.Persist);
        if (!move.Persist)
            SavePresence(entity.Session, savedX, savedY, savedZ, touchAccess: false);
    }

    void QueueGrant(OlympiadGrant grant)
    {
        if (!_byId.TryGetValue(grant.CharacterId, out var entity) || entity.Session == null || entity.Inventory == null)
            return;
        if (!_game.Items.TryGet(grant.ItemId, out _))
            return;
        var added = entity.Inventory.Add(grant.ItemId, grant.Count);
        var update = InventoryMessages.Update(InventoryChange.Add, added);
        // The Gate Pass lands in the bag, so Net clients need the same row the V2 frame carries.
        NetBridge?.FanoutInventoryUpdate([entity.ObjectId], update);
        SaveInventory(entity.Session, entity.Inventory);
    }

    void QueueNotice(OlympiadNotice notice)
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
                    live.Add(player.ObjectId);
                }
            }
            if (live.Count > 0)
                NetBridge?.FanoutSystemMessage(live, message);
            return;
        }

        if (_byId.TryGetValue(notice.CharacterId, out var target) && target.Session != null)
        {
            NetBridge?.FanoutSystemMessage([target.ObjectId], message);
        }
    }

}
