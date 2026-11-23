using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

/// <summary>
/// World → Net presence fanout: Protocol/primitives in, wire encode out (#353 / B2b).
/// Extracted from <see cref="NetZoneServer"/> so the listener stays dispatch + lifecycle.
/// </summary>
sealed partial class NetWorldEventPublisher
{
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
            Paperdoll = slots,
            Karma = info.Karma,
            PvpFlag = info.PvpFlag,
            PvpKills = info.PvpKills,
            PkKills = info.PkKills,
            Title = info.Title ?? "",
            ClanCrestId = info.ClanCrestId,
            LargeCrestId = info.LargeCrestId,
            AllyId = info.AllyId,
            AllyCrestId = info.AllyCrestId,
            ClanPrivileges = info.ClanPrivileges,
            PledgeClass = info.PledgeClass,
            PledgeType = info.PledgeType,
            ClanLeader = info.ClanLeader,
            Noble = info.Noble,
            Hero = info.Hero,
            BaseClassId = info.BaseClassId,
            ClassIndex = info.ClassIndex,
            Cubics = info.Cubics ?? [],
            Team = info.Team
        };
    }

    void IWorldEventPublisher.FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId) =>
        ((IWorldEventPublisher)this).PrepareCharInfo(watcherObjectIds, objectId)();

    Action IWorldEventPublisher.PrepareCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        var frames = new PreparedFrames(_presence);
        if (watcherObjectIds.Count == 0 || !_world.TryGetEntityById(objectId, out var entity) ||
            entity.Kind != ZoneEntityKind.Player)
            return frames.Send;
        var id = _ids.AllocateOrGet(objectId);
        var payload = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, id, _playerSpawnPAtkSpd(entity)));
        // A re-sent CharInfo rebuilds the remote pawn; keep it sitting / running instead of snapping to idle.
        var followUps = NetEnterWorldProjector.BuildPresenceFollowUps(entity, id, _world);
        foreach (int watcher in watcherObjectIds)
        {
            frames.Critical(watcher, NetOpcode.EntitySpawn, payload);
            if (watcher == objectId)
                continue;
            foreach (var (opcode, frame) in followUps)
                frames.Critical(watcher, opcode, frame);
            if (_world.TryGetEntityById(watcher, out var viewer) && _world.SpawnRelationFrame(entity, viewer) is { } relation)
                frames.Critical(watcher, NetOpcode.RelationChanged, relation);
        }

        return frames.Send;
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
            TargetZ = attack.TargetZ,
            ExtraHits = attack.SecondHit is { } second
                ? [new AttackHitNet { Damage = second.Damage, Flags = second.Flags }]
                : []
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.AttackState, payload);
    }

    void IWorldEventPublisher.FanoutMagicSkillLaunched(IReadOnlyList<int> watcherObjectIds, int casterObjectId, int skillId, int skillLevel, int[] targetObjectIds)
    {
        var payload = NetCodec.EncodeMagicSkillLaunched(new MagicSkillLaunchedNetMsg
        {
            CasterObjectId = casterObjectId,
            SkillId = skillId,
            SkillLevel = skillLevel,
            TargetObjectIds = targetObjectIds
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.MagicSkillLaunched, payload);
    }

    void IWorldEventPublisher.FanoutMagicSkillCanceled(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        var payload = NetCodec.EncodeMagicSkillCanceled(new MagicSkillCanceledNetMsg { ObjectId = objectId });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.MagicSkillCanceled, payload);
    }

    void IWorldEventPublisher.FanoutPlaySound(IReadOnlyList<int> watcherObjectIds, int type, string file, int objectId, int x, int y, int z, int delayMs)
    {
        var payload = NetCodec.EncodePlaySound(new PlaySoundNetMsg
        {
            Type = type,
            File = file,
            ObjectId = objectId,
            X = x,
            Y = y,
            Z = z,
            DelayMs = delayMs
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.PlaySound, payload);
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
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.StatusUpdate, payload);
    }

    void IWorldEventPublisher.FanoutDie(IReadOnlyList<int> watcherObjectIds, int objectId, int killerObjectId, int restartFlags)
    {
        var payload = NetCodec.EncodeEntityDie(new EntityDieMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1,
            KillerObjectId = killerObjectId,
            RestartFlags = restartFlags
        });
        _presence.TryBroadcastCritical(watcherObjectIds, NetOpcode.EntityDie, payload);
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
        _presence.TryBroadcastCritical(watcherObjectIds, NetOpcode.DropItem, payload);
    }

    void IWorldEventPublisher.FanoutEntityDespawn(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        var payload = NetCodec.EncodeEntityDespawn(new EntityDespawnMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1
        });
        _presence.TryBroadcastCritical(watcherObjectIds, NetOpcode.EntityDespawn, payload);
    }

    void IWorldEventPublisher.FanoutMonRaceInfo(IReadOnlyList<int> watcherObjectIds, MonRaceInfoNetMsg packet)
    {
        var payload = NetCodec.EncodeMonRaceInfo(packet);
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.MonRaceInfo, payload);
    }

    void IWorldEventPublisher.FanoutVehicleInfo(IReadOnlyList<int> watcherObjectIds, VehicleInfoNetMsg info) =>
        SendAll(watcherObjectIds, NetOpcode.VehicleInfo, NetCodec.EncodeVehicleInfo(info));

    void IWorldEventPublisher.FanoutVehicleDeparture(IReadOnlyList<int> watcherObjectIds, VehicleDepartureNetMsg departure) =>
        SendAll(watcherObjectIds, NetOpcode.VehicleDeparture, NetCodec.EncodeVehicleDeparture(departure));
}
