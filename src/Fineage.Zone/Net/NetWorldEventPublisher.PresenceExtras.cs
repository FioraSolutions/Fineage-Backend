using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
    void IWorldEventPublisher.FanoutSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.SkillListMsg list)
    {
        var payload = NetCodec.EncodeSkillList(NetEnterWorldProjector.ToNetSkillList(list));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.SkillList, payload);
    }

    void IWorldEventPublisher.FanoutShortcutInit(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.ShortCutInitMsg init)
    {
        var payload = NetCodec.EncodeShortCutInit(SessionLifecyclePacketHandler.ToNetShortcutInit(init));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.ShortCutInit, payload);
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
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.ChangeWaitType, payload);
    }

    void IWorldEventPublisher.FanoutSocialAction(IReadOnlyList<int> watcherObjectIds, int objectId, int actionId)
    {
        var payload = NetCodec.EncodeSocialAction(new SocialActionNetMsg
        {
            EntityValue = (ulong)(uint)objectId,
            EntityGeneration = 1,
            ActionId = actionId
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.SocialAction, payload);
    }

    void IWorldEventPublisher.FanoutTargetSelected(IReadOnlyList<int> watcherObjectIds, int targetObjectId, int colorHint)
    {
        var payload = NetCodec.EncodeTargetSelected(new TargetSelectedNetMsg
        {
            TargetObjectId = targetObjectId,
            ColorHint = colorHint
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.TargetSelected, payload);
    }

    void IWorldEventPublisher.FanoutTargetUnselected(IReadOnlyList<int> watcherObjectIds, int targetObjectId)
    {
        var payload = NetCodec.EncodeTargetUnselected(new TargetUnselectedNetMsg
        {
            TargetObjectId = targetObjectId
        });
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.TargetUnselected, payload);
    }

    void IWorldEventPublisher.FanoutAcquireSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillListMsg list)
    {
        var payload = NetCodec.EncodeAcquireSkillList(ToNetAcquireSkillList(list));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.AcquireSkillList, payload);
    }

    void IWorldEventPublisher.FanoutAcquireSkillInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.AcquireSkillInfoMsg info)
    {
        var payload = NetCodec.EncodeAcquireSkillInfo(ToNetAcquireSkillInfo(info));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.AcquireSkillInfo, payload);
    }

    void IWorldEventPublisher.FanoutEnchantSkillList(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantSkillListMsg list)
    {
        var payload = NetCodec.EncodeEnchantSkillList(ToNetEnchantSkillList(list));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.EnchantSkillList, payload);
    }

    void IWorldEventPublisher.FanoutEnchantSkillInfo(IReadOnlyList<int> watcherObjectIds, Fineage.Protocol.EnchantSkillInfoMsg info)
    {
        var payload = NetCodec.EncodeEnchantSkillInfo(ToNetEnchantSkillInfo(info));
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.EnchantSkillInfo, payload);
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

    static EnchantSkillListNetMsg ToNetEnchantSkillList(Fineage.Protocol.EnchantSkillListMsg list)
    {
        var src = list.Skills ?? [];
        var rows = new EnchantSkillListEntryNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            rows[i] = new EnchantSkillListEntryNet
            {
                SkillId = src[i].SkillId,
                NextLevel = src[i].NextLevel,
                Sp = src[i].Sp,
                Exp = src[i].Exp
            };
        }

        return new EnchantSkillListNetMsg { Skills = rows };
    }

    static EnchantSkillInfoNetMsg ToNetEnchantSkillInfo(Fineage.Protocol.EnchantSkillInfoMsg info)
    {
        var src = info.Requirements ?? [];
        var reqs = new EnchantSkillRequirementNet[src.Length];
        for (int i = 0; i < src.Length; i++)
        {
            reqs[i] = new EnchantSkillRequirementNet
            {
                Type = src[i].Type,
                ItemId = src[i].ItemId,
                Count = src[i].Count,
                Unk = src[i].Unk
            };
        }

        return new EnchantSkillInfoNetMsg
        {
            SkillId = info.SkillId,
            Level = info.Level,
            SpCost = info.SpCost,
            ExpCost = info.ExpCost,
            Rate = info.Rate,
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
        _presence.TryBroadcast(watcherObjectIds, NetOpcode.EnchantResult, payload);
    }
}
