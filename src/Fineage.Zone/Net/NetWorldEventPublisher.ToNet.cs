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
            PkKills = info.PkKills
        };
    }

    void IWorldEventPublisher.FanoutCharInfo(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        if (watcherObjectIds.Count == 0 || !_world.TryGetEntityById(objectId, out var entity) ||
            entity.Kind != ZoneEntityKind.Player)
            return;
        var id = _ids.AllocateOrGet(objectId);
        var payload = NetCodec.EncodeEntitySpawn(NetEnterWorldProjector.BuildRemoteSpawn(entity, id, _playerSpawnPAtkSpd(entity)));
        // A re-sent CharInfo rebuilds the remote pawn; keep it sitting / running instead of snapping to idle.
        var followUps = NetEnterWorldProjector.BuildPresenceFollowUps(entity, id, _world);
        foreach (int watcher in watcherObjectIds)
        {
            _presence.TrySendCritical(watcher, NetOpcode.EntitySpawn, payload);
            if (watcher == objectId)
                continue;
            foreach (var (opcode, frame) in followUps)
                _presence.TrySendCritical(watcher, opcode, frame);
        }
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
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AttackState, payload);
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
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.MagicSkillLaunched, payload);
    }

    void IWorldEventPublisher.FanoutMagicSkillCanceled(IReadOnlyList<int> watcherObjectIds, int objectId)
    {
        var payload = NetCodec.EncodeMagicSkillCanceled(new MagicSkillCanceledNetMsg { ObjectId = objectId });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.MagicSkillCanceled, payload);
    }

    void IWorldEventPublisher.FanoutPlaySound(IReadOnlyList<int> watcherObjectIds, int type, string file, int objectId, int x, int y, int z, int delayMs)
    {
        var payload = NetCodec.EncodePlaySound(new PlaySoundNetMsg
        {
            Type = type, File = file, ObjectId = objectId, X = x, Y = y, Z = z, DelayMs = delayMs
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.PlaySound, payload);
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
}
