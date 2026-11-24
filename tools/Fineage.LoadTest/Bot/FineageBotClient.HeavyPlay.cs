using Fineage.Net.Contract;

namespace Fineage.LoadTest.Bot;

public sealed partial class FineageBotClient
{
    HeavyPlayRole _heavyRole;
    bool _heavyRoleSet;

    public int SpawnX => _spawnX;
    public int SpawnY => _spawnY;
    public int SpawnZ => _spawnZ;

    internal void EnsureHeavyPlayRole()
    {
        if (_heavyRoleSet)
            return;
        _heavyRole = HeavyPlayRoles.Resolve(Scenario, BotId);
        _heavyRoleSet = true;
    }

    public async Task RunBehaviorTickAsync(IReadOnlyList<FineageBotClient> fleet, CancellationToken ct)
    {
        if (_zoneStream == null || _stats.Disconnected)
            return;

        EnsureHeavyPlayRole();
        if (await HeavyPlayBehavior.TryRunTickAsync(this, fleet, _heavyRole, ct))
        {
            Interlocked.Increment(ref _stats.ActionsAttempted);
            _stats.LastActionAt = DateTimeOffset.UtcNow;
            return;
        }

        await RunMixedBehaviorTickAsync(fleet, ct);
    }

    internal async Task RunMixedBehaviorTickAsync(IReadOnlyList<FineageBotClient> fleet, CancellationToken ct) =>
        await RunLegacyMixedBehaviorTickAsync(fleet, ct);

    internal async Task ZoneWritePublicAsync(ushort opcode, byte[] payload, CancellationToken ct)
    {
        _lastSentOpcode = opcode;
        await ZoneWriteAsync(opcode, payload, ct);
    }

    /// <summary>NPC the fighter is auto-attacking; 0 when it needs a new one.</summary>
    internal int FightTargetObjectId { get; set; }

    /// <summary>Fighter ticks since enter; drives the skill rotation order.</summary>
    internal int FightTicks { get; set; }

    /// <summary>When the fighter last sent AttackRequest (re-sent after a cast, which drops auto-attack).</summary>
    internal DateTimeOffset LastAttackSentAt { get; set; }

    internal async Task MaybeAcceptPartyInviteAsync(ushort opcode, byte[] payload)
    {
        if (opcode != NetOpcode.PartyAskJoin || _zoneStream == null)
            return;
        var ask = NetCodec.DecodePartyAskJoin(payload);
        if (ask.RequestorObjectId <= 0)
            return;
        try
        {
            await ZoneWriteAsync(
                NetOpcode.PartyAnswerJoinRequest,
                NetCodec.EncodePartyAnswerJoinRequest(new PartyAnswerJoinRequestNetMsg
                {
                    RequestorObjectId = ask.RequestorObjectId,
                    Response = 1
                }),
                CancellationToken.None);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The zone socket closed while answering; the receive loop reports the disconnect.
            _stats.LastError = $"party answer: {ex.GetType().Name}: {ex.Message}";
        }
    }
}