using System.Diagnostics;
using System.Net.Sockets;
using Fineage.Accounts;
using Fineage.LoadTest.Models;
using Fineage.Net.Contract;
using Fineage.Net.Host;

namespace Fineage.LoadTest.Bot;

public sealed class FineageBotClient : IAsyncDisposable
{
    readonly TcpClient _login = new();
    readonly TcpClient? _zone;
    NetworkStream? _loginStream;
    NetworkStream? _zoneStream;
    uint _loginSeq;
    uint _zoneSeq;
    readonly Random _rng;
    HandoffMsg? _handoff;
    ulong _selfEntity;
    readonly BotStats _stats;

    public FineageBotClient(LoadScenario scenario, int botId, BotStats stats)
    {
        _rng = new Random(botId * 7919 + 17);
        _stats = stats;
        BotId = botId;
        Scenario = scenario;
        Account = $"{scenario.AccountPrefix}{botId:D5}";
        CharacterName = $"Bot{botId:D4}";
        _zone = new TcpClient();
    }

    public int BotId { get; }
    public LoadScenario Scenario { get; }
    public string Account { get; }
    public string CharacterName { get; }
    public BotStats Stats => _stats;

    public async Task ConnectAndEnterAsync(CancellationToken ct)
    {
        await _login.ConnectAsync(Scenario.LoginHost, Scenario.LoginPort, ct);
        _loginStream = _login.GetStream();
        await LoginHandshakeAsync(ct);

        await _zone!.ConnectAsync(_handoff!.ZoneHost, _handoff.ZonePort, ct);
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

        int characterId;
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
        characterId = pick.CharacterId;

        var handoffPayload = await LoginExchangePayloadAsync(NetOpcode.SelectCharacter,
            NetCodec.EncodeSelectCharacter(new SelectCharacterMsg { CharacterId = characterId }),
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

    public async Task RunBehaviorTickAsync(CancellationToken ct)
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
                    await ZoneWriteAsync(NetOpcode.MoveInput,
                        NetCodec.EncodeMoveInput(new MoveInputMsg
                        {
                            DestX = Scenario.SpawnX + dx,
                            DestY = Scenario.SpawnY + dy,
                            DestZ = Scenario.SpawnZ
                        }), ct);
                    break;
                case "attack":
                    await ZoneWriteAsync(NetOpcode.AttackRequest,
                        NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = (int)(_selfEntity + 1) }), ct);
                    break;
                case "skill":
                    await ZoneExchangeAsync(NetOpcode.UseSkill,
                        NetCodec.EncodeUseSkill(new UseSkillNetMsg { SkillId = 3, CtrlPressed = false }),
                        NetOpcode.Reject, ct, optionalResponse: true);
                    break;
                case "say":
                    await ZoneWriteAsync(NetOpcode.Say,
                        NetCodec.EncodeSay(new SayNetMsg { Text = $"load {BotId}", ChatType = 0 }), ct);
                    break;
                case "pickup":
                    await ZoneWriteAsync(NetOpcode.PickupRequest,
                        NetCodec.EncodePickupRequest(new PickupRequestNetMsg { GroundObjectId = 1 }), ct);
                    break;
                case "trade":
                    await ZoneWriteAsync(NetOpcode.TradeRequest,
                        NetCodec.EncodeTradeRequest(new TradeRequestNetMsg { TargetObjectId = (int)_selfEntity + 2 }), ct);
                    break;
                case "party":
                    await ZoneWriteAsync(NetOpcode.PartyInviteRequest,
                        NetCodec.EncodePartyInviteRequest(new PartyInviteRequestNetMsg { TargetObjectId = (int)_selfEntity + 3 }), ct);
                    break;
                case "teleport":
                    await ZoneExchangeAsync(NetOpcode.TeleportToLocation,
                        NetCodec.EncodeTeleportToLocation(new TeleportToLocationNetMsg
                        {
                            X = Scenario.SpawnX,
                            Y = Scenario.SpawnY,
                            Z = Scenario.SpawnZ
                        }),
                        NetOpcode.Reject, ct, optionalResponse: true);
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

    async Task ZoneExchangeAsync(ushort send, byte[] payload, ushort expect, CancellationToken ct, bool optionalResponse = false)
    {
        var sw = Stopwatch.StartNew();
        await ZoneWriteAsync(send, payload, ct);
        if (optionalResponse)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromMilliseconds(800));
                await ZoneReadFrameAsync(cts.Token);
            }
            catch { /* fire-and-forget friendly */ }
        }
        else
        {
            ushort opcode = (await ZoneReadFrameAsync(ct)).opcode;
            if (opcode != expect && opcode != NetOpcode.Reject)
                throw new InvalidOperationException($"expected 0x{expect:X4}");
        }

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

    public ValueTask DisposeAsync()
    {
        try { _zoneStream?.Dispose(); } catch { /* ignore */ }
        try { _loginStream?.Dispose(); } catch { /* ignore */ }
        try { _zone?.Dispose(); } catch { /* ignore */ }
        try { _login.Dispose(); } catch { /* ignore */ }
        return ValueTask.CompletedTask;
    }
}