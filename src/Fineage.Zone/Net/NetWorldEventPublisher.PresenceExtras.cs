using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SkillListMsg list)
    {
        var payload = NetCodec.EncodeSkillList(NetEnterWorldProjector.ToNetSkillList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SkillList, payload);
    }

    void IWorldEventPublisher.FanoutChangeWaitType(IReadOnlyList<int> watcherObjectIds, int objectId, int waitType, int x, int y, int z)
    {
        var payload = NetCodec.EncodeChangeWaitType(new ChangeWaitTypeNetMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1,
            WaitType = waitType,
            X = x,
            Y = y,
            Z = z
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.ChangeWaitType, payload);
    }

    void IWorldEventPublisher.FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, int objectId, int actionId)
    {
        var payload = NetCodec.EncodeSocialAction(new SocialActionNetMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1,
            ActionId = actionId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.SocialAction, payload);
    }

    void IWorldEventPublisher.FanoutTargetSelected(IReadOnlyList<int> watcherObjectIds, int targetObjectId, int colorHint)
    {
        var payload = NetCodec.EncodeTargetSelected(new TargetSelectedNetMsg
        {
            TargetObjectId = targetObjectId,
            ColorHint = colorHint
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TargetSelected, payload);
    }

    void IWorldEventPublisher.FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, int targetObjectId)
    {
        var payload = NetCodec.EncodeTargetUnselected(new TargetUnselectedNetMsg
        {
            TargetObjectId = targetObjectId
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.TargetUnselected, payload);
    }

    void IWorldEventPublisher.FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillListMsg list)
    {
        var payload = NetCodec.EncodeAcquireSkillList(ToNetAcquireSkillList(list));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AcquireSkillList, payload);
    }

    void IWorldEventPublisher.FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillInfoMsg info)
    {
        var payload = NetCodec.EncodeAcquireSkillInfo(ToNetAcquireSkillInfo(info));
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.AcquireSkillInfo, payload);
    }

    static AcquireSkillListNetMsg ToNetAcquireSkillList(Fineage.Protocol.AcquireSkillListMsg list)
    {
        var src = list.Skills ?? [];
        var rows = new AcquireSkillEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new AcquireSkillEntryNet
            {
                SkillId = src[i].SkillId,
                NextLevel = src[i].NextLevel,
                MaxLevel = src[i].MaxLevel,
                SpCost = src[i].SpCost,
                Requirements = src[i].Requirements
            };
        }

        return new AcquireSkillListNetMsg { SkillType = list.SkillType, Skills = rows };
    }

    static AcquireSkillInfoNetMsg ToNetAcquireSkillInfo(Fineage.Protocol.AcquireSkillInfoMsg info)
    {
        var src = info.Requirements ?? [];
        var reqs = new AcquireSkillRequirementNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            reqs[i] = new AcquireSkillRequirementNet
            {
                Type = src[i].Type,
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Unk = src[i].Unk
            };
        }

        return new AcquireSkillInfoNetMsg
        {
            SkillId = info.SkillId,
            Level = info.Level,
            SpCost = info.SpCost,
            SkillType = info.SkillType,
            Requirements = reqs
        };
    }

    void IWorldEventPublisher.FanoutEnchantResult(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantResultMsg result)
    {
        var payload = NetCodec.EncodeEnchantResult(new EnchantResultNetMsg
        {
            Result = result.Result,
            EnchantLevel = result.EnchantLevel,
            ObjectId = result.ObjectId,
            CrystalItemId = result.CrystalItemId,
            CrystalCount = result.CrystalCount
        });
        foreach (int watcher in watcherObjectIds)
            _presence.TrySend(watcher, NetOpcode.EnchantResult, payload);
    }
}
