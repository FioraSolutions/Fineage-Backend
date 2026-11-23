using Fineage.Net.Contract;

namespace Fineage.Net.Host;

/// <summary>
/// Admission class for one connection's outbound mailbox. The lane decides what a full mailbox does.
/// None of them block the caller: a slow client loses updates or is disconnected, and the world keeps ticking.
/// </summary>
enum OutboundLane
{
    /// <summary>Latest pose per entity. A newer frame replaces the pending one.</summary>
    Movement,

    /// <summary>Knownlist / appearance. One pending snapshot per connection; a newer burst replaces it when the budget cannot hold both.</summary>
    Snapshot,

    /// <summary>Combat and other visuals. A full lane drops the stale frame.</summary>
    Visual,

    /// <summary>Order-preserving gameplay. A full mailbox closes the connection so the client resyncs.</summary>
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
            or NetOpcode.SocialAction or NetOpcode.TargetSelected or NetOpcode.TargetUnselected
            or NetOpcode.CreatureSay => OutboundLane.Visual,
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
}
