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
            default:
                ZoneLog.Info($"RX unknown type={type} session={sessionId}");
                await ErrorAsync(session, "unknown", $"msgType={type}", ct).ConfigureAwait(false);
                break;
        }
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
