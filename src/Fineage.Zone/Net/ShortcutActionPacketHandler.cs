using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// Action use / auto soul shot / shortcut opcodes extracted from <see cref="NetZoneServer"/> (B2b / #353).
/// </summary>
public sealed class ShortcutActionPacketHandler
{
    readonly ZoneWorld _world;
    readonly Func<NetConnection, string, string, CancellationToken, Task> _reject;

    public ShortcutActionPacketHandler(
        ZoneWorld world,
        Func<NetConnection, string, string, CancellationToken, Task> reject)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _reject = reject ?? throw new ArgumentNullException(nameof(reject));
    }

    public async Task OnActionUseAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;

        var use = NetCodec.DecodeActionUse(envelope.Payload);
        // Net renders sit/stand (ChangeWaitType), the walk/run toggle (ChangeMoveType),
        // next-target (MyTargetSelected), panel Attack (AttackRequest) and the three social
        // gestures (SocialAction); the rest of Mobius' action table is still V2-only.
        if (use.ActionId is not (ActionProtocol.SitStand or ActionProtocol.WalkRun
            or ActionProtocol.Attack or ActionProtocol.NextTarget
            or ActionProtocol.SocialGreeting or ActionProtocol.SocialVictory or ActionProtocol.SocialAdvance))
        {
            await _reject(conn, ActionProtocol.Unsupported, $"action {use.ActionId} not on Net yet", ct).ConfigureAwait(false);
            return;
        }

        string? actionError = await _world.TryCharacterActionAsync(
            zoneSession, use.ActionId, targetObjectId: 0, ct).ConfigureAwait(false);
        if (actionError != null)
            await _reject(conn, actionError, "Action failed.", ct).ConfigureAwait(false);
    }

    public async Task OnAutoSoulShotAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;

        var auto = NetCodec.DecodeAutoSoulShot(envelope.Payload);
        string? autoError = await _world.TryAutoSoulShotAsync(zoneSession, auto.ItemId, auto.Enabled, ct).ConfigureAwait(false);
        if (autoError != null)
            await _reject(conn, autoError, "Auto shot failed.", ct).ConfigureAwait(false);
    }

    public async Task OnShortCutRegAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;

        var reg = NetCodec.DecodeShortCut(envelope.Payload);
        var regReply = _world.RegisterShortcut(zoneSession, reg.Type, reg.Slot, reg.Id, reg.Level);
        await SendShortcutReplyAsync(conn, regReply, envelope.RequestSequence, ct).ConfigureAwait(false);
    }

    public async Task OnShortCutDelAsync(
        NetConnection conn,
        ZoneSession zoneSession,
        PacketEnvelope envelope,
        CancellationToken ct)
    {
        if (!zoneSession.Bound || !zoneSession.InWorld)
            return;

        var del = NetCodec.DecodeShortCutDel(envelope.Payload);
        var delReply = _world.DeleteShortcut(zoneSession, del.Slot);
        await SendShortcutReplyAsync(conn, delReply, envelope.RequestSequence, ct).ConfigureAwait(false);
    }

    static async Task SendShortcutReplyAsync(NetConnection conn, ZoneWorld.ShortcutReply reply, uint sequence, CancellationToken ct)
    {
        if (reply.Register != null)
            await conn.SendAsync(NetOpcode.ShortCutRegister, NetCodec.EncodeShortCut(reply.Register), sequence, ct).ConfigureAwait(false);
        else if (reply.DeleteSlot is int slot)
            await conn.SendAsync(NetOpcode.ShortCutDelete, NetCodec.EncodeShortCutDel(new ShortCutDelNetMsg { Slot = slot }), sequence, ct).ConfigureAwait(false);
        if (reply.Error != null)
            ZoneLog.Info($"shortcut rejected: {reply.Error}");
    }
}
