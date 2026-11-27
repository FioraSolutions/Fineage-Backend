using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>Mobius party matching room opcodes (#928).</summary>
public sealed class PartyMatchPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public PartyMatchPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnConfigAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePartyMatchConfig(envelope.Payload);
        string? error = await _world.TryPartyMatchConfigAsync(zoneSession, new PartyMatchConfigMsg
        {
            Auto = net.Auto,
            Location = net.Location,
            Level = net.Level
        }, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party match config failed.", ct).ConfigureAwait(false);
    }

    public async Task OnListAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePartyMatchList(envelope.Payload);
        string? error = await _world.TryPartyMatchListAsync(zoneSession, new PartyMatchListMsg
        {
            RoomId = net.RoomId,
            MaxMembers = net.MaxMembers,
            MinLevel = net.MinLevel,
            MaxLevel = net.MaxLevel,
            Loot = net.Loot,
            Title = net.Title ?? ""
        }, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party match list failed.", ct).ConfigureAwait(false);
    }

    public async Task OnDetailAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePartyMatchDetailRequest(envelope.Payload);
        string? error = await _world.TryPartyMatchDetailAsync(zoneSession, new PartyMatchDetailRequestMsg
        {
            RoomId = net.RoomId,
            Unk1 = net.Unk1,
            Unk2 = net.Unk2,
            Unk3 = net.Unk3
        }, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party match join failed.", ct).ConfigureAwait(false);
    }

    public async Task OnDismissAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePartyMatchDismiss(envelope.Payload);
        string? error = await _world.TryPartyRoomDismissAsync(zoneSession, net.RoomId, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party room dismiss failed.", ct).ConfigureAwait(false);
    }

    public async Task OnWithdrawAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePartyMatchWithdraw(envelope.Payload);
        string? error = await _world.TryPartyRoomWithdrawAsync(zoneSession, net.RoomId, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party room withdraw failed.", ct).ConfigureAwait(false);
    }

    public async Task OnOustAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePartyMatchOust(envelope.Payload);
        string? error = await _world.TryPartyRoomOustAsync(zoneSession, net.ObjectId, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party room oust failed.", ct).ConfigureAwait(false);
    }

    public async Task OnAskJoinAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePartyMatchAskJoin(envelope.Payload);
        string? error = await _world.TryPartyRoomAskJoinAsync(zoneSession, net.Name ?? "", ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party room invite failed.", ct).ConfigureAwait(false);
    }

    public async Task OnAnswerJoinAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePartyMatchAnswerJoin(envelope.Payload);
        string? error = await _world.TryPartyRoomAnswerJoinAsync(zoneSession, net.Answer, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party room answer failed.", ct).ConfigureAwait(false);
    }

    public async Task OnWaitingListAsync(NetConnection conn, ZoneSession zoneSession, PacketEnvelope envelope, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        var net = NetCodec.DecodePartyMatchWaitingListRequest(envelope.Payload);
        string? error = await _world.TryPartyMatchWaitingListAsync(zoneSession, new PartyMatchWaitingListRequestMsg
        {
            Page = net.Page,
            MinLevel = net.MinLevel,
            MaxLevel = net.MaxLevel,
            Mode = net.Mode
        }, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party match waiting list failed.", ct).ConfigureAwait(false);
    }

    public async Task OnWaitingExitAsync(NetConnection conn, ZoneSession zoneSession, CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;
        string? error = await _world.TryPartyMatchWaitingExitAsync(zoneSession, ct).ConfigureAwait(false);
        if (error != null)
            await _reject(conn, error, "Party match waiting exit failed.", ct).ConfigureAwait(false);
    }
}
