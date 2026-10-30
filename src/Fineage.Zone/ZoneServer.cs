using System.Diagnostics;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Protocol.Net;
using Fineage.Zone.World;
using MessagePack;

namespace Fineage.Zone;

public sealed class ZoneServer
{
    readonly HandoffTokens _tokens;
    readonly ZoneWorld _world;
    readonly Func<DateTimeOffset> _clock;
    readonly int _maxMessagesPerSecond;
    readonly bool _adminCommands;
    int _nextSession = 1;

    public ZoneServer(HandoffTokens tokens, Func<DateTimeOffset>? clock = null)
        : this(tokens, ZoneAoiSettings.CreateWorld(), clock)
    {
    }

    public ZoneServer(HandoffTokens tokens, ZoneWorld world, Func<DateTimeOffset>? clock = null)
        : this(tokens, world, clock, null)
    {
    }

    public ZoneServer(HandoffTokens tokens, ZoneWorld world, Func<DateTimeOffset>? clock, ZoneServerOptions? options)
    {
        _tokens = tokens;
        _world = world;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _maxMessagesPerSecond = options?.MaxMessagesPerSecond ?? ZoneNetSettings.MaxMessagesPerSecond;
        _adminCommands = options?.AdminCommands ?? ZoneAdmin.Enabled;
    }

