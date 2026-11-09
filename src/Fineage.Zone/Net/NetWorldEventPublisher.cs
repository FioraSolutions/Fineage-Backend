using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// World → Net presence fanout: Protocol/primitives in, wire encode out (#353 / B2b).
/// Extracted from <see cref="NetZoneServer"/> so the listener stays dispatch + lifecycle.
/// </summary>
sealed partial class NetWorldEventPublisher : IWorldEventPublisher
{
    readonly ZoneWorld _world;
    readonly EntityIdMap _ids;
    readonly NetPresenceHub _presence;
    readonly Func<ZoneEntity, int> _playerSpawnPAtkSpd;

    public NetWorldEventPublisher(
        ZoneWorld world,
        EntityIdMap ids,
        NetPresenceHub presence,
        Func<ZoneEntity, int> playerSpawnPAtkSpd)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
        _presence = presence ?? throw new ArgumentNullException(nameof(presence));
        _playerSpawnPAtkSpd = playerSpawnPAtkSpd ?? throw new ArgumentNullException(nameof(playerSpawnPAtkSpd));
    }

    void IWorldEventPublisher.FanoutMoveState(
        IReadOnlyList<int> watcherObjectIds,
        int moverObjectId,
        int originX,
        int originY,
        int originZ,
        int destX,
        int destY,
        int destZ,
        bool running)
    {
        var mover = _ids.AllocateOrGet(moverObjectId);
        var payload = NetCodec.EncodeMoveState(new MoveStateMsg
        {
            EntityValue = mover.Value,
            EntityGeneration = mover.Generation,
            OriginX = originX,
            OriginY = originY,
            OriginZ = originZ,
            DestX = destX,
            DestY = destY,
            DestZ = destZ,
            Running = running
        });
        foreach (int watcher in watcherObjectIds)
        {
            if (!_presence.TryGet(watcher, out _, out _))
            {
                ZoneLog.Info($"[move] MoveState watcher={watcher} mover={moverObjectId} NOT REGISTERED in presence hub");
                continue;
            }
            _presence.TrySend(watcher, NetOpcode.MoveState, payload);
        }
    }

    void IWorldEventPublisher.FanoutKnownlistDiff(int selfObjectId, IReadOnlyList<int> added, IReadOnlyList<int> removed)
    {
        if (!_world.TryGetEntityById(selfObjectId, out var selfEntity))
            return;
        var self = _ids.AllocateOrGet(selfObjectId);
        var ct = CancellationToken.None;
        byte[]? selfSpawn = null;
        foreach (int id in added)
        {
            if (!_world.TryGetEntityById(id, out var seen))
                continue;
            var otherId = _ids.AllocateOrGet(id);
            if (seen.Kind == ZoneEntityKind.GroundItem)
            {
                _presence.TrySend(selfObjectId, NetOpcode.DropItem, NetCodec.EncodeDropItem(new DropItemNetMsg
                {
                    DropperObjectId = 0,
                    EntityValue = otherId.Value,
                    EntityGeneration = otherId.Generation,
                    ItemId = seen.GroundItemId,
                    X = seen.X,
                    Y = seen.Y,
                    Z = seen.Z,
                    Stackable = seen.GroundStackable,
                    Count = seen.GroundCount
                }));
                continue;
            }

            _presence.TrySendCritical(selfObjectId, NetOpcode.EntitySpawn,
                NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildPresenceSpawn(seen, otherId, _playerSpawnPAtkSpd(seen))));
            if (seen.MoveActive)
                SendMoveState(selfObjectId, otherId, seen);
            if (seen.Kind == ZoneEntityKind.Player && seen.Session is { InWorld: true })
            {
                selfSpawn ??= NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(selfEntity, self, _playerSpawnPAtkSpd(selfEntity)));
                _presence.TrySendCritical(id, NetOpcode.EntitySpawn, selfSpawn);
                if (selfEntity.MoveActive)
                    SendMoveState(id, self, selfEntity);
            }
        }

        if (removed.Count == 0)
            return;
        var despawnSelf = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = self.Value,
            EntityGeneration = self.Generation
        });
        foreach (int id in removed)
        {
            var forgotten = _ids.AllocateOrGet(id);
            _presence.TrySendCritical(selfObjectId, NetOpcode.EntityDespawn, NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
            {
                EntityValue = forgotten.Value,
                EntityGeneration = forgotten.Generation
            }));
            if (_world.TryGetLivePlayerById(id, out _))
                _presence.TrySendCritical(id, NetOpcode.EntityDespawn, despawnSelf);
        }
    }

    void SendMoveState(int watcherObjectId, EntityId mover, ZoneEntity e)
    {
        _presence.TrySend(watcherObjectId, NetOpcode.MoveState, NetCodec.EncodeMoveState(new MoveStateMsg
        {
            EntityValue = mover.Value,
            EntityGeneration = mover.Generation,
            OriginX = e.X,
            OriginY = e.Y,
            OriginZ = e.Z,
            DestX = e.MoveDestX,
            DestY = e.MoveDestY,
            DestZ = e.MoveDestZ,
            Running = e.Running
        }));
    }

    void IWorldEventPublisher.FanoutChangeMoveType(IReadOnlyList<int> watcherObjectIds, int moverObjectId, bool running, float runSpeed, float walkSpeed, bool inWater)
    {
        var mover = _ids.AllocateOrGet(moverObjectId);
        var payload = NetCodec.EncodeChangeMoveType(new ChangeMoveTypeNetMsg
        {
            EntityValue = mover.Value,
            EntityGeneration = mover.Generation,
            Running = running,
            RunSpeed = runSpeed,
            WalkSpeed = walkSpeed,
            InWater = inWater
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ChangeMoveType, payload);
    }

    void IWorldEventPublisher.FanoutAttackStance(IReadOnlyList<int> watcherObjectIds, int objectId, bool inStance)
    {
        var id = _ids.AllocateOrGet(objectId);
        var payload = NetCodec.EncodeAttackStance(new AttackStanceNetMsg { EntityValue = id.Value, EntityGeneration = id.Generation });
        ushort opcode = inStance ? NetOpcode.AutoAttackStart : NetOpcode.AutoAttackStop;
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, opcode, payload);
    }

    void IWorldEventPublisher.FanoutAutoSoulShot(IReadOnlyList<int> watcherObjectIds, int itemId, bool enabled)
    {
        var payload = NetCodec.EncodeAutoSoulShot(new AutoSoulShotNetMsg { ItemId = itemId, Enabled = enabled });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AutoSoulShot, payload);
    }

    void IWorldEventPublisher.FanoutUserInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.UserInfoMsg info)
    {
        var payload = NetCodec.EncodeUserInfo(ToNetUserInfo(info));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.UserInfo, payload);
    }

    internal static UserInfoNetMsg ToNetUserInfo(Fineage.Protocol.UserInfoMsg info)
    {
        var src = info.Paperdoll ?? [];
        var slots = new PaperdollSlotNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            slots[i] = new PaperdollSlotNet
            {
                ObjectId = src[i].ObjectId,
                ItemId = src[i].ItemId,
                EnchantLevel = src[i].EnchantLevel
            };
        }

        return new UserInfoNetMsg
        {
            EntityValue = (ulong)(uint)info.ObjectId,
            EntityGeneration = 1,
            Name = info.Name ?? "",
            ClassId = info.ClassId,
            Race = info.Race,
            Sex = info.Sex,
            X = info.X,
            Y = info.Y,
            Z = info.Z,
            RunSpeed = info.RunSpeed,
            WalkSpeed = info.WalkSpeed,
            Running = info.Running,
            Level = info.Level,
            Exp = info.Exp,
            Sp = info.Sp,
            CurHp = info.CurHp,
            MaxHp = info.MaxHp,
            CurMp = info.CurMp,
            MaxMp = info.MaxMp,
            CurCp = info.CurCp,
            MaxCp = info.MaxCp,
            Str = info.Str,
            Dex = info.Dex,
            Con = info.Con,
            Int = info.Int,
            Wit = info.Wit,
            Men = info.Men,
            PAtk = info.PAtk,
            MAtk = info.MAtk,
            PDef = info.PDef,
            MDef = info.MDef,
            PAtkSpd = info.PAtkSpd,
            MAtkSpd = info.MAtkSpd,
            Accuracy = info.Accuracy,
            Evasion = info.Evasion,
            CritRate = info.CritRate,
            WeaponGradePenalty = info.WeaponGradePenalty,
            ArmorGradePenalty = info.ArmorGradePenalty,
            ClanId = info.ClanId,
            CurrentWeight = info.CurrentWeight,
            MaxWeight = info.MaxWeight,
            Paperdoll = slots
        };
    }

    void IWorldEventPublisher.FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        if (watcherObjectIds.Count == 0 || !_world.TryGetEntityById(objectId, out var entity) ||
            entity.Kind != ZoneEntityKind.Player)
            return;
        var payload = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, _ids.AllocateOrGet(objectId), _playerSpawnPAtkSpd(entity)));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.EntitySpawn, payload);
    }

    void IWorldEventPublisher.FanoutAttackState(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AttackMsg attack)
    {
        var payload = NetCodec.EncodeAttackState(new AttackStateMsg
        {
            AttackerValue = (ulong)(uint)attack.AttackerObjectId,
            AttackerGeneration = 1,
            TargetValue = (ulong)(uint)attack.TargetObjectId,
            TargetGeneration = 1,
            Damage = attack.Damage,
            Flags = attack.Flags,
            AttackerX = attack.AttackerX,
            AttackerY = attack.AttackerY,
            AttackerZ = attack.AttackerZ,
            TargetX = attack.TargetX,
            TargetY = attack.TargetY,
            TargetZ = attack.TargetZ
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AttackState, payload);
    }

    void IWorldEventPublisher.FanoutStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.StatusUpdateMsg status)
    {
        var payload = NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = (ulong)(uint)status.ObjectId,
            EntityGeneration = 1,
            AttributeIds = status.AttributeIds ?? [],
            Values = status.Values ?? []
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.StatusUpdate, payload);
    }

    void IWorldEventPublisher.FanoutDie(IReadOnlyList<int> watcherObjectIds, int objectId, int killerObjectId)
    {
        var payload = NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1,
            KillerObjectId = killerObjectId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.EntityDie, payload);
    }

    void IWorldEventPublisher.FanoutDropItem(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.DropItemMsg drop)
    {
        var payload = NetCodec.EncodeDropItem(new DropItemNetMsg
        {
            DropperObjectId = drop.CharObjectId,
            EntityValue = (ulong)(uint)drop.ObjectId,
            EntityGeneration = 1,
            ItemId = drop.ItemId,
            X = drop.X,
            Y = drop.Y,
            Z = drop.Z,
            Stackable = drop.Stackable,
            Count = drop.Count
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.DropItem, payload);
    }

    void IWorldEventPublisher.FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        var payload = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.EntityDespawn, payload);
    }

    void IWorldEventPublisher.FanoutInventoryUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.InventoryUpdateMsg update)
    {
        var src = update.Changes ?? [];
        var rows = new InventoryUpdateEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            var row = src[i];
            rows[i] = new InventoryUpdateEntryNet
            {
                Change = row.Change,
                ObjectId = row.ObjectId,
                ItemId = row.ItemId,
                Count = row.Count,
                EnchantLevel = row.EnchantLevel,
                EquippedSlot = row.EquippedSlot,
                BodySlot = row.BodySlot ?? "",
                Kind = row.Kind ?? "",
                Icon = row.Icon ?? "",
                WeaponType = row.WeaponType ?? "",
                ArmorType = row.ArmorType ?? ""
            };
        }
        var payload = NetCodec.EncodeInventoryUpdate(new InventoryUpdateNetMsg { Changes = rows });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySendCritical(watcher, NetOpcode.InventoryUpdate, payload);
    }

    void IWorldEventPublisher.FanoutEtcStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EtcStatusUpdateMsg status)
    {
        foreach (int watcher in watcherObjectIds)
        {
            var payload = NetCodec.EncodeEtcStatusUpdate(ToNetEtcStatus(status, watcher));
            _presence.TrySend(watcher, NetOpcode.EtcStatusUpdate, payload);
        }
    }

    internal static EtcStatusUpdateNetMsg ToNetEtcStatus(Fineage.Protocol.EtcStatusUpdateMsg status, int objectId) => new()
    {
        EntityValue = (ulong)(uint)objectId,
        EntityGeneration = 1,
        Charges = status.Charges,
        WeightPenalty = status.WeightPenalty,
        ChatBlocked = status.ChatBlocked,
        DangerArea = status.DangerArea,
        GradePenalty = status.GradePenalty,
        CharmOfCourage = status.CharmOfCourage,
        DeathPenalty = status.DeathPenalty
    };

    void IWorldEventPublisher.FanoutCreatureSay(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.CreatureSayMsg say)
    {
        var payload = NetCodec.EncodeCreatureSay(new CreatureSayNetMsg
        {
            ObjectId = say.ObjectId,
            Name = say.Name ?? "",
            Text = say.Text ?? "",
            ChatType = say.ChatType
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.CreatureSay, payload);
    }

    void IWorldEventPublisher.FanoutSystemMessage(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SystemMessageMsg message)
    {
        var payload = NetCodec.EncodeSystemMessage(new SystemMessageNetMsg
        {
            Id = message.Id,
            Params = message.Params ?? [],
            Text = message.Text ?? ""
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SystemMessage, payload);
    }

    void IWorldEventPublisher.FanoutMagicSkillUse(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.MagicSkillUseMsg cast)
    {
        var payload = NetCodec.EncodeMagicSkillUse(new MagicSkillUseNetMsg
        {
            CasterObjectId = cast.CasterObjectId,
            TargetObjectId = cast.TargetObjectId,
            SkillId = cast.SkillId,
            SkillLevel = cast.SkillLevel,
            HitTimeMs = cast.HitTimeMs,
            ReuseDelayMs = cast.ReuseDelayMs
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.MagicSkillUse, payload);
    }

    void IWorldEventPublisher.FanoutRevive(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        var payload = NetCodec.EncodeRevive(new ReviveNetMsg { ObjectId = objectId });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.Revive, payload);
    }

    void IWorldEventPublisher.FanoutTeleportToLocation(IReadOnlyList<int> watcherObjectIds, int objectId, int x, int y, int z, int heading)
    {
        var payload = NetCodec.EncodeTeleportToLocation(new TeleportToLocationNetMsg
        {
            ObjectId = objectId,
            X = x,
            Y = y,
            Z = z,
            Heading = heading
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TeleportToLocation, payload);
    }

    void IWorldEventPublisher.FanoutPlayerStats(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.PlayerStatsMsg stats)
    {
        var payload = NetCodec.EncodePlayerStats(new Fineage.Net.Contract.PlayerStatsMsg
        {
            EntityValue = (ulong)(uint)stats.ObjectId,
            EntityGeneration = 1,
            Level = stats.Level,
            Exp = stats.Exp,
            Sp = stats.Sp,
            Hp = stats.Hp,
            MaxHp = stats.MaxHp,
            Mp = stats.Mp,
            MaxMp = stats.MaxMp,
            Cp = stats.Cp,
            MaxCp = stats.MaxCp
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PlayerStats, payload);
    }

    void IWorldEventPublisher.FanoutAbnormalStatusUpdate(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AbnormalStatusUpdateMsg update)
    {
        var effects = update.Effects ?? [];
        var netEffects = new AbnormalEffectNet[effects.Length];
        for (int i = 0; i < effects.Length; i++)
        {
            var src = effects[i];
            netEffects[i] = new AbnormalEffectNet
            {
                Id = src.Id,
                Value = src.Value,
                DurationSeconds = src.DurationSeconds
            };
        }
        var payload = NetCodec.EncodeAbnormalStatusUpdate(new AbnormalStatusUpdateNetMsg
        {
            ObjectId = update.ObjectId,
            Effects = netEffects
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AbnormalStatusUpdate, payload);
    }

    void IWorldEventPublisher.FanoutNpcHtml(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.NpcHtmlMsg html)
    {
        var payload = NetCodec.EncodeNpcHtml(new NpcHtmlNetMsg
        {
            NpcObjectId = html.NpcObjectId,
            NpcId = html.NpcId,
            Html = html.Html ?? "",
            ItemId = html.ItemId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.NpcHtml, payload);
    }

}
