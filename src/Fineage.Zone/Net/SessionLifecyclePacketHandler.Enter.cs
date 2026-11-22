using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol;
using Fineage.Zone;
using Fineage.Zone.Chat;
using Fineage.Zone.World;
using NetHelloAck = Fineage.Net.Contract.HelloAckMsg;

namespace Fineage.Zone.Net;

sealed partial class SessionLifecyclePacketHandler
{
    public async Task OnEnterAsync(
        NetConnection conn, ZoneSession zoneSession, EntityId self, PacketEnvelope envelope, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound)
        {
            Console.Error.WriteLine("[ZONE] enter rejected auth_required (no handoff bind)");
            await _reject(conn, "auth_required", "Auth with a handoff token first.", ct).ConfigureAwait(false);
            return;
        }

        var enter = NetCodec.DecodeEnterWorld(envelope.Payload);
        _world.ApplyPlayerLanguage(zoneSession, enter.Language);

        var snapshot = await _world.EnterWithSnapshotAsync(
                zoneSession, entity => CommitEnter(zoneSession, self, entity), ct)
            .ConfigureAwait(false);
        if (snapshot == null)
        {
            Console.Error.WriteLine($"[ZONE] enter rejected enter_failed account={zoneSession.Account} char={zoneSession.ObjectId} name={zoneSession.Name}");
            await _reject(conn, "enter_failed", "Could not enter world.", ct).ConfigureAwait(false);
            return;
        }

        var burst = await EnterEncoder.EncodeAndDispatchAsync(snapshot, ct).ConfigureAwait(false);

