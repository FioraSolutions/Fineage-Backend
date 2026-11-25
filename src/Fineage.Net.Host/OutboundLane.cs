using Fineage.Net.Contract;

namespace Fineage.Net.Host;

/// <summary>
/// Admission class for one connection's outbound mailbox. The lane decides what a full mailbox does.
/// None of them block the caller: a slow client loses updates or is disconnected, and the world keeps ticking.
/// </summary>
enum OutboundLane
{
    /// <summary>Newest state per (opcode, entity): pose, move type, position correction, status (attributes merged), abnormals. Never dropped.</summary>
    Movement,

    /// <summary>Knownlist / appearance (spawn, despawn, relation, wait type). Reliable and an ordering barrier: never replaced or dropped.</summary>
    Snapshot,

    /// <summary>Animations and markers (attack / cast visuals, social actions, target select). Above the soft limit the oldest is shed.</summary>
    Visual,

    /// <summary>Order-preserving gameplay (inventory, drops, death, chat, system messages). Above the hard limit the connection closes so the client resyncs.</summary>
    Reliable
}

static class OutboundLanePolicy
{
    public static OutboundLane Classify(ushort opcode) => opcode switch
    {
        NetOpcode.MoveState or NetOpcode.ChangeMoveType or NetOpcode.PositionCorrection
            or NetOpcode.StatusUpdate or NetOpcode.AbnormalStatusUpdate => OutboundLane.Movement,
        NetOpcode.EntitySpawn or NetOpcode.EntityDespawn or NetOpcode.RelationChanged
            or NetOpcode.ChangeWaitType => OutboundLane.Snapshot,
        NetOpcode.AttackState or NetOpcode.MagicSkillUse or NetOpcode.MagicSkillLaunched
            or NetOpcode.MagicSkillCanceled or NetOpcode.AutoAttackStart or NetOpcode.AutoAttackStop
            or NetOpcode.SocialAction or NetOpcode.TargetSelected or NetOpcode.TargetUnselected => OutboundLane.Visual,
        _ => OutboundLane.Reliable
    };

    public static string Name(OutboundLane lane) => lane switch
    {
        OutboundLane.Movement => "movement",
        OutboundLane.Snapshot => "snapshot",
        OutboundLane.Visual => "visual",
        _ => "reliable"
    };

    public static bool IsReliable(ushort opcode) => Classify(opcode) == OutboundLane.Reliable;

    /// <summary>True when losing this frame would desync the client: everything except visuals.</summary>
    public static bool MustDeliver(ushort opcode) => Classify(opcode) != OutboundLane.Visual;
}
