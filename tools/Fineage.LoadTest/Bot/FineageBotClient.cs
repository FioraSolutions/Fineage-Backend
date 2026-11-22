using System.Diagnostics;
using System.Net.Sockets;
using Fineage.Accounts;
using Fineage.LoadTest.Models;
using Fineage.Net.Contract;
using Fineage.Net.Host;

namespace Fineage.LoadTest.Bot;

public sealed class FineageBotClient : IAsyncDisposable
{
    TcpClient _login = new();
    TcpClient _zone = new();
    NetworkStream? _loginStream;
    NetworkStream? _zoneStream;
    uint _loginSeq;
    uint _zoneSeq;
    readonly Random _rng;
    HandoffMsg? _handoff;
    readonly BotStats _stats;
    readonly BotObservationLog _observation;
    readonly BotPeerDirectory _peers;
    Task? _receiveTask;
    CancellationTokenSource _receiveCts = new();
    ushort _lastRecvOpcode;
    ushort _lastSentOpcode;
    int _spawnX;
    int _spawnY;
    int _spawnZ;

    public FineageBotClient(LoadScenario scenario, int botId, BotStats stats, BotPeerDirectory peers)
    {
        _peers = peers;
        _rng = new Random(botId * 7919 + 17);
        _stats = stats;
        BotId = botId;
        Scenario = scenario;
        Account = $"{scenario.AccountPrefix}{botId:D5}";
        CharacterName = $"Bot{botId:D4}";
        (_spawnX, _spawnY, _spawnZ) = BotSpawn.Position(scenario, botId);
        _observation = new BotObservationLog(botId);
    }

    public int BotId { get; }
    public LoadScenario Scenario { get; }
    public string Account { get; }
    public string CharacterName { get; }
    public BotStats Stats => _stats;
    public BotObservationLog ObservationLog => _observation;
    public int SelfObjectId => (int)_selfEntity;

    ulong _selfEntity;