    public async Task HandleAsync(Stream stream, CancellationToken ct)
    {
        var session = new ZoneSession(stream);
        int sessionId = Interlocked.Increment(ref _nextSession);
        session.WireSessionId = sessionId;
        ZoneLog.Connect(sessionId, "");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await FrameIO.ReadAsync(stream, ct).ConfigureAwait(false);
                if (frame == null)
                    return;

                await HandleFrameAsync(session, sessionId, frame.Value.Type, frame.Value.Body, ct, rate: null).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await _world.LeaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            ZoneLog.Disconnect(session);
        }
    }

    public async Task HandleAsync(PipelineConnection connection, CancellationToken ct)
    {
        var session = new ZoneSession(connection);
        int sessionId = Interlocked.Increment(ref _nextSession);
        session.WireSessionId = sessionId;
        ZoneLog.Connect(sessionId, connection.Remote);
        var rate = new MessageRateGate(_maxMessagesPerSecond);
        using var idleWatch = new SessionIdleWatchdog(ct, TcpSessionSettings.IdleTimeout);
        CancellationToken readCt = idleWatch.Token;
        try
        {
            while (!readCt.IsCancellationRequested)
            {
                var frame = await PipeFrames.ReadV2Async(connection.Reader, readCt).ConfigureAwait(false);
                if (frame == null)
                    return;

                PipelineTelemetry.FrameIn("zone", FrameIO.HeaderSize + frame.Value.Body.Length);
                idleWatch.Touch();
                if (!await HandleFrameAsync(session, sessionId, frame.Value.Type, frame.Value.Body, readCt, rate).ConfigureAwait(false))
                    return;
            }
        }
        catch (OperationCanceledException) when (idleWatch.Token.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            session.FailureRecorded = false;
            await ErrorAsync(session, "idle_timeout", "Session timed out.", CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            await _world.LeaveAsync(session, CancellationToken.None).ConfigureAwait(false);
            ZoneLog.Disconnect(session);
        }
    }

    async Task<bool> HandleFrameAsync(ZoneSession session, int sessionId, ushort type, byte[] body, CancellationToken ct, MessageRateGate? rate)
    {
        session.LastPacketId = type;
        session.FailureRecorded = false;
        long started = Stopwatch.GetTimestamp();
        try
        {
            if (rate != null && !rate.TryConsume())
            {
                await ErrorAsync(session, "rate_limit", "Too many messages per second.", ct).ConfigureAwait(false);
                return false;
            }

            await DispatchAsync(session, sessionId, type, body, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!session.FailureRecorded)
            {
                session.FailureRecorded = true;
                PipelineTelemetry.PacketErrored("zone");
            }

            ZoneLog.Fault(session, type, ex);
            throw;
        }
        finally
        {
            PipelineTelemetry.Handler("zone", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    async Task DispatchAsync(ZoneSession session, int sessionId, ushort type, byte[] body, CancellationToken ct)
    {
        if (ZoneWireBoundary.IsLoginClientOpcode(type))
        {
            ZoneLog.Info($"RX login opcode on zone type={type} session={sessionId}");
            await ErrorAsync(session, "wrong_port",
                "That message belongs on the login port, not the zone port.", ct).ConfigureAwait(false);
            return;
        }

        switch ((MsgType)type)
        {
            case MsgType.Hello:
            {
                var hello = Decode<HelloMsg>(body) ?? new HelloMsg();
                ZoneLog.Info($"RX Hello session={sessionId} proto={hello.ProtocolVersion} build={hello.ClientBuild}");
                await session.SendAsync(MsgType.HelloAck, new HelloAckMsg
                {
                    ProtocolVersion = 1,
                    ServerName = "FineageZone"
                }, ct).ConfigureAwait(false);
                break;
            }
            case MsgType.Ping:
            {
                var ping = Decode<PingMsg>(body) ?? new PingMsg();
                long now = _clock().ToUnixTimeMilliseconds();
                await session.SendAsync(MsgType.Pong, new PongMsg { ClientUnixMs = ping.ClientUnixMs, ServerUnixMs = now }, ct)
                    .ConfigureAwait(false);
                break;
            }
            case MsgType.Auth:
            {
                if (session.Bound)
                {
                    ZoneLog.Info($"RX Auth rejected session={sessionId} reason=already_authenticated");
                    await ErrorAsync(session, "already_authenticated",
                        "This session already completed zone Auth.", ct).ConfigureAwait(false);
                    break;
                }

                var auth = Decode<AuthMsg>(body);
                if (auth == null || string.IsNullOrWhiteSpace(auth.Account))
                {
                    await ErrorAsync(session, "bad_request", "Auth account is required.", ct).ConfigureAwait(false);
                    break;
                }

                var status = _tokens.Validate(auth.Password, _clock(), out var parsed);
                if (status == HandoffTokens.ValidateStatus.Expired)
                {
                    ZoneLog.Info($"RX Auth rejected session={sessionId} account={auth.Account} reason=expired");
                    await _world.LeaveAsync(session, ct).ConfigureAwait(false);
                    session.NoteForLog(auth.Account);
                    await ErrorAsync(session, "handoff_expired", "Handoff token has expired.", ct).ConfigureAwait(false);
                    session.Clear();
                    break;
                }

                if (status != HandoffTokens.ValidateStatus.Ok ||
                    !string.Equals(parsed.Account, auth.Account, StringComparison.OrdinalIgnoreCase))
                {
                    ZoneLog.Info($"RX Auth rejected session={sessionId} account={auth.Account}");
                    await _world.LeaveAsync(session, ct).ConfigureAwait(false);
                    session.NoteForLog(auth.Account);
                    await ErrorAsync(session, "invalid_credentials", "Handoff token was rejected.", ct).ConfigureAwait(false);
                    session.Clear();
                    break;
                }

                await _world.LeaveAsync(session, ct).ConfigureAwait(false);
                session.Bind(parsed.CharacterId, parsed.Account, parsed.CharacterName, parsed.ClassId);
                ZoneLog.Info($"RX Auth session={sessionId} account={session.Account} character={session.Name}");
                await session.SendAsync(MsgType.AuthOk, new AuthOkMsg
                {
                    SessionId = sessionId,
                    CharacterName = session.Name
                }, ct).ConfigureAwait(false);
                break;
            }
            case MsgType.EnterWorld:
            {
                if (!session.Bound)
                {
                    await ErrorAsync(session, "auth_required", "Auth with a handoff token first.", ct).ConfigureAwait(false);
                    break;
                }

                if (!await _world.TryEnterAsync(session, ct).ConfigureAwait(false))
                {
                    await ErrorAsync(session, "object_id", "That character id belongs to an NPC.", ct).ConfigureAwait(false);
                    break;
                }

                break;
            }
            case MsgType.MoveTo:
            {
                var move = Decode<MoveToMsg>(body);
                if (move == null)
                {
                    await ErrorAsync(session, "bad_request", "MoveTo body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryMoveAsync(session, move, ct).ConfigureAwait(false);
                if (code == "range")
                    await ErrorAsync(session, "range", "Destination is too far.", ct).ConfigureAwait(false);
                else if (code == ActionProtocol.Sitting)
                    await ErrorAsync(session, code, "You cannot move while sitting.", ct).ConfigureAwait(false);
                else if (code == InventoryProtocol.Dead)
                    await ErrorAsync(session, code, "You cannot do that while dead.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX Move session={sessionId} → {session.X},{session.Y},{session.Z}");
                break;
            }
            case MsgType.SelectTarget:
            {
                var select = Decode<SelectTargetMsg>(body);
                if (select == null)
                {
                    await ErrorAsync(session, "bad_request", "SelectTarget body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySelectTargetAsync(session, select, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == "target")
                    await ErrorAsync(session, "target", "Invalid target.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX SelectTarget session={sessionId} → {select.TargetObjectId}");
                break;
            }
            case MsgType.AttackRequest:
            {
                var attack = Decode<AttackRequestMsg>(body) ?? new AttackRequestMsg();
                string? code = await _world.TryAttackAsync(session, attack, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "attack", "Attack failed.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX AttackRequest session={sessionId} target={session.SelectedTargetId}");
                break;
            }
            case MsgType.Say:
            {
                var say = Decode<SayMsg>(body);
                if (say == null)
                {
                    await ErrorAsync(session, "bad_request", "Say body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySayAsync(session, say, _clock(), ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "Say text was rejected.", ct).ConfigureAwait(false);
                break;
            }
            case MsgType.GrantItemRequest:
            {
                var grant = Decode<GrantItemRequestMsg>(body);
                if (grant == null)
                {
                    await ErrorAsync(session, "bad_request", "GrantItemRequest body was not readable.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryGrantItemAsync(session, grant, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "Grant was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX GrantItem session={sessionId} item={grant.ItemId} count={grant.Count}");
                break;
            }
            case MsgType.UseItem:
            {
                var use = Decode<UseItemMsg>(body);
                if (use == null)
                {
                    await ErrorAsync(session, "bad_request", "UseItem body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryUseItemAsync(session, use, ct).ConfigureAwait(false);
                if (!await RejectGearAsync(session, code, "UseItem was rejected.", ct).ConfigureAwait(false))
                    ZoneLog.Info($"RX UseItem session={sessionId} object={use.ObjectId}");
                break;
            }
            case MsgType.UnequipItem:
            {
                var unequip = Decode<UnequipItemMsg>(body);
                if (unequip == null)
                {
                    await ErrorAsync(session, "bad_request", "UnequipItem body was not readable.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryUnequipItemAsync(session, unequip, ct).ConfigureAwait(false);
                if (!await RejectGearAsync(session, code, "UnequipItem was rejected.", ct).ConfigureAwait(false))
                    ZoneLog.Info($"RX UnequipItem session={sessionId} slot={unequip.PaperdollSlot}");
                break;
            }
            case MsgType.GiveItem:
            {
                if (!_adminCommands)
                {
                    await ErrorAsync(session, InventoryProtocol.Forbidden, "GiveItem is disabled.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                var give = Decode<GiveItemMsg>(body);
                if (give == null)
                {
                    await ErrorAsync(session, "bad_request", "GiveItem body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryGiveItemAsync(session, give, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "GiveItem was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX GiveItem session={sessionId} item={give.ItemId} count={give.Count}");
                break;
            }
            case MsgType.AdminSpoilTarget:
            {
                if (!_adminCommands)
                {
                    await ErrorAsync(session, InventoryProtocol.Forbidden, "AdminSpoilTarget is disabled.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                var spoil = Decode<AdminSpoilTargetMsg>(body);
                if (spoil == null)
                {
                    await ErrorAsync(session, "bad_request", "AdminSpoilTarget body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryAdminSpoilAsync(session, spoil, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == "target")
                    await ErrorAsync(session, "target", "Spoil target rejected.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "AdminSpoilTarget was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX AdminSpoilTarget session={sessionId} target={spoil.TargetObjectId}");
                break;
            }
            case MsgType.AdminSweepCorpse:
            {
                if (!_adminCommands)
                {
                    await ErrorAsync(session, InventoryProtocol.Forbidden, "AdminSweepCorpse is disabled.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                var sweep = Decode<AdminSweepCorpseMsg>(body);
                if (sweep == null)
                {
                    await ErrorAsync(session, "bad_request", "AdminSweepCorpse body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryAdminSweepAsync(session, sweep, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == "sweep")
                    await ErrorAsync(session, "sweep", "Sweep failed.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "AdminSweepCorpse was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX AdminSweepCorpse session={sessionId} target={sweep.TargetObjectId}");
                break;
            }
            case MsgType.PickupRequest:
            {
                var pickup = Decode<PickupRequestMsg>(body);
                if (pickup == null)
                {
                    await ErrorAsync(session, "bad_request", "PickupRequest body was not readable.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryPickupAsync(session, pickup, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == "pickup")
                    await ErrorAsync(session, "pickup", "Pickup failed.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "Pickup was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX Pickup session={sessionId} object={pickup.ObjectId}");
                break;
            }
            case MsgType.DropItemRequest:
            {
                var drop = Decode<DropItemRequestMsg>(body);
                if (drop == null)
                {
                    await ErrorAsync(session, "bad_request", "DropItemRequest body was not readable.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryDropItemAsync(session, drop, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == InventoryProtocol.Dead)
                    await ErrorAsync(session, InventoryProtocol.Dead, "Cannot drop while dead.", ct).ConfigureAwait(false);
                else if (code == InventoryProtocol.NotOwned)
                    await ErrorAsync(session, InventoryProtocol.NotOwned, "Item is not in your bag.", ct).ConfigureAwait(false);
                else if (code == "range")
                    await ErrorAsync(session, "range", "Drop point is too far.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "Drop was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX DropItem session={sessionId} object={drop.ObjectId} count={drop.Count}");
                break;
            }
            case MsgType.BuffRequest:
            {
                var buff = Decode<BuffRequestMsg>(body);
                if (buff == null)
                {
                    await ErrorAsync(session, "bad_request", "BuffRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryBuffRequestAsync(session, buff, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == "target")
                    await ErrorAsync(session, "target", "Invalid buff target.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, "bad_request", "Buff request was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX BuffRequest session={sessionId} target={buff.TargetObjectId} effect={buff.EffectId} remove={buff.Remove}");
                break;
            }
            case MsgType.PartyInviteRequest:
            {
                var invite = Decode<PartyInviteRequestMsg>(body);
                if (invite == null)
                {
                    await ErrorAsync(session, "bad_request", "PartyInviteRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryPartyInviteAsync(session, invite, ct).ConfigureAwait(false);
                await PartyErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX PartyInvite session={sessionId} target={invite.TargetObjectId}");
                break;
            }
            case MsgType.PartyAnswerJoinRequest:
            {
                var answer = Decode<PartyAnswerJoinRequestMsg>(body);
                if (answer == null)
                {
                    await ErrorAsync(session, "bad_request", "PartyAnswerJoinRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryPartyAnswerAsync(session, answer, ct).ConfigureAwait(false);
                await PartyErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX PartyAnswer session={sessionId} requestor={answer.RequestorObjectId} response={answer.Response}");
                break;
            }
            case MsgType.PartyLeaveRequest:
            {
                string? code = await _world.TryPartyLeaveAsync(session, ct).ConfigureAwait(false);
                await PartyErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX PartyLeave session={sessionId}");
                break;
            }
            case MsgType.SevenSignsStatusRequest:
            {
                string? code = await _world.TrySevenSignsStatusAsync(session, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == null)
                    ZoneLog.Info($"RX SevenSignsStatus session={sessionId}");
                break;
            }
            case MsgType.BossStatusRequest:
            {
                var query = Decode<BossStatusRequestMsg>(body);
                if (query == null)
                {
                    await ErrorAsync(session, "bad_request", "BossStatusRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryBossStatusAsync(session, query.NpcId, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == BossProtocol.UnknownBoss)
                    await ErrorAsync(session, BossProtocol.UnknownBoss, "Boss is not on the schedule.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, code, "BossStatusRequest was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX BossStatus session={sessionId} npc={query.NpcId}");
                break;
            }
            case MsgType.AdminBossSchedule:
            {
                if (!_adminCommands)
                {
                    await ErrorAsync(session, InventoryProtocol.Forbidden, "AdminBossSchedule is disabled.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                var adminBoss = Decode<AdminBossScheduleMsg>(body);
                if (adminBoss == null)
                {
                    await ErrorAsync(session, "bad_request", "AdminBossSchedule body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryAdminBossAsync(session, adminBoss, ct).ConfigureAwait(false);
                if (code == "world")
                    await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                else if (code == BossProtocol.UnknownBoss)
                    await ErrorAsync(session, BossProtocol.UnknownBoss, "Boss is not on the schedule.", ct).ConfigureAwait(false);
                else if (code != null)
                    await ErrorAsync(session, code, "AdminBossSchedule was rejected.", ct).ConfigureAwait(false);
                else
                    ZoneLog.Info($"RX AdminBossSchedule session={sessionId} npc={adminBoss.NpcId} action={adminBoss.Action}");
                break;
            }
            case MsgType.SevenSignsActionRequest:
            {
                var action = Decode<SevenSignsActionRequestMsg>(body);
                if (action == null)
                {
                    await ErrorAsync(session, "bad_request", "SevenSignsAction body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySevenSignsActionAsync(session, action, ct).ConfigureAwait(false);
                await SevenSignsErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX SevenSignsAction session={sessionId} action={action.Action} cabal={action.Cabal} seal={action.Seal}");
                break;
            }
            case MsgType.DungeonEnterRequest:
            {
                var enter = Decode<DungeonEnterRequestMsg>(body);
                if (enter == null)
                {
                    await ErrorAsync(session, "bad_request", "DungeonEnterRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryDungeonAsync(session, enter, ct).ConfigureAwait(false);
                await DungeonErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX DungeonEnter session={sessionId} dungeon={enter.DungeonId} action={enter.Action}");
                break;
            }
            case MsgType.PartyKickRequest:
            {
                var kick = Decode<PartyKickRequestMsg>(body);
                if (kick == null)
                {
                    await ErrorAsync(session, "bad_request", "PartyKickRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryPartyKickAsync(session, kick, ct).ConfigureAwait(false);
                await PartyErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX PartyKick session={sessionId} target={kick.TargetObjectId}");
                break;
            }
            case MsgType.TradeRequest:
            {
                var trade = Decode<TradeRequestMsg>(body);
                if (trade == null)
                {
                    await ErrorAsync(session, "bad_request", "TradeRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryTradeRequestAsync(session, trade, ct).ConfigureAwait(false);
                await TradeErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX TradeRequest session={sessionId} target={trade.TargetObjectId}");
                break;
            }
            case MsgType.TradeAnswerRequest:
            {
                var answer = Decode<TradeAnswerRequestMsg>(body);
                if (answer == null)
                {
                    await ErrorAsync(session, "bad_request", "TradeAnswerRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryTradeAnswerAsync(session, answer, ct).ConfigureAwait(false);
                await TradeErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX TradeAnswer session={sessionId} requestor={answer.RequestorObjectId} response={answer.Response}");
                break;
            }
            case MsgType.TradeAddItemRequest:
            {
                var add = Decode<TradeAddItemRequestMsg>(body);
                if (add == null)
                {
                    await ErrorAsync(session, "bad_request", "TradeAddItemRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryTradeAddItemAsync(session, add, ct).ConfigureAwait(false);
                await TradeErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX TradeAddItem session={sessionId} object={add.InventoryObjectId} count={add.Count}");
                break;
            }
            case MsgType.TradeDoneRequest:
            {
                var done = Decode<TradeDoneRequestMsg>(body);
                if (done == null)
                {
                    await ErrorAsync(session, "bad_request", "TradeDoneRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryTradeDoneAsync(session, done, ct).ConfigureAwait(false);
                await TradeErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX TradeDone session={sessionId} response={done.Response}");
                break;
            }
            case MsgType.ClanCreateRequest:
            {
                var create = Decode<ClanCreateRequestMsg>(body);
                if (create == null)
                {
                    await ErrorAsync(session, "bad_request", "ClanCreateRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryClanCreateAsync(session, create, _clock(), ct).ConfigureAwait(false);
                await ClanErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX ClanCreate session={sessionId} name={create.Name}");
                break;
            }
            case MsgType.ClanInviteRequest:
            {
                var invite = Decode<ClanInviteRequestMsg>(body);
                if (invite == null)
                {
                    await ErrorAsync(session, "bad_request", "ClanInviteRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryClanInviteAsync(session, invite, _clock(), ct).ConfigureAwait(false);
                await ClanErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX ClanInvite session={sessionId} target={invite.TargetObjectId}");
                break;
            }
            case MsgType.ClanAnswerJoinRequest:
            {
                var answer = Decode<ClanAnswerJoinRequestMsg>(body);
                if (answer == null)
                {
                    await ErrorAsync(session, "bad_request", "ClanAnswerJoinRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryClanAnswerAsync(session, answer, _clock(), ct).ConfigureAwait(false);
                await ClanErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX ClanAnswer session={sessionId} requestor={answer.RequestorObjectId} response={answer.Response}");
                break;
            }
            case MsgType.ClanLeaveRequest:
            {
                string? code = await _world.TryClanLeaveAsync(session, _clock(), ct).ConfigureAwait(false);
                await ClanErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX ClanLeave session={sessionId}");
                break;
            }
            case MsgType.ClanKickRequest:
            {
                var kick = Decode<ClanKickRequestMsg>(body);
                if (kick == null)
                {
                    await ErrorAsync(session, "bad_request", "ClanKickRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryClanKickAsync(session, kick, _clock(), ct).ConfigureAwait(false);
                await ClanErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX ClanKick session={sessionId} target={kick.TargetObjectId}");
                break;
            }
            case MsgType.ClanDissolveRequest:
            {
                string? code = await _world.TryClanDissolveAsync(session, _clock(), ct).ConfigureAwait(false);
                await ClanErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX ClanDissolve session={sessionId}");
                break;
            }
            case MsgType.ClanCancelDissolveRequest:
            {
                string? code = await _world.TryClanCancelDissolveAsync(session, _clock(), ct).ConfigureAwait(false);
                await ClanErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX ClanCancelDissolve session={sessionId}");
                break;
            }
            case MsgType.EnchantRequest:
            {
                var enchant = Decode<EnchantRequestMsg>(body);
                if (enchant == null)
                {
                    await ErrorAsync(session, "bad_request", "EnchantRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryEnchantRequestAsync(session, enchant, ct).ConfigureAwait(false);
                if (!await RejectEnchantAsync(session, code, ct).ConfigureAwait(false))
                    ZoneLog.Info($"RX EnchantRequest session={sessionId} object={enchant.ObjectId}");
                break;
            }
            case MsgType.EnchantConfirm:
            {
                var enchant = Decode<EnchantConfirmMsg>(body);
                if (enchant == null)
                {
                    await ErrorAsync(session, "bad_request", "EnchantConfirm body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryEnchantConfirmAsync(session, enchant, ct).ConfigureAwait(false);
                if (!await RejectEnchantAsync(session, code, ct).ConfigureAwait(false))
                    ZoneLog.Info($"RX EnchantConfirm session={sessionId} object={enchant.ObjectId}");
                break;
            }
            case MsgType.RequestQuestList:
            {
                string? code = await _world.TryQuestListAsync(session, ct).ConfigureAwait(false);
                await QuestErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX QuestList session={sessionId}");
                break;
            }
            case MsgType.RequestQuestAbort:
            {
                var abort = Decode<RequestQuestAbortMsg>(body);
                if (abort == null)
                {
                    await ErrorAsync(session, "bad_request", "RequestQuestAbort body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryQuestAbortAsync(session, abort.QuestId, ct).ConfigureAwait(false);
                await QuestErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX QuestAbort session={sessionId} quest={abort.QuestId}");
                break;
            }
            case MsgType.NpcTalk:
            {
                var talk = Decode<NpcTalkMsg>(body);
                if (talk == null)
                {
                    await ErrorAsync(session, "bad_request", "NpcTalk body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryNpcTalkAsync(session, talk.NpcObjectId, ct).ConfigureAwait(false);
                await NpcDialogErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX NpcTalk session={sessionId} npc={talk.NpcObjectId}");
                break;
            }
            case MsgType.BypassRequest:
            {
                var bypass = Decode<BypassRequestMsg>(body);
                if (bypass == null)
                {
                    await ErrorAsync(session, "bad_request", "BypassRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryBypassAsync(session, bypass.NpcObjectId, bypass.Command, ct).ConfigureAwait(false);
                await NpcDialogErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX Bypass session={sessionId} npc={bypass.NpcObjectId}");
                break;
            }
            case MsgType.RequestBuyItem:
            {
                var buy = Decode<RequestBuyItemMsg>(body);
                if (buy == null)
                {
                    await ErrorAsync(session, "bad_request", "RequestBuyItem body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryBuyAsync(session, buy, ct).ConfigureAwait(false);
                await ShopErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX Buy session={sessionId} list={buy.ListId} npc={buy.NpcObjectId}");
                break;
            }
            case MsgType.RequestSellItem:
            {
                var sell = Decode<RequestSellItemMsg>(body);
                if (sell == null)
                {
                    await ErrorAsync(session, "bad_request", "RequestSellItem body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySellAsync(session, sell, ct).ConfigureAwait(false);
                await ShopErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX Sell session={sessionId} npc={sell.NpcObjectId}");
                break;
            }
            case MsgType.RequestMultiSellChoose:
            {
                var choose = Decode<RequestMultiSellChooseMsg>(body);
                if (choose == null)
                {
                    await ErrorAsync(session, "bad_request", "RequestMultiSellChoose body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryMultiSellAsync(session, choose, ct).ConfigureAwait(false);
                await ShopErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX MultiSell session={sessionId} list={choose.ListId} entry={choose.EntryId}");
                break;
            }
            case MsgType.RequestPreviewItem:
            {
                var preview = Decode<RequestPreviewItemMsg>(body);
                if (preview == null)
                {
                    await ErrorAsync(session, "bad_request", "RequestPreviewItem body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryPreviewAsync(session, preview, ct).ConfigureAwait(false);
                await ShopErrorAsync(session, code, ct).ConfigureAwait(false);
                break;
            }
            case MsgType.QuestEvent:
            {
                var questEvent = Decode<QuestEventMsg>(body);
                if (questEvent == null)
                {
                    await ErrorAsync(session, "bad_request", "QuestEvent body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryQuestEventAsync(session, questEvent.NpcObjectId, questEvent.QuestId, questEvent.Event, ct)
                    .ConfigureAwait(false);
                await QuestErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX QuestEvent session={sessionId} quest={questEvent.QuestId} event={questEvent.Event}");
                break;
            }
            case MsgType.SiegeRegisterRequest:
            {
                var register = Decode<SiegeRegisterRequestMsg>(body);
                if (register == null)
                {
                    await ErrorAsync(session, "bad_request", "SiegeRegisterRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySiegeRegisterAsync(session, register, ct).ConfigureAwait(false);
                await SiegeErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX SiegeRegister session={sessionId} castle={register.CastleId} attacker={register.Attacker} joining={register.Joining}");
                break;
            }
            case MsgType.SiegeDefenderAnswerRequest:
            {
                var answer = Decode<SiegeDefenderAnswerRequestMsg>(body);
                if (answer == null)
                {
                    await ErrorAsync(session, "bad_request", "SiegeDefenderAnswerRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySiegeAnswerAsync(session, answer, ct).ConfigureAwait(false);
                await SiegeErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX SiegeDefenderAnswer session={sessionId} castle={answer.CastleId} clan={answer.ClanId} approved={answer.Approved}");
                break;
            }
            case MsgType.SiegeListRequest:
            {
                var list = Decode<SiegeListRequestMsg>(body);
                if (list == null)
                {
                    await ErrorAsync(session, "bad_request", "SiegeListRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySiegeListAsync(session, list.CastleId, ct).ConfigureAwait(false);
                await SiegeErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX SiegeList session={sessionId} castle={list.CastleId}");
                break;
            }
            case MsgType.AdminCaptureCastle:
            {
                if (!_adminCommands)
                {
                    await ErrorAsync(session, InventoryProtocol.Forbidden, "AdminCaptureCastle is disabled.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                var capture = Decode<AdminCaptureCastleMsg>(body);
                if (capture == null)
                {
                    await ErrorAsync(session, "bad_request", "AdminCaptureCastle body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryAdminCaptureAsync(session, capture.CastleId, capture.ClanId, ct).ConfigureAwait(false);
                await SiegeErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX AdminCaptureCastle session={sessionId} castle={capture.CastleId} clan={capture.ClanId}");
                break;
            }
            case MsgType.SkillUseRequest:
            {
                var skill = Decode<SkillUseRequestMsg>(body);
                if (skill == null)
                {
                    await ErrorAsync(session, "bad_request", "SkillUseRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySkillUseAsync(session, skill, ct).ConfigureAwait(false);
                await SkillErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX SkillUse session={sessionId} skill={skill.SkillId} target={skill.TargetObjectId}");
                break;
            }
            case MsgType.CharacterActionRequest:
            {
                var action = Decode<CharacterActionRequestMsg>(body);
                if (action == null)
                {
                    await ErrorAsync(session, "bad_request", "CharacterActionRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryCharacterActionAsync(session, action, ct).ConfigureAwait(false);
                await ActionErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX CharacterAction session={sessionId} action={action.ActionId}");
                break;
            }
            case MsgType.ReviveRequest:
            {
                var revive = Decode<ReviveRequestMsg>(body);
                if (revive == null)
                {
                    await ErrorAsync(session, "bad_request", "ReviveRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryReviveAsync(session, revive, ct).ConfigureAwait(false);
                await DeathErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX ReviveRequest session={sessionId} action={revive.Action}");
                break;
            }
            case MsgType.AdminKillPlayer:
            {
                if (!_adminCommands)
                {
                    await ErrorAsync(session, InventoryProtocol.Forbidden, "AdminKillPlayer is disabled.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                if (Decode<AdminKillPlayerMsg>(body) == null)
                {
                    await ErrorAsync(session, "bad_request", "AdminKillPlayer body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryAdminKillPlayerAsync(session, ct).ConfigureAwait(false);
                await DeathErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX AdminKillPlayer session={sessionId}");
                break;
            }
            case MsgType.OlympiadStatusRequest:
            {
                string? code = await _world.TryOlympiadStatusAsync(session, ct).ConfigureAwait(false);
                await OlympiadErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX OlympiadStatus session={sessionId}");
                break;
            }
            case MsgType.OlympiadRegisterRequest:
            {
                var register = Decode<OlympiadRegisterRequestMsg>(body);
                if (register == null)
                {
                    await ErrorAsync(session, "bad_request", "OlympiadRegisterRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryOlympiadRegisterAsync(session, register, ct).ConfigureAwait(false);
                await OlympiadErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX OlympiadRegister session={sessionId} classed={register.Classed}");
                break;
            }
            case MsgType.OlympiadUnregisterRequest:
            {
                string? code = await _world.TryOlympiadUnregisterAsync(session, ct).ConfigureAwait(false);
                await OlympiadErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX OlympiadUnregister session={sessionId}");
                break;
            }
            case MsgType.OlympiadLeaveRequest:
            {
                string? code = await _world.TryOlympiadLeaveAsync(session, ct).ConfigureAwait(false);
                await OlympiadErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX OlympiadLeave session={sessionId}");
                break;
            }
            case MsgType.AdminReportOlympiadResult:
            {
                if (!_adminCommands)
                {
                    await ErrorAsync(session, InventoryProtocol.Forbidden, "AdminReportOlympiadResult is disabled.", ct)
                        .ConfigureAwait(false);
                    break;
                }

                var report = Decode<AdminReportOlympiadResultMsg>(body);
                if (report == null)
                {
                    await ErrorAsync(session, "bad_request", "AdminReportOlympiadResult body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryAdminReportOlympiadAsync(session, report.MatchId, report.WinnerObjectId, ct).ConfigureAwait(false);
                await OlympiadErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX AdminReportOlympiadResult session={sessionId} match={report.MatchId} winner={report.WinnerObjectId}");
                break;
            }
            case MsgType.TeleportSelectRequest:
            {
                var teleport = Decode<TeleportSelectRequestMsg>(body);
                if (teleport == null)
                {
                    await ErrorAsync(session, "bad_request", "TeleportSelectRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryTeleportSelectAsync(session, teleport, ct).ConfigureAwait(false);
                await NpcDialogErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX TeleportSelect session={sessionId} npc={teleport.NpcObjectId}");
                break;
            }
            case MsgType.WarehouseDepositRequest:
            {
                var deposit = Decode<WarehouseDepositRequestMsg>(body);
                if (deposit == null)
                {
                    await ErrorAsync(session, "bad_request", "WarehouseDepositRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryWarehouseDepositAsync(session, deposit, ct).ConfigureAwait(false);
                await NpcDialogErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX WarehouseDeposit session={sessionId} npc={deposit.NpcObjectId}");
                break;
            }
            case MsgType.WarehouseWithdrawRequest:
            {
                var withdraw = Decode<WarehouseWithdrawRequestMsg>(body);
                if (withdraw == null)
                {
                    await ErrorAsync(session, "bad_request", "WarehouseWithdrawRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryWarehouseWithdrawAsync(session, withdraw, ct).ConfigureAwait(false);
                await NpcDialogErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX WarehouseWithdraw session={sessionId} npc={withdraw.NpcObjectId}");
                break;
            }
            case MsgType.SupportMagicRequest:
            {
                var magic = Decode<SupportMagicRequestMsg>(body);
                if (magic == null)
                {
                    await ErrorAsync(session, "bad_request", "SupportMagicRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TrySupportMagicAsync(session, magic.NpcObjectId, ct).ConfigureAwait(false);
                await NpcDialogErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX SupportMagic session={sessionId} npc={magic.NpcObjectId}");
                break;
            }
            case MsgType.GiveBlessingRequest:
            {
                var blessing = Decode<GiveBlessingRequestMsg>(body);
                if (blessing == null)
                {
                    await ErrorAsync(session, "bad_request", "GiveBlessingRequest body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryGiveBlessingAsync(session, blessing.NpcObjectId, ct).ConfigureAwait(false);
                await NpcDialogErrorAsync(session, code, ct).ConfigureAwait(false);
                if (code == null)
                    ZoneLog.Info($"RX GiveBlessing session={sessionId} npc={blessing.NpcObjectId}");
                break;
            }
            case MsgType.EnchantCancel:
            {
                if (Decode<EnchantCancelMsg>(body) == null)
                {
                    await ErrorAsync(session, "bad_request", "EnchantCancel body was not readable.", ct).ConfigureAwait(false);
                    break;
                }

                string? code = await _world.TryEnchantCancelAsync(session, ct).ConfigureAwait(false);
                if (!await RejectEnchantAsync(session, code, ct).ConfigureAwait(false))
                    ZoneLog.Info($"RX EnchantCancel session={sessionId}");
                break;
            }
            default:
                ZoneLog.Info($"RX unknown type={type} session={sessionId}");
                await ErrorAsync(session, "unknown", $"msgType={type}", ct).ConfigureAwait(false);
                break;
        }
    }

    static async Task<bool> RejectEnchantAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        switch (code)
        {
            case null:
                return false;
            case "world":
                await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.NotOwned:
                await ErrorAsync(session, code, "That item is not in your inventory.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.Dead:
                await ErrorAsync(session, code, "You cannot enchant while dead.", ct).ConfigureAwait(false);
                break;
            case EnchantProtocol.NotScroll:
                await ErrorAsync(session, code, "That item is not an enchant scroll.", ct).ConfigureAwait(false);
                break;
            case EnchantProtocol.NoRequest:
                await ErrorAsync(session, code, "No enchant scroll is selected.", ct).ConfigureAwait(false);
                break;
            case EnchantProtocol.NotEnchantable:
                await ErrorAsync(session, code, "That item cannot be enchanted.", ct).ConfigureAwait(false);
                break;
            case EnchantProtocol.GradeMismatch:
                await ErrorAsync(session, code, "The scroll grade does not match the item.", ct).ConfigureAwait(false);
                break;
            case EnchantProtocol.SlotMismatch:
                await ErrorAsync(session, code, "That scroll cannot enchant this item.", ct).ConfigureAwait(false);
                break;
            case EnchantProtocol.MaxEnchant:
                await ErrorAsync(session, code, "That item is already at the maximum enchant.", ct).ConfigureAwait(false);
                break;
            default:
                await ErrorAsync(session, "bad_request", "Enchant request was rejected.", ct).ConfigureAwait(false);
                break;
        }

        return true;
    }

    static async Task<bool> RejectGearAsync(ZoneSession session, string? code, string fallback, CancellationToken ct)
    {
        switch (code)
        {
            case null:
                return false;
            case "world":
                await ErrorAsync(session, "world", "EnterWorld first.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.NotOwned:
                await ErrorAsync(session, code, "That item is not in your inventory.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.NotEquippable:
                await ErrorAsync(session, code, "That item cannot be equipped.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.Dead:
                await ErrorAsync(session, code, "You cannot change gear while dead.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.Blocked:
                await ErrorAsync(session, code, "That item cannot be equipped right now.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.BadSlot:
                await ErrorAsync(session, code, "Paperdoll slot is out of range.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.Empty:
                await ErrorAsync(session, code, "That paperdoll slot is empty.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.ClassRestricted:
                await ErrorAsync(session, code, "Your class cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.RaceRestricted:
                await ErrorAsync(session, code, "Your race cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.SexRestricted:
                await ErrorAsync(session, code, "Your sex cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.HeroRestricted:
                await ErrorAsync(session, code, "Only a hero can equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.ClanLeaderRestricted:
                await ErrorAsync(session, code, "Only a clan leader can equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.PledgeRestricted:
                await ErrorAsync(session, code, "Your clan rank cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.PvpRestricted:
                await ErrorAsync(session, code, "Your PvP status cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.LevelRestricted:
                await ErrorAsync(session, code, "Your level cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.CastleRestricted:
                await ErrorAsync(session, code, "Your castle cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.ClanHallRestricted:
                await ErrorAsync(session, code, "Your clan hall cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.PkRestricted:
                await ErrorAsync(session, code, "Your PK count cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.GradeRestricted:
                await ErrorAsync(session, code, "Your expertise grade cannot equip that item.", ct).ConfigureAwait(false);
                break;
            case InventoryProtocol.ConditionRestricted:
                await ErrorAsync(session, code, "You do not meet the required condition to equip that item.", ct).ConfigureAwait(false);
                break;
            default:
                await ErrorAsync(session, "bad_request", fallback, ct).ConfigureAwait(false);
                break;
        }

        return true;
    }

    static Task ErrorAsync(ZoneSession session, string code, string message, CancellationToken ct)
    {
        if (!session.FailureRecorded)
        {
            session.FailureRecorded = true;
            if (code is "rate_limit" or "unknown" or "wrong_port")
                PipelineTelemetry.PacketRejected("zone");
            else
                PipelineTelemetry.PacketErrored("zone");
        }

        ZoneLog.Packet(session, code, code is "rate_limit" or "unknown" or "wrong_port");
        return session.SendAsync(MsgType.Error, new ErrorMsg { Code = code, Message = message }, ct);
    }

    static Task SkillErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        return code switch
        {
            "world" => ErrorAsync(session, "world", "EnterWorld first.", ct),
            "bad_request" => ErrorAsync(session, "bad_request", "Skill request was rejected.", ct),
            SkillProtocol.Dead => ErrorAsync(session, code, "You cannot do that while dead.", ct),
            SkillProtocol.Unknown => ErrorAsync(session, code, "You do not know that skill.", ct),
            SkillProtocol.Passive => ErrorAsync(session, code, "That skill cannot be used.", ct),
            SkillProtocol.Reuse => ErrorAsync(session, code, "That skill is not ready yet.", ct),
            SkillProtocol.Mp => ErrorAsync(session, code, "Not enough MP.", ct),
            SkillProtocol.Target => ErrorAsync(session, code, "Invalid skill target.", ct),
            SkillProtocol.Range => ErrorAsync(session, code, "Target is out of range.", ct),
            _ => ErrorAsync(session, "bad_request", "Skill request was rejected.", ct)
        };
    }

    static Task ActionErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        return code switch
        {
            "world" => ErrorAsync(session, "world", "EnterWorld first.", ct),
            "bad_request" => ErrorAsync(session, "bad_request", "Character action was rejected.", ct),
            "target" => ErrorAsync(session, "target", "No valid target.", ct),
            "attack" => ErrorAsync(session, "attack", "Attack failed.", ct),
            ActionProtocol.Dead => ErrorAsync(session, code, "You cannot do that while dead.", ct),
            ActionProtocol.Blocked => ErrorAsync(session, code, "You cannot do that now.", ct),
            ActionProtocol.Sitting => ErrorAsync(session, code, "You cannot move while sitting.", ct),
            ActionProtocol.Unknown => ErrorAsync(session, code, "Unknown action.", ct),
            ActionProtocol.Unsupported => ErrorAsync(session, code, "That action is not available yet.", ct),
            _ => ErrorAsync(session, "bad_request", "Character action was rejected.", ct)
        };
    }

    static Task NpcDialogErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == NpcHtmlProtocol.Attackable)
            return ErrorAsync(session, code, "That NPC cannot be talked to.", ct);
        return QuestErrorAsync(session, code, ct);
    }

    static Task DeathErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        return code switch
        {
            "world" => ErrorAsync(session, "world", "EnterWorld first.", ct),
            "bad_request" => ErrorAsync(session, "bad_request", "Death request was rejected.", ct),
            InventoryProtocol.Dead => ErrorAsync(session, code, "You cannot do that while dead.", ct),
            DeathProtocol.NotDead => ErrorAsync(session, code, "You are not dead.", ct),
            DeathProtocol.AlreadyDead => ErrorAsync(session, code, "You are already dead.", ct),
            DeathProtocol.Unsupported => ErrorAsync(session, code, "That respawn option is not available yet.", ct),
            _ => ErrorAsync(session, "bad_request", "Death request was rejected.", ct)
        };
    }

    static Task QuestErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        return code switch
        {
            "world" => ErrorAsync(session, "world", "EnterWorld first.", ct),
            "bad_request" => ErrorAsync(session, "bad_request", "Quest request was rejected.", ct),
            "range" => ErrorAsync(session, "range", "You are too far away.", ct),
            InventoryProtocol.Dead => ErrorAsync(session, code, "You cannot do that while dead.", ct),
            QuestProtocol.Unknown => ErrorAsync(session, code, "That quest does not exist.", ct),
            QuestProtocol.Limit => ErrorAsync(session, code, "You cannot take on any more quests.", ct),
            QuestProtocol.State => ErrorAsync(session, code, "That quest cannot be updated.", ct),
            QuestProtocol.Abort => ErrorAsync(session, code, "That quest cannot be aborted.", ct),
            QuestProtocol.Item => ErrorAsync(session, code, "You do not have the required item.", ct),
            QuestProtocol.Npc => ErrorAsync(session, code, "That NPC is not here.", ct),
            _ => ErrorAsync(session, "bad_request", "Quest request was rejected.", ct)
        };
    }

    static Task SevenSignsErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        return code switch
        {
            "world" => ErrorAsync(session, "world", "EnterWorld first.", ct),
            "bad_request" => ErrorAsync(session, "bad_request", "Seven Signs request was rejected.", ct),
            SevenSignsProtocol.PeriodClosed => ErrorAsync(session, code, "Seven Signs is not open for that during this period.", ct),
            SevenSignsProtocol.BadCabal => ErrorAsync(session, code, "Cabal join was rejected.", ct),
            SevenSignsProtocol.BadSeal => ErrorAsync(session, code, "Seal choice was rejected.", ct),
            SevenSignsProtocol.ClassTooLow => ErrorAsync(session, code, "A class transfer is required to join a cabal.", ct),
            SevenSignsProtocol.CastleBlocked => ErrorAsync(session, code, "Castle owners cannot join Dusk.", ct),
            SevenSignsProtocol.FeeRequired => ErrorAsync(session, code, "Dawn requires a certificate of approval or the adena fee.", ct),
            SevenSignsProtocol.ContributionRejected => ErrorAsync(session, code, "That contribution exceeds the seal stone cap.", ct),
            SevenSignsProtocol.StonesMissing => ErrorAsync(session, code, "Not enough seal stones.", ct),
            _ => ErrorAsync(session, "bad_request", "Seven Signs request was rejected.", ct)
        };
    }

    static Task ShopErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        string message = code switch
        {
            "world" => "EnterWorld first.",
            "bad_request" => "Shop request was rejected.",
            "range" => "You are too far away.",
            InventoryProtocol.Dead => "You cannot do that while dead.",
            QuestProtocol.Npc => "That NPC is not here.",
            NpcHtmlProtocol.Attackable => "That NPC will not trade.",
            _ => "Shop request was rejected."
        };
        return ErrorAsync(session, code, message, ct);
    }

    static Task DungeonErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        string message = code switch
        {
            "world" => "EnterWorld first.",
            "bad_request" => "Dungeon request was rejected.",
            DungeonProtocol.Unknown => "That catacomb or necropolis does not exist.",
            DungeonProtocol.Cabal => "You may not pass.",
            DungeonProtocol.Dawn => "Only a Lord of Dawn may use this.",
            DungeonProtocol.Dusk => "Only a Revolutionary of Dusk may use this.",
            DungeonProtocol.Fee => "You do not have enough to pay the entrance fee.",
            InventoryProtocol.Dead => "You cannot do that while dead.",
            _ => "Dungeon request was rejected."
        };
        return ErrorAsync(session, code, message, ct);
    }

    static Task PartyErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        return code switch
        {
            "world" => ErrorAsync(session, "world", "EnterWorld first.", ct),
            "bad_request" => ErrorAsync(session, "bad_request", "Party request was rejected.", ct),
            "target" => ErrorAsync(session, "target", "Invalid party target.", ct),
            "party_full" => ErrorAsync(session, "party_full", "The party is full.", ct),
            "party_leader" => ErrorAsync(session, "party_leader", "Only the party leader can do that.", ct),
            "party_busy" => ErrorAsync(session, "party_busy", "That player cannot join right now.", ct),
            "party_invite" => ErrorAsync(session, "party_invite", "No pending party invite.", ct),
            "party" => ErrorAsync(session, "party", "Not in a party.", ct),
            _ => ErrorAsync(session, "party", "Party action failed.", ct)
        };
    }

    static Task TradeErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        return code switch
        {
            "world" => ErrorAsync(session, "world", "EnterWorld first.", ct),
            "bad_request" => ErrorAsync(session, "bad_request", "Trade request was rejected.", ct),
            "target" => ErrorAsync(session, "target", "Invalid trade target.", ct),
            TradeProtocol.Busy => ErrorAsync(session, TradeProtocol.Busy, "That player cannot trade right now.", ct),
            TradeProtocol.Invite => ErrorAsync(session, TradeProtocol.Invite, "No pending trade request.", ct),
            TradeProtocol.Session => ErrorAsync(session, TradeProtocol.Session, "You are not trading.", ct),
            TradeProtocol.Item => ErrorAsync(session, TradeProtocol.Item, "That item cannot be traded.", ct),
            TradeProtocol.Range => ErrorAsync(session, TradeProtocol.Range, "You are too far to trade.", ct),
            InventoryProtocol.NotOwned => ErrorAsync(session, InventoryProtocol.NotOwned, "You do not own that item.", ct),
            _ => ErrorAsync(session, TradeProtocol.Session, "Trade action failed.", ct)
        };
    }

    static Task SiegeErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        string message = code switch
        {
            "world" => "EnterWorld first.",
            "bad_request" => "Siege request was rejected.",
            SiegeProtocol.Clan => "You are not in a clan.",
            SiegeProtocol.Leader => "Only the clan leader can do that.",
            SiegeProtocol.Dissolving => "Your clan may not register for a siege while it is dissolving.",
            SiegeProtocol.Npc => "You cannot defend a castle that is owned by an NPC.",
            SiegeProtocol.Closed => "The deadline to register for that siege has passed.",
            SiegeProtocol.Progress => "This is not the time for siege registration.",
            SiegeProtocol.Level => $"Only clans of level {SiegeProtocol.MinClanLevel} or higher may register for a castle siege.",
            SiegeProtocol.AutoDefender => "The clan that owns the castle is automatically registered on the defending side.",
            SiegeProtocol.Owns => "A clan that owns a castle cannot participate in another siege.",
            SiegeProtocol.Duplicate => "You have already requested a siege battle.",
            SiegeProtocol.SameDay => "Your clan is already registered for another siege on that day.",
            SiegeProtocol.Attackers => "No more clans may register on the attacking side.",
            SiegeProtocol.Defenders => "No more clans may register on the defending side.",
            SiegeProtocol.None => "Your clan is not registered for that siege.",
            SiegeProtocol.NotOwner => "Only the castle owner can approve defenders.",
            SiegeProtocol.Pending => "That clan is not waiting for defender approval.",
            SiegeProtocol.Castle => "That castle does not exist.",
            SiegeProtocol.Idle => "That castle is not under siege.",
            SiegeProtocol.NotAttacker => "That clan is not attacking the castle.",
            SiegeProtocol.Store => "The siege could not be saved.",
            _ => "Siege request was rejected."
        };
        return ErrorAsync(session, code, message, ct);
    }

    static Task OlympiadErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        string message = code switch
        {
            "world" => "EnterWorld first.",
            "bad_request" => "Olympiad request was rejected.",
            OlympiadProtocol.Disabled => "The Olympiad is disabled.",
            OlympiadProtocol.Period => "The Grand Olympiad Games are not currently in progress.",
            OlympiadProtocol.Noble => "Only noblesse can participate in the Olympiad.",
            OlympiadProtocol.Subclass => "A subclass cannot enter the Olympiad.",
            OlympiadProtocol.Cursed => "A cursed weapon cannot enter the Olympiad.",
            OlympiadProtocol.Inventory => "Your inventory is too full for the Olympiad.",
            OlympiadProtocol.Late => "Olympiad registration is closed for the last 10 minutes.",
            OlympiadProtocol.Classed => "You are already on the classed waiting list.",
            OlympiadProtocol.Registered => "You are already registered for the Olympiad.",
            OlympiadProtocol.Points => "You do not have enough Olympiad points.",
            OlympiadProtocol.Selected => "You are already selected for an Olympiad match.",
            OlympiadProtocol.None => "You are not on an Olympiad waiting list.",
            OlympiadProtocol.MatchMissing => "You are not in an Olympiad match.",
            OlympiadProtocol.Phase => "That Olympiad match is not in progress.",
            OlympiadProtocol.Winner => "That character is not in the match.",
            OlympiadProtocol.Store => "The Olympiad could not be saved.",
            _ => "Olympiad request was rejected."
        };
        return ErrorAsync(session, code, message, ct);
    }

    static Task ClanErrorAsync(ZoneSession session, string? code, CancellationToken ct)
    {
        if (code == null)
            return Task.CompletedTask;
        string message = code switch
        {
            "world" => "EnterWorld first.",
            "bad_request" => "Clan request was rejected.",
            ClanProtocol.Level => "Level 10 is required to create a clan.",
            ClanProtocol.Member => "You already belong to a clan.",
            ClanProtocol.CreatePenalty => "You must wait before creating another clan.",
            ClanProtocol.Name => "Clan name is incorrect.",
            ClanProtocol.NameLength => "Clan name is too long.",
            ClanProtocol.NameTaken => "That clan name already exists.",
            ClanProtocol.None => "You are not in a clan.",
            ClanProtocol.Leader => "Only the clan leader can do that.",
            ClanProtocol.LeaderLeave => "The clan leader cannot leave the clan.",
            ClanProtocol.Pledge => "That pledge type is not available.",
            ClanProtocol.Self => "You cannot invite yourself.",
            ClanProtocol.Target => "That target cannot be invited.",
            ClanProtocol.TargetClan => "That player is already in a clan.",
            ClanProtocol.Busy => "That player is busy.",
            ClanProtocol.Waiting => "You are already waiting for a reply.",
            ClanProtocol.RecruitPenalty => "The clan must wait before accepting a new member.",
            ClanProtocol.JoinPenalty => "That player must wait before joining a clan.",
            ClanProtocol.Full => "The clan is full.",
            ClanProtocol.Invite => "No pending clan invite.",
            ClanProtocol.Combat => "You cannot do that during combat.",
            ClanProtocol.DismissSelf => "You cannot dismiss yourself.",
            ClanProtocol.Dissolving => "Clan dissolution is already in progress.",
            ClanProtocol.NotDissolving => "The clan is not being dissolved.",
            ClanProtocol.Store => "The clan could not be saved.",
            _ => "Clan action failed."
        };
        return ErrorAsync(session, code, message, ct);
    }

    static T? Decode<T>(byte[] body) where T : class
    {
        if (body.Length == 0)
            return null;
        try
        {
            return MessagePackSerializer.Deserialize<T>(body);
        }
        catch (MessagePackSerializationException)
        {
            return null;
        }
    }
}
