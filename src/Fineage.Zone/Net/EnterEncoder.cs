using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.Net;

static class EnterEncoder
{
    public static Task<List<NetFrame>> EncodeAndDispatchAsync(EnterSnapshot snapshot, CancellationToken ct) =>
        Task.Run(() => EncodeAndDispatch(snapshot), ct);

    public static List<NetFrame> EncodeAndDispatch(EnterSnapshot snapshot)
    {
        var frames = new List<NetFrame>(snapshot.Self.Count + snapshot.Known.Count * 2 + snapshot.PeerFanout.Count * 2);
        int bytes = 0;

        foreach (var slot in snapshot.Self)
        {
            byte[] payload = slot.Payload ?? EncodeSelf(slot, snapshot.SessionObjectId);
            frames.Add(NetFrame.ToSelf(slot.Opcode, payload));
            bytes += payload.Length;
        }

        using (EnterMetrics.MeasureKnownEncode())
        {
            foreach (var row in snapshot.Known)
            {
                if (row.GroundItem)
                {
                    byte[] drop = NetCodec.EncodeDropItem(row.Drop!);
                    frames.Add(NetFrame.ToSelf(NetOpcode.DropItem, drop));
                    bytes += drop.Length;
                    continue;
                }

                EntitySpawnMsg spawn = row.Npc is { } npc
                    ? NetEnterWorldProjector.BuildNpcSpawn(npc)
                    : row.Spawn!;
                byte[] encoded = row.Kind == EntityKindNet.Npc
                    ? NpcEnterSpawnCache.EncodeNpcSpawn(spawn)
                    : NetCodec.EncodeEntitySpawn(spawn);
                frames.Add(NetFrame.ToSelf(NetOpcode.EntitySpawn, encoded));
                bytes += encoded.Length;
                foreach (var follow in row.FollowUps)
                {
                    byte[] payload = EnterWire.EncodePresence(follow);
                    frames.Add(NetFrame.ToSelf(follow.Opcode, payload));
                    bytes += payload.Length;
                }

                if (row.RelationToEntrant is { } relation)
                {
                    frames.Add(NetFrame.ToSelf(NetOpcode.RelationChanged, relation));
                    bytes += relation.Length;
                }
            }
        }

        using (EnterMetrics.MeasurePeerFanout())
        {
            byte[] selfRemote = NetCodec.EncodeEntitySpawn(snapshot.SelfRemoteSpawn);
            bytes += selfRemote.Length;
            foreach (var peer in snapshot.PeerFanout)
            {
                frames.Add(new NetFrame(peer.PeerObjectId, NetOpcode.EntitySpawn, selfRemote));
                if (peer.RelationPayload is { } relation)
                {
                    frames.Add(new NetFrame(peer.PeerObjectId, NetOpcode.RelationChanged, relation));
                    bytes += relation.Length;
                }
            }
        }

        EnterMetrics.RecordCounts(snapshot.KnownEntityCount, snapshot.KnownPlayerCount, frames.Count, bytes);
        return frames;
    }

    static byte[] EncodeItemList(EnterSelfSlot slot)
    {
        var message = slot.Message switch
        {
            ItemListNetMsg ready => ready,
            PlayerInventory inventory => NetEnterWorldProjector.BuildItems(inventory),
            _ => throw new InvalidOperationException("enter ItemList slot requires ItemListNetMsg or PlayerInventory")
        };
        return InventoryVersionTail.Append(NetCodec.EncodeItemList(message), message.Version);
    }

    static byte[] EncodeSelf(EnterSelfSlot slot, int objectId) =>
        slot.Opcode switch
        {
            NetOpcode.EntitySpawn => NetCodec.EncodeEntitySpawn((EntitySpawnMsg)slot.Message!),
            NetOpcode.PlayerStats => NetCodec.EncodePlayerStats((Fineage.Net.Contract.PlayerStatsMsg)slot.Message!),
            NetOpcode.UserInfo => NetCodec.EncodeUserInfo(NetZoneServer.ToNetUserInfo((UserInfoMsg)slot.Message!)),
            NetOpcode.ItemList => EncodeItemList(slot),
            NetOpcode.SkillList => NetCodec.EncodeSkillList(NetEnterWorldProjector.ToNetSkillList(slot.Message switch
            {
                SkillListMsg list => list,
                EnterSkillCapture capture => capture.ToSkillListMsg(),
                _ => throw new InvalidOperationException("enter SkillList slot requires SkillListMsg or EnterSkillCapture")
            })),
            NetOpcode.ShortCutInit => NetCodec.EncodeShortCutInit(SessionLifecyclePacketHandler.ToNetShortcutInit((ShortCutInitMsg)slot.Message!)),
            NetOpcode.PlayerActionState => NetCodec.EncodePlayerActionState((PlayerActionStateNetMsg)slot.Message!),
            NetOpcode.AbnormalStatusUpdate => NetCodec.EncodeAbnormalStatusUpdate(
                SessionLifecyclePacketHandler.ToNetAbnormal((AbnormalStatusUpdateMsg)slot.Message!)),
            NetOpcode.EtcStatusUpdate => NetCodec.EncodeEtcStatusUpdate(
                NetZoneServer.ToNetEtcStatus((EtcStatusUpdateMsg)slot.Message!, objectId)),
            NetOpcode.QuestList => NetCodec.EncodeQuestList(NetZoneServer.ToNetQuestList((QuestListMsg)slot.Message!)),
            NetOpcode.PledgeStatus => NetCodec.EncodePledgeStatus(NetZoneServer.ToNetPledgeStatus((PledgeStatusChangedMsg)slot.Message!)),
            NetOpcode.PledgeMemberList => NetCodec.EncodePledgeMemberList(NetZoneServer.ToNetPledgeMemberList((PledgeMemberListMsg)slot.Message!)),
            NetOpcode.ClientSetTime => NetCodec.EncodeClientSetTime((ClientSetTimeNetMsg)slot.Message!),
            NetOpcode.SystemMessage => NetCodec.EncodeSystemMessage((SystemMessageNetMsg)slot.Message!),
            _ => throw new InvalidOperationException($"enter encode unsupported opcode {slot.Opcode}")
        };
}