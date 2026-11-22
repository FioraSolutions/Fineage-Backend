using Fineage.Net.Contract;
using Fineage.Protocol;

namespace Fineage.Zone.Net;

static class EnterWire
{
    public static byte[] EncodePresence(PresenceWireSlot slot) =>
        slot.Payload ?? slot.Opcode switch
        {
            NetOpcode.MoveState => NetCodec.EncodeMoveState((MoveStateMsg)slot.Message!),
            NetOpcode.ChangeWaitType => NetCodec.EncodeChangeWaitType((ChangeWaitTypeNetMsg)slot.Message!),
            NetOpcode.AbnormalStatusUpdate => NetCodec.EncodeAbnormalStatusUpdate(
                SessionLifecyclePacketHandler.ToNetAbnormal((AbnormalStatusUpdateMsg)slot.Message!)),
            NetOpcode.DoorStatus => throw new InvalidOperationException("door follow-up requires pre-encoded payload"),
            _ => throw new InvalidOperationException($"enter presence encode unsupported opcode {slot.Opcode}")
        };
}