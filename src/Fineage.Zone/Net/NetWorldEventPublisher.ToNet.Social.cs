using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;

namespace Fineage.Zone.Net;

sealed partial class NetWorldEventPublisher
{
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
                DurationSeconds = src.DurationSeconds,
                Slot = src.Slot ?? ""
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
