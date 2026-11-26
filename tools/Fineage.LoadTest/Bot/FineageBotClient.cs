using System.Diagnostics;
using System.Net.Sockets;
using Fineage.Accounts;
using Fineage.LoadTest.Models;
using Fineage.LoadTest.Travel;
using Fineage.Net.Contract;
using Fineage.Net.Host;

namespace Fineage.LoadTest.Bot;

public sealed partial class FineageBotClient : IAsyncDisposable
{
    TcpClient _login = new();
    TcpClient _zone = new();
    NetworkStream? _loginStream;
    NetworkStream? _zoneStream;
    /// <summary>
    /// One per zone socket. A cancelled read (the enter-settle timer) must not drop the bytes it already took,
    /// otherwise the next read starts mid-frame and the connection dies with <c>bad_frame_length</c>.
    /// </summary>
    NetFrameReader? _zoneReader;
    /// <summary>Zone writes come from the behavior loop and the receive loop (party answers), one frame at a time.</summary>
    readonly SemaphoreSlim _zoneWriteGate = new(1, 1);
    uint _loginSeq;
    uint _zoneSeq;
    readonly Random _rng;
    HandoffMsg? _handoff;
    readonly BotStats _stats;
    readonly BotObservationLog _observation;
    readonly BotPeerDirectory _peers;
    readonly BotBagTracker _bag = new();
    Task? _receiveTask;
    CancellationTokenSource _receiveCts = new();
    ushort _lastRecvOpcode;
    ushort _lastSentOpcode;
    int _spawnX;
    int _spawnY;
    int _spawnZ;

    readonly GatekeeperTraveler? _traveler;

    public FineageBotClient(
        LoadScenario scenario,
        int botId,
        BotStats stats,
        BotPeerDirectory peers,
        GatekeeperDirectory? gatekeepers = null)
    {
        _peers = peers;
        _rng = new Random(botId * 7919 + 17);
        _stats = stats;
        BotId = botId;
        Scenario = scenario;
        Account = $"{scenario.AccountPrefix}{botId:D5}";
        CharacterName = $"Bot{botId:D4}";
        (_spawnX, _spawnY, _spawnZ) = BotSpawn.Position(scenario, botId);
        _observation = new BotObservationLog(botId, _bag);
        if (scenario.IsGatekeeperTraveler(botId))
        {
            if (gatekeepers == null)
                throw new InvalidOperationException($"bot {botId} is a gatekeeper traveller but no GatekeeperDirectory was loaded");
            stats.Travel = new TravelStats();
            _traveler = new GatekeeperTraveler(
                gatekeepers,
                botId * 7919 + 31,
                TimeSpan.FromMilliseconds(scenario.GatekeeperTripCooldownMs),
                stats.Travel);
        }
    }

    public bool IsTraveler => _traveler != null;

    public int BotId { get; }
    public LoadScenario Scenario { get; }
    public string Account { get; }
    public string CharacterName { get; }
    public BotStats Stats => _stats;
    public BotObservationLog ObservationLog => _observation;
    public BotBagTracker Bag => _bag;
    public int SelfObjectId => (int)_selfEntity;
    /// <summary>Tests replay a log without the enter handshake; lets the roster select the replayed self pawn.</summary>
    internal void SetSelfEntityForTest(ulong entityValue) => _selfEntity = entityValue;
    internal Random Rng => _rng;
    internal BotPeerDirectory Peers => _peers;

    ulong _selfEntity;

    public async Task ConnectAndEnterAsync(CancellationToken ct)
    {
        await _login.ConnectAsync(Scenario.LoginHost, Scenario.LoginPort, ct);
        _loginStream = _login.GetStream();
        await LoginHandshakeAsync(ct);

        await _zone.ConnectAsync(_handoff!.ZoneHost, _handoff.ZonePort, ct);
        _zoneStream = _zone.GetStream();
        _zoneReader = new NetFrameReader(_zoneStream);

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
        await PlaceAtScenarioSpawnAsync(ct);
    }