    public async Task ConnectAndEnterAsync(CancellationToken ct)
    {
        await _login.ConnectAsync(Scenario.LoginHost, Scenario.LoginPort, ct);
        _loginStream = _login.GetStream();
        await LoginHandshakeAsync(ct);

        await _zone.ConnectAsync(_handoff!.ZoneHost, _handoff.ZonePort, ct);
        _zoneStream = _zone.GetStream();

        await ZoneWriteAsync(NetOpcode.Hello, NetCodec.EncodeHello(new HelloMsg { ClientName = "loadtest" }), ct);
        await ZoneReadUntilAsync(NetOpcode.HelloAck, ct);

        await ZoneExchangeAsync(
            NetOpcode.ZoneAuth,
            NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = Account, Token = _handoff.Token }),
            NetOpcode.ZoneAuthResult,
            ct);

        await EnterWorldAsync(ct);
        _stats.InWorld = true;
        if (_selfEntity != 0)
            _peers.Register(BotId, SelfObjectId);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);
    }

    async Task ReceiveLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var (opcode, payload) = await ZoneReadFrameAsync(ct);
                _lastRecvOpcode = opcode;
                _observation.Record(opcode, payload, DateTimeOffset.UtcNow);
                if (opcode == NetOpcode.Reject)
                    _stats.LastError = NetCodec.DecodeReject(payload).Code;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LoadTestDisconnectLog.LogBotDisconnect(
                    BotId,
                    SelfObjectId,
                    _lastSentOpcode,
                    _lastRecvOpcode,
                    ex);
                _stats.LastError = ex.Message;
                _stats.Disconnected = true;
                break;
            }
        }
    }

    public async Task RunBehaviorLoopAsync(
        IReadOnlyList<FineageBotClient> fleet,
        DateTimeOffset behaviorEnd,
        TimeSpan slotPeriod,
        CancellationToken ct)
    {
        var nextSlot = DateTimeOffset.UtcNow;
        while (!ct.IsCancellationRequested && DateTimeOffset.UtcNow < behaviorEnd && !_stats.Disconnected)
        {
            await RunBehaviorTickAsync(fleet, ct);
            nextSlot += slotPeriod;
            var delay = nextSlot - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
            else
                Interlocked.Increment(ref _stats.MissedBehaviorSlots);
        }
    }

    async Task LoginHandshakeAsync(CancellationToken ct)
    {
        await LoginExchangeAsync(NetOpcode.Hello,
            NetCodec.EncodeHello(new HelloMsg
            {
                ClientName = "loadtest",
                FeatureFlags = ProtocolCapabilities.FeatureSession | ProtocolCapabilities.FeatureLobby
            }),
            NetOpcode.HelloAck, ct);

        await LoginExchangeAsync(NetOpcode.Auth,
            NetCodec.EncodeAuth(new AuthMsg
            {
                Account = Account,
                Password = Scenario.Password,
                RegisterIfMissing = true
            }),
            NetOpcode.AuthResult, ct);

        await LoginWriteAsync(NetOpcode.ListCharacters, [], ct);
        var rosterPayload = await LoginReadUntilAsync(NetOpcode.CharacterRoster, ct);
        var roster = NetCodec.DecodeCharacterRoster(rosterPayload);

        if (roster.Characters.Length == 0)
        {
            await LoginExchangeAsync(NetOpcode.CreateCharacter,
                NetCodec.EncodeCreateCharacter(new CreateCharacterMsg
                {
                    Name = CharacterName,
                    ClassId = Scenario.ClassId,
                    Race = Scenario.Race,
                    Sex = HumanClasses.SexMale
                }),
                NetOpcode.CreateResult, ct);
            await LoginWriteAsync(NetOpcode.ListCharacters, [], ct);
            rosterPayload = await LoginReadUntilAsync(NetOpcode.CharacterRoster, ct);
            roster = NetCodec.DecodeCharacterRoster(rosterPayload);
        }

        var pick = roster.Characters.FirstOrDefault(c => c.Name == CharacterName)
            ?? roster.Characters[0];

        var handoffPayload = await LoginExchangePayloadAsync(NetOpcode.SelectCharacter,
            NetCodec.EncodeSelectCharacter(new SelectCharacterMsg { CharacterId = pick.CharacterId }),
            NetOpcode.Handoff, ct);
        _handoff = NetCodec.DecodeHandoff(handoffPayload);
    }

    async Task EnterWorldAsync(CancellationToken ct)
    {
        await ZoneWriteAsync(NetOpcode.EnterWorld, NetCodec.EncodeEnterWorld(new EnterWorldMsg()), ct);
        bool sawWelcome = false;
        while (true)
        {
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (sawWelcome)
                quiet.CancelAfter(TimeSpan.FromMilliseconds(400));
            byte[] payload;
            ushort opcode;
            try
            {
                (opcode, payload) = await ZoneReadFrameAsync(quiet.Token);
            }
            catch (OperationCanceledException) when (sawWelcome && !ct.IsCancellationRequested)
            {
                return;
            }

            _observation.Record(opcode, payload, DateTimeOffset.UtcNow);
            if (opcode == NetOpcode.Reject)
                throw new InvalidOperationException(NetCodec.DecodeReject(payload).Code);
            if (opcode == NetOpcode.SystemMessage)
                sawWelcome = true;
            if (opcode == NetOpcode.EntitySpawn)
            {
                var spawn = NetCodec.DecodeEntitySpawn(payload);
                if (spawn.IsSelf)
                    _selfEntity = spawn.EntityValue;
            }
        }
    }

    public async Task RunBehaviorTickAsync(IReadOnlyList<FineageBotClient> fleet, CancellationToken ct)
    {
        if (_zoneStream == null || _stats.Disconnected)
            return;

        string action = PickAction();
        var sw = Stopwatch.StartNew();
        try
        {
            switch (action)
            {
                case "move":
                    int dx = _rng.Next(-400, 400);
                    int dy = _rng.Next(-400, 400);
                    await SendTrackedAsync(NetOpcode.MoveInput,
                        NetCodec.EncodeMoveInput(new MoveInputMsg
                        {
                            DestX = _spawnX + dx,
                            DestY = _spawnY + dy,
                            DestZ = _spawnZ
                        }), ct);
                    break;
                case "attack":
                    {
                        if (_observation.TryPickAttackTarget(out int target))
                        {
                            await ZoneWriteAsync(NetOpcode.SelectTarget,
                                NetCodec.EncodeSelectTarget(new SelectTargetNetMsg { TargetObjectId = target }), ct);
                            await SendTrackedAsync(
                                NetOpcode.AttackRequest,
                                NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = target }),
                                ct,
                                targetObjectId: target);
                        }

                        break;
                    }
                case "skill":
                    {
                        if (_observation.TryPickAttackTarget(out int target))
                        {
                            await SendTrackedAsync(
                                NetOpcode.UseSkill,
                                NetCodec.EncodeUseSkill(new UseSkillNetMsg { SkillId = 3, TargetObjectId = target, CtrlPressed = false }),
                                ct,
                                targetObjectId: target,
                                skillId: 3);
                        }

                        break;
                    }
                case "say":
                    await ZoneWriteAsync(NetOpcode.Say,
                        NetCodec.EncodeSay(new SayNetMsg { Text = $"load {BotId}", ChatType = 0 }), ct);
                    break;
                case "sit":
                    await ZoneWriteAsync(NetOpcode.RequestActionUse,
                        NetCodec.EncodeActionUse(new ActionUseNetMsg { ActionId = 0 }), ct);
                    break;
                case "social":
                    await ZoneWriteAsync(NetOpcode.RequestActionUse,
                        NetCodec.EncodeActionUse(new ActionUseNetMsg { ActionId = 12 + _rng.Next(0, 3) }), ct);
                    break;
                case "pickup":
                    await ZoneWriteAsync(NetOpcode.PickupRequest,
                        NetCodec.EncodePickupRequest(new PickupRequestNetMsg { GroundObjectId = 1 }), ct);
                    break;
                case "trade":
                    await ZoneWriteAsync(NetOpcode.TradeRequest,
                        NetCodec.EncodeTradeRequest(new TradeRequestNetMsg { TargetObjectId = SelfObjectId + 2 }), ct);
                    break;
                case "pvp":
                    {
                        int? peer = VisiblePlayerObjectId() ?? _peers.PickPeerObjectId(BotId, _rng);
                        if (peer is > 0)
                        {
                            await ZoneWriteAsync(NetOpcode.SelectTarget,
                                NetCodec.EncodeSelectTarget(new SelectTargetNetMsg { TargetObjectId = peer.Value }), ct);
                            await SendTrackedAsync(NetOpcode.AttackRequest,
                                NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = peer.Value, Forced = true }), ct);
                        }

                        break;
                    }
                case "party":
                    {
                        int? peer = _peers.PickPeerObjectId(BotId, _rng);
                        if (peer is > 0)
                            await ZoneWriteAsync(NetOpcode.PartyInviteRequest,
                                NetCodec.EncodePartyInviteRequest(new PartyInviteRequestNetMsg { TargetObjectId = peer.Value }), ct);
                        break;
                    }
                case "relog":
                    await RelogAsync(fleet, ct);
                    break;
                case "teleport":
                    await ZoneWriteAsync(NetOpcode.TeleportToLocation,
                        NetCodec.EncodeTeleportToLocation(new TeleportToLocationNetMsg
                        {
                            X = _spawnX,
                            Y = _spawnY,
                            Z = _spawnZ
                        }), ct);
                    break;
            }

            Interlocked.Increment(ref _stats.ActionsAttempted);
            _stats.LastActionAt = DateTimeOffset.UtcNow;
            sw.Stop();
            _stats.RecordRtt(sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _stats.ActionsFailed);
            _stats.LastError = ex.Message;
            _stats.Disconnected = true;
        }
    }

    async Task SendTrackedAsync(
        ushort opcode,
        byte[] payload,
        CancellationToken ct,
        int targetObjectId = 0,
        int skillId = 0)
    {
        _lastSentOpcode = opcode;
        _observation.RecordCommandSent(opcode, DateTimeOffset.UtcNow, targetObjectId, skillId);
        await ZoneWriteAsync(opcode, payload, ct);
    }

    int? VisiblePlayerObjectId()
    {
        var tracker = _observation.Tracker;
        foreach (var ent in tracker.Entities.Values)
        {
            if (ent.Despawned || ent.Kind != EntityKindNet.Player || ent.ObjectId == SelfObjectId)
                continue;
            return ent.ObjectId;
        }

        return null;
    }

    async Task RelogAsync(IReadOnlyList<FineageBotClient> fleet, CancellationToken ct)
    {
        _stats.RelogsAttempted++;
        _peers.Unregister(BotId);
        await StopReceiveLoopAsync();
        TearDownConnections();

        _login = new TcpClient();
        _zone = new TcpClient();
        _loginSeq = 0;
        _zoneSeq = 0;
        _selfEntity = 0;
        _receiveCts = new CancellationTokenSource();
        _stats.Disconnected = false;

        await _login.ConnectAsync(Scenario.LoginHost, Scenario.LoginPort, ct);
        _loginStream = _login.GetStream();
        await LoginHandshakeAsync(ct);
        await _zone.ConnectAsync(_handoff!.ZoneHost, _handoff.ZonePort, ct);
        _zoneStream = _zone.GetStream();
        await ZoneWriteAsync(NetOpcode.Hello, NetCodec.EncodeHello(new HelloMsg { ClientName = "loadtest" }), ct);
        await ZoneReadUntilAsync(NetOpcode.HelloAck, ct);
        await ZoneExchangeAsync(
            NetOpcode.ZoneAuth,
            NetCodec.EncodeZoneAuth(new ZoneAuthMsg { Account = Account, Token = _handoff.Token }),
            NetOpcode.ZoneAuthResult,
            ct);
        await EnterWorldAsync(ct);
        _stats.InWorld = true;
        if (_selfEntity != 0)
            _peers.Register(BotId, SelfObjectId);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token), CancellationToken.None);
        Interlocked.Increment(ref _stats.RelogsSucceeded);
    }

    async Task StopReceiveLoopAsync()
    {
        try
        {
            await _receiveCts.CancelAsync();
        }
        catch
        {
            /* ignore */
        }

        if (_receiveTask != null)
        {
            try
            {
                await _receiveTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch
            {
                /* ignore */
            }
        }

        _receiveCts.Dispose();
    }

    void TearDownConnections()
    {
        try { _zoneStream?.Dispose(); } catch { /* ignore */ }
        try { _loginStream?.Dispose(); } catch { /* ignore */ }
        try { _zone.Dispose(); } catch { /* ignore */ }
        try { _login.Dispose(); } catch { /* ignore */ }
        _zoneStream = null;
        _loginStream = null;
    }

    string PickAction()
    {
        double roll = _rng.NextDouble();
        double acc = 0;
        foreach (var (name, weight) in Scenario.ActionWeights)
        {
            acc += weight;
            if (roll <= acc)
                return name;
        }

        return "move";
    }

    async Task LoginExchangeAsync(ushort send, byte[] payload, ushort expect, CancellationToken ct)
    {
        await LoginWriteAsync(send, payload, ct);
        await LoginReadUntilAsync(expect, ct);
    }

    async Task<byte[]> LoginExchangePayloadAsync(ushort send, byte[] payload, ushort expect, CancellationToken ct)
    {
        await LoginWriteAsync(send, payload, ct);
        return await LoginReadUntilAsync(expect, ct);
    }

    async Task ZoneExchangeAsync(ushort send, byte[] payload, ushort expect, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        await ZoneWriteAsync(send, payload, ct);
        ushort opcode = (await ZoneReadFrameAsync(ct)).opcode;
        if (opcode != expect && opcode != NetOpcode.Reject)
            throw new InvalidOperationException($"expected 0x{expect:X4}");
        sw.Stop();
        _stats.RecordRtt(sw.Elapsed.TotalMilliseconds);
    }

    async Task LoginWriteAsync(ushort opcode, byte[] payload, CancellationToken ct)
    {
        await NetFraming.WriteAsync(_loginStream!, new PacketEnvelope
        {
            Opcode = opcode,
            RequestSequence = unchecked(++_loginSeq),
            Payload = payload
        }, ct);
    }

    async Task<byte[]> LoginReadUntilAsync(ushort expected, CancellationToken ct)
    {
        while (true)
        {
            var (opcode, payload) = await LoginReadFrameAsync(ct);
            if (opcode == expected)
                return payload;
            if (opcode == NetOpcode.Reject)
                throw new InvalidOperationException(NetCodec.DecodeReject(payload).Code);
        }
    }

    async Task<(ushort opcode, byte[] payload)> LoginReadFrameAsync(CancellationToken ct)
    {
        var env = await NetFraming.ReadAsync(_loginStream!, ct)
            ?? throw new InvalidOperationException("login closed");
        return (env.Opcode, env.Payload);
    }

    async Task ZoneWriteAsync(ushort opcode, byte[] payload, CancellationToken ct)
    {
        await NetFraming.WriteAsync(_zoneStream!, new PacketEnvelope
        {
            Opcode = opcode,
            RequestSequence = unchecked(++_zoneSeq),
            Payload = payload
        }, ct);
    }

    async Task<byte[]> ZoneReadUntilAsync(ushort expected, CancellationToken ct)
    {
        while (true)
        {
            var (opcode, payload) = await ZoneReadFrameAsync(ct);
            if (opcode == expected)
                return payload;
            if (opcode == NetOpcode.Reject)
                throw new InvalidOperationException(NetCodec.DecodeReject(payload).Code);
        }
    }

    async Task<(ushort opcode, byte[] payload)> ZoneReadFrameAsync(CancellationToken ct)
    {
        var env = await NetFraming.ReadAsync(_zoneStream!, ct)
            ?? throw new InvalidOperationException("zone closed");
        return (env.Opcode, env.Payload);
    }

    public async ValueTask DisposeAsync()
    {
        _peers.Unregister(BotId);
        await StopReceiveLoopAsync();
        TearDownConnections();
    }
}