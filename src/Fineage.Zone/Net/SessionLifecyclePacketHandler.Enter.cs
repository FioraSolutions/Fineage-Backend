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
    public async Task OnEnterAsync(NetConnection conn, ZoneSession zoneSession, EntityId self, CancellationToken ct)
    {
        RequireHello(conn);
        if (!zoneSession.Bound)
        {
            Console.Error.WriteLine("[ZONE] enter rejected auth_required (no handoff bind)");
            await _reject(conn, "auth_required", "Auth with a handoff token first.", ct).ConfigureAwait(false);
            return;
        }

        var burst = await _world.EnterAsync(zoneSession, entity => PlanEnter(zoneSession, self, entity), ct)
            .ConfigureAwait(false);
        if (burst == null)
        {
            Console.Error.WriteLine($"[ZONE] enter rejected enter_failed account={zoneSession.Account} char={zoneSession.ObjectId} name={zoneSession.Name}");
            await _reject(conn, "enter_failed", "Could not enter world.", ct).ConfigureAwait(false);
            return;
        }

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
    /// On the simulation thread, in the step the player entered: the enter burst for the player's connection, then
    /// its spawn for the peers that see it.
    /// </summary>
    List<NetFrame> PlanEnter(ZoneSession zoneSession, EntityId self, ZoneEntity entity)
    {
        var frames = new List<NetFrame>();
        void Self(ushort opcode, byte[] payload) => frames.Add(NetFrame.ToSelf(opcode, payload));

        // Single source of truth: ZoneEntity after TryEnter (inventory, vitals, AutoSkills already applied).
        var packets = NetEnterWorldProjector.FromEntity(entity, self, _clock().ToUnixTimeMilliseconds(), _playerSpawnPAtkSpd(entity));
        Self(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(packets.Spawn));
        Self(NetOpcode.PlayerStats, NetCodec.EncodePlayerStats(packets.Stats));
        // Mobius enter: UserInfo (full sheet + paperdoll) before ItemList so the status window and model are dressed.
        if (_world.TryBuildNetUserInfo(zoneSession, out var userInfo))
            Self(NetOpcode.UserInfo, NetCodec.EncodeUserInfo(NetZoneServer.ToNetUserInfo(userInfo)));
        Self(NetOpcode.ItemList, NetCodec.EncodeItemList(packets.Items));
        Self(NetOpcode.SkillList, NetCodec.EncodeSkillList(NetEnterWorldProjector.ToNetSkillList(packets.Skills)));
        // Mobius EnterWorld: ShortCutInit after ItemList / SkillList so item and skill icons resolve.
        Self(NetOpcode.ShortCutInit, NetCodec.EncodeShortCutInit(ToNetShortcutInit(_world.BuildShortcutInit(zoneSession))));
        if (_world.TryBuildPlayerActionStateNet(entity, out var actionState))
            Self(NetOpcode.PlayerActionState, NetCodec.EncodePlayerActionState(actionState));
        // Mobius updateEffectIcons: restored buffs (character_skills_save) must land before EtcStatusUpdate.
        if (_world.TryBuildNetAbnormalStatus(zoneSession, out var abnormals))
            Self(NetOpcode.AbnormalStatusUpdate, NetCodec.EncodeAbnormalStatusUpdate(ToNetAbnormal(abnormals)));
        // Mobius EnterWorld:401 sends EtcStatusUpdate after ItemList/ShortCutInit and after updateEffectIcons, so the
        // overload icon appears on an otherwise fully-dressed status bar.
        if (_world.TryBuildNetEtcStatus(zoneSession, out var etcStatus))
            Self(NetOpcode.EtcStatusUpdate, NetCodec.EncodeEtcStatusUpdate(NetZoneServer.ToNetEtcStatus(etcStatus, zoneSession.ObjectId)));
        // Journal on enter so the quest tabs are filled before the player presses the menu button.
        Self(NetOpcode.QuestList, NetCodec.EncodeQuestList(NetZoneServer.ToNetQuestList(_world.NetQuestList(zoneSession))));
        // Mobius EnterWorld:561 — a returning clan member gets the pledge sheet and roster up front,
        // otherwise the clan window is blank until some later clan action fans a status out.
        if (_world.TryBuildNetClanSnapshot(zoneSession, out var clanStatus, out var clanMembers))
        {
            Self(NetOpcode.PledgeStatus, NetCodec.EncodePledgeStatus(NetZoneServer.ToNetPledgeStatus(clanStatus)));
            Self(NetOpcode.PledgeMemberList, NetCodec.EncodePledgeMemberList(NetZoneServer.ToNetPledgeMemberList(clanMembers)));
        }

        var welcome = SayRules.Welcome(zoneSession.Name);
        Self(NetOpcode.SystemMessage, NetCodec.EncodeSystemMessage(new SystemMessageNetMsg
        {
            Id = welcome.Id,
            Text = welcome.Text,
            Params = welcome.Params ?? []
        }));

        var known = _world.SnapshotKnownEntities(zoneSession);
        foreach (var other in known)
        {
            var otherId = _ids.AllocateOrGet(other.ObjectId);
            if (other.Kind == ZoneEntityKind.GroundItem)
            {
                Self(NetOpcode.DropItem, NetCodec.EncodeDropItem(new DropItemNetMsg
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
                }));
                continue;
            }

            var remote = NetEnterWorldProjector.BuildPresenceSpawn(other, otherId, _playerSpawnPAtkSpd(other));
            Self(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(remote));
            // Late join: a peer mid-run / seated is shown in that state, not idle at a stale spot.
            foreach (var (opcode, payload) in NetEnterWorldProjector.BuildPresenceFollowUps(other, otherId, _world))
                Self(opcode, payload);
            // Clan war / alliance colours for the pawn just shown (Mobius RelationChanged after CharInfo).
            if (_world.SpawnRelationFrame(other, entity) is { } relation)
                Self(NetOpcode.RelationChanged, relation);
        }

        var selfRemote = NetCodec.EncodeEntitySpawn(
            NetEnterWorldProjector.BuildRemoteSpawn(entity, self, _playerSpawnPAtkSpd(entity)));
        NetEnterSpawnRecord.AppendPeerSpawns(frames, entity, self, selfRemote, known, _world);

        return frames;
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