    /// <summary>
    /// EnterWorld resumes the persisted character position, so a scenario that needs a specific start sends the
    /// load-test relocation and waits for the arrival frame before any behaviour starts. Without it a bot whose
    /// character was last saved on a lower geo layer, or in another town, plays from a place the scenario never
    /// intended and every proximity gate (talk, shop, quest) refuses for range.
    /// </summary>
    async Task PlaceAtScenarioSpawnAsync(CancellationToken ct)
    {
        if (!Scenario.TeleportToSpawnOnEnter)
            return;
        int mark = _observation.FrameCount;
        await TeleportToAsync(_spawnX, _spawnY, _spawnZ, ct);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (_observation.ContainsOpcodeAfter(mark, NetOpcode.TeleportToLocation))
                return;
            if (_observation.ContainsRejectAfter(mark, out string? code))
                throw new InvalidOperationException(
                    $"scenario spawn relocation to {_spawnX},{_spawnY},{_spawnZ} rejected: {code}; " +
                    "start the zone with FINEAGE_ZONE_LOADTEST_TELEPORT=1");
            await Task.Delay(25, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"scenario spawn relocation to {_spawnX},{_spawnY},{_spawnZ}: no TeleportToLocation within 10s; " +
            "start the zone with FINEAGE_ZONE_LOADTEST_TELEPORT=1");
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
                _traveler?.OnFrame(opcode, payload);
                await MaybeAcceptPartyInviteAsync(opcode, payload);
                if (opcode == NetOpcode.Reject)
                {
                    string code = NetCodec.DecodeReject(payload).Code;
                    _stats.LastError = code;
                    if (Scenario.Economy is { Enabled: true })
                        _stats.RecordEconomyReject(code);
                }
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
            var (classId, race) = Scenario.ResolveClassAndRace(BotId);
            await LoginExchangeAsync(NetOpcode.CreateCharacter,
                NetCodec.EncodeCreateCharacter(new CreateCharacterMsg
                {
                    Name = CharacterName,
                    ClassId = classId,
                    Race = race,
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

    static readonly TimeSpan EnterSettleCap = TimeSpan.FromSeconds(3);
    static readonly TimeSpan EnterWelcomeTimeout = TimeSpan.FromSeconds(30);

    async Task EnterWorldAsync(CancellationToken ct)
    {
        await ZoneWriteAsync(NetOpcode.EnterWorld, NetCodec.EncodeEnterWorld(new EnterWorldMsg()), ct);
        bool sawWelcome = false;
        var enterStarted = Stopwatch.StartNew();
        TimeSpan welcomeAt = TimeSpan.Zero;
        while (true)
        {
            // Enter ends at the first 400 ms quiet after the welcome, or EnterSettleCap after it: next to a fighting
            // pack the stream never goes quiet, and the bot must start playing rather than wait for silence.
            if (sawWelcome && enterStarted.Elapsed - welcomeAt >= EnterSettleCap)
                return;
            if (!sawWelcome && enterStarted.Elapsed >= EnterWelcomeTimeout)
                throw new TimeoutException($"bot {BotId}: no welcome SystemMessage within {EnterWelcomeTimeout.TotalSeconds:0}s of EnterWorld");
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(ct);
            quiet.CancelAfter(sawWelcome ? TimeSpan.FromMilliseconds(400) : EnterWelcomeTimeout - enterStarted.Elapsed);
            byte[] payload;
            ushort opcode;
            try
            {
                (opcode, payload) = await ZoneReadFrameAsync(quiet.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (sawWelcome)
                    return;
                continue; // the welcome timeout check above throws
            }

            _observation.Record(opcode, payload, DateTimeOffset.UtcNow);
            _traveler?.OnFrame(opcode, payload);
            if (opcode == NetOpcode.Reject)
                throw new InvalidOperationException(NetCodec.DecodeReject(payload).Code);
            if (opcode == NetOpcode.SystemMessage && !sawWelcome)
            {
                sawWelcome = true;
                welcomeAt = enterStarted.Elapsed;
            }
            if (opcode == NetOpcode.EntitySpawn)
            {
                var spawn = NetCodec.DecodeEntitySpawn(payload);
                if (spawn.IsSelf)
                    _selfEntity = spawn.EntityValue;
            }
        }
    }

    async Task RunLegacyMixedBehaviorTickAsync(IReadOnlyList<FineageBotClient> fleet, CancellationToken ct)
    {
        if (_zoneStream == null || _stats.Disconnected)
            return;

        if (_traveler != null)
        {
            await RunTravelTickAsync(ct);
            return;
        }

        string action = PickAction();
        var sw = Stopwatch.StartNew();
        try
        {
            if (action.StartsWith("economy_", StringComparison.Ordinal))
            {
                await BotEconomyFlows.RunActionAsync(this, action, fleet, ct);
                Interlocked.Increment(ref _stats.ActionsAttempted);
                _stats.LastActionAt = DateTimeOffset.UtcNow;
                sw.Stop();
                _stats.RecordRtt(sw.Elapsed.TotalMilliseconds);
                return;
            }

            await RunLegacyActionAsync(action, fleet, ct);
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

    async Task RunLegacyActionAsync(string action, IReadOnlyList<FineageBotClient> fleet, CancellationToken ct)
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
                        int skillId = Scenario.ResolveCombatSkillId();
                        await ZoneWriteAsync(NetOpcode.SelectTarget,
                            NetCodec.EncodeSelectTarget(new SelectTargetNetMsg { TargetObjectId = target }), ct);
                        await SendTrackedAsync(
                            NetOpcode.UseSkill,
                            NetCodec.EncodeUseSkill(new UseSkillNetMsg { SkillId = skillId, TargetObjectId = target, CtrlPressed = false }),
                            ct,
                            targetObjectId: target,
                            skillId: skillId);
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
            default:
                throw new InvalidOperationException($"bot {BotId}: action '{action}' has no implementation.");
        }
    }

    /// <summary>Gatekeeper travellers run only the travel loop; see <see cref="GatekeeperTraveler"/>.</summary>
    async Task RunTravelTickAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var next = _traveler!.Step(DateTimeOffset.UtcNow);
            if (next is { } command)
                await SendTrackedAsync(command.Opcode, command.Payload, ct);
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

    internal async Task SendTrackedAsync(
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

    public int? VisiblePlayerObjectId()
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
        _zoneReader = new NetFrameReader(_zoneStream);
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
        _zoneReader = null;
        _loginStream = null;
    }

    /// <summary>Weighted pick over the whole mix: weights are relative, so a mix that sums past 1 cannot shadow its tail.</summary>
    internal string PickAction()
    {
        IEnumerable<KeyValuePair<string, double>> weights = Scenario.ActionWeights;
        if (Scenario.Economy is { Enabled: true } eco)
        {
            int mod = Math.Abs(BotId) % eco.ManufacturerModulo;
            weights = weights.Where(pair => pair.Key switch
            {
                "economy_manufacture_maker" => mod == eco.ManufacturerRemainder,
                "economy_manufacture_customer" => mod == 1,
                _ => true
            });
        }

        double roll = _rng.NextDouble();
        double acc = 0;
        string last = "";
        foreach (var (name, weight) in weights)
        {
            if (weight <= 0)
                continue;
            acc += weight;
            last = name;
            if (roll < acc)
                return name;
        }

        return last;
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
        // The behavior loop and the receive loop (party answers) both write to this socket. NetFraming.WriteAsync
        // frames and writes without a lock, so two interleaved writes would splice their bytes and the server
        // would read a bogus length. One writer at a time.
        await _zoneWriteGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await NetFraming.WriteAsync(_zoneStream!, new PacketEnvelope
            {
                Opcode = opcode,
                RequestSequence = unchecked(++_zoneSeq),
                Payload = payload
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            _zoneWriteGate.Release();
        }
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
        var env = await _zoneReader!.ReadAsync(ct)
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