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

    internal string PickEconomyOrTeleportAction()
    {
        var hp = Scenario.HeavyPlay;
        double roll = _rng.NextDouble();
        if (hp != null && roll < hp.EconomyTeleportWeight)
            return "teleport";
        return PickAction();
    }

    internal async Task RunWeightedActionAsync(string action, IReadOnlyList<FineageBotClient> fleet, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (action.StartsWith("economy_", StringComparison.Ordinal))
            {
                await BotEconomyFlows.RunActionAsync(this, action, fleet, ct);
                return;
            }

            await RunLegacyActionAsync(action, fleet, ct);
        }
        finally
        {
            Interlocked.Increment(ref _stats.ActionsAttempted);
            _stats.LastActionAt = DateTimeOffset.UtcNow;
            sw.Stop();
            _stats.RecordRtt(sw.Elapsed.TotalMilliseconds);
        }
    }

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
        catch
        {
            /* disconnect */
        }
    }
}