        Console.WriteLine($"[ZONE] enter ok account={zoneSession.Account} char={zoneSession.ObjectId} name={zoneSession.Name} obj={zoneSession.ObjectId}");
        await NetFrame.SendAsync(conn, _presence, burst, ct).ConfigureAwait(false);
        // Quest marks and journal effects fan out through the world, so they follow the burst instead of overtaking it.
        await _world.OnSimulationAsync(zoneSession, "quest-client-fx", () =>
        {
            _world.NetQuestClientFx(zoneSession);
            return 0;
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// On the simulation thread: immutable enter snapshot for off-thread encoding. No NetCodec on known entities.
    /// </summary>
    EnterSnapshot CommitEnter(ZoneSession zoneSession, EntityId self, ZoneEntity entity)
    {
        using var _ = EnterMetrics.MeasurePlan();
        var selfSlots = new List<EnterSelfSlot>();
        IReadOnlyList<KnownPresenceSlot> known;
        EntitySpawnMsg selfRemote;
        int knownPlayers;

        using (EnterMetrics.MeasureSelfProjection())
        {
            long nowMs = _clock().ToUnixTimeMilliseconds();
            int pAtkSpd = _playerSpawnPAtkSpd(entity);
            var packets = NetEnterWorldProjector.FromEntityCore(entity, self, nowMs, pAtkSpd);
            selfSlots.Add(new EnterSelfSlot(NetOpcode.EntitySpawn, packets.Spawn));
            selfSlots.Add(new EnterSelfSlot(NetOpcode.PlayerStats, packets.Stats));
            if (_world.TryBuildNetUserInfo(zoneSession, out var userInfo))
                selfSlots.Add(new EnterSelfSlot(NetOpcode.UserInfo, userInfo));
            var items = zoneSession.PendingEnterSelfPrepare?.Items;
            selfSlots.Add(items != null
                ? new EnterSelfSlot(NetOpcode.ItemList, items)
                : new EnterSelfSlot(NetOpcode.ItemList, entity.Inventory!));
            selfSlots.Add(new EnterSelfSlot(NetOpcode.SkillList, EnterSkillCapture.From(entity, nowMs)));
            selfSlots.Add(new EnterSelfSlot(NetOpcode.ShortCutInit, _world.BuildShortcutInit(zoneSession)));
            if (_world.TryBuildPlayerActionStateNet(entity, out var actionState))
                selfSlots.Add(new EnterSelfSlot(NetOpcode.PlayerActionState, actionState));
            if (_world.TryBuildNetAbnormalStatus(zoneSession, out var abnormals))
                selfSlots.Add(new EnterSelfSlot(NetOpcode.AbnormalStatusUpdate, abnormals));
            if (_world.TryBuildNetEtcStatus(zoneSession, out var etcStatus))
                selfSlots.Add(new EnterSelfSlot(NetOpcode.EtcStatusUpdate, etcStatus));
            selfSlots.Add(new EnterSelfSlot(NetOpcode.QuestList, _world.NetQuestList(zoneSession)));
            if (_world.TryBuildNetClanSnapshot(zoneSession, out var clanStatus, out var clanMembers))
            {
                selfSlots.Add(new EnterSelfSlot(NetOpcode.PledgeStatus, clanStatus));
                selfSlots.Add(new EnterSelfSlot(NetOpcode.PledgeMemberList, clanMembers));
            }

            var welcome = _world.Localize(zoneSession, SayRules.Welcome(zoneSession.Name));
            selfSlots.Add(new EnterSelfSlot(NetOpcode.SystemMessage, new SystemMessageNetMsg
            {
                Id = welcome.Id,
                Text = welcome.Text,
                Params = welcome.Params ?? []
            }));

            selfRemote = NetEnterWorldProjector.BuildRemoteSpawn(entity, self, _playerSpawnPAtkSpd(entity));
        }

        List<PeerFanoutSlot> peerFanout;
        using (EnterMetrics.MeasureKnownSnapshot())
            (known, peerFanout) = SnapshotKnownPresence(zoneSession, entity);

        knownPlayers = known.Count(row => row is { GroundItem: false, Kind: EntityKindNet.Player });

        return new EnterSnapshot
        {
            SessionObjectId = zoneSession.ObjectId,
            Self = selfSlots,
            SelfRemoteSpawn = selfRemote,
            Known = known,
            PeerFanout = peerFanout,
            KnownEntityCount = known.Count,
            KnownPlayerCount = knownPlayers
        };
    }

    (List<KnownPresenceSlot> Known, List<PeerFanoutSlot> Peers) SnapshotKnownPresence(ZoneSession zoneSession, ZoneEntity entity)
    {
        var known = _world.SnapshotKnownEntities(zoneSession);
        var rows = new List<KnownPresenceSlot>(known.Count);
        var peers = new List<PeerFanoutSlot>();
        foreach (var other in known)
        {
            var otherId = _ids.AllocateOrGet(other.ObjectId);
            if (other.Kind == ZoneEntityKind.GroundItem)
            {
                rows.Add(new KnownPresenceSlot
                {
                    SourceObjectId = other.ObjectId,
                    GroundItem = true,
                    Drop = new DropItemNetMsg
                    {
                        DropperObjectId = 0,
                        EntityValue = otherId.Value,
                        EntityGeneration = otherId.Generation,
                        ItemId = other.GroundItemId,
                        X = other.X,
                        Y = other.Y,
                        Z = other.Z,
                        Stackable = other.GroundStackable,
                        Count = other.GroundCount
                    }
                });
                continue;
            }

            var followUps = NetEnterWorldProjector.PlanPresenceFollowUps(other, otherId, _world);
            byte[]? relation = _world.SpawnRelationFrame(other, entity);
            if (other.Kind == ZoneEntityKind.Npc)
            {
                rows.Add(new KnownPresenceSlot
                {
                    SourceObjectId = other.ObjectId,
                    Kind = EntityKindNet.Npc,
                    Npc = NetEnterWorldProjector.CaptureNpcPresence(other, otherId),
                    FollowUps = followUps,
                    RelationToEntrant = relation
                });
                continue;
            }

            var remote = NetEnterWorldProjector.BuildPresenceSpawn(other, otherId, _playerSpawnPAtkSpd(other));
            rows.Add(new KnownPresenceSlot
            {
                SourceObjectId = other.ObjectId,
                Kind = remote.Kind,
                Spawn = remote,
                FollowUps = followUps,
                RelationToEntrant = relation
            });

            if (other.Kind == ZoneEntityKind.Player)
                peers.Add(new PeerFanoutSlot(other.ObjectId, _world.SpawnRelationFrame(entity, other)));
        }

        return (rows, peers);
    }

    public async Task LeaveAsync(ZoneSession zoneSession, EntityId self, int registeredObjectId, NetConnection conn)
    {
        List<int>? peers = null;
        try
        {
            peers = await _world.LeaveAsync(zoneSession, _ => _world.SnapshotKnownPlayers(zoneSession).ConvertAll(peer => peer.ObjectId),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            ZoneLog.Warn(
                $"leave detach failed session={zoneSession.WireSessionId} account={zoneSession.Account} char={zoneSession.ObjectId}: {ex}");
        }

        try
        {
            if (peers != null && zoneSession.Bound && self.Value != 0)
            {
                var despawn = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
                {
                    EntityValue = self.Value,
                    EntityGeneration = self.Generation
                });
                foreach (int peer in peers)
                    await _presence.SendAsync(peer, NetOpcode.EntityDespawn, despawn, CancellationToken.None)
                        .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            ZoneLog.Warn(
                $"leave despawn failed session={zoneSession.WireSessionId} account={zoneSession.Account} char={zoneSession.ObjectId}: {ex}");
        }

        if (registeredObjectId != 0)
            _presence.Unregister(registeredObjectId, conn);
    }

    internal EnterSnapshot CommitEnterForTest(ZoneSession zoneSession, EntityId self, ZoneEntity entity) =>
        CommitEnter(zoneSession, self, entity);

    static void RequireHello(NetConnection conn)
    {
        if (!conn.HelloCompleted)
            throw new InvalidOperationException("hello_required");
    }

    internal static ShortCutInitNetMsg ToNetShortcutInit(ShortCutInitMsg init)
    {
        var src = init.Shortcuts ?? [];
        var rows = new ShortCutNetMsg[src.Length];
        for (int i = 0; i < src.Length; i++)
            rows[i] = ShortcutActionPacketHandler.ToNet(src[i]);
        return new ShortCutInitNetMsg { Shortcuts = rows };
    }

    internal static AbnormalStatusUpdateNetMsg ToNetAbnormal(AbnormalStatusUpdateMsg update)
    {
        var src = update.Effects ?? [];
        var rows = new AbnormalEffectNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var e = src[i];
            rows[i] = new AbnormalEffectNet
            {
                Id = e.Id,
                Value = e.Value,
                DurationSeconds = e.DurationSeconds,
                Slot = e.Slot ?? "",
                Visual = e.Visual ?? ""
            };
        }

        return new AbnormalStatusUpdateNetMsg { ObjectId = update.ObjectId, Effects = rows };
    }
}