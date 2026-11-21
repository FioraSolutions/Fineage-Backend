namespace Fineage.Zone.World;

/// <summary>
/// What a player is in the middle of doing (B11 in <c>docs/architecture/zone-simulation.md</c>): auto attack, approach,
/// pending pickup / talk / cast, queued move. It lives on the player entity, so the region that owns the player is
/// the only writer, from commands and its own stages.
/// </summary>
public sealed class PlayerIntent
{
    /// <summary>Interlude-style auto attack: target kept swinging at until it dies or the player stops.</summary>
    public int AutoAttackTargetId { get; set; }
    public long NextAutoAttackUnixMs { get; set; }

    /// <summary>
    /// Mobius AI_INTENTION_ATTACK + moveToPawn in flight: sticky approach. While now &lt; ApproachArriveUnixMs the
    /// pawn is running from (ApproachOriginX/Y) to (ApproachDestX/Y) toward target last seen at ApproachTargetX/Y.
    /// </summary>
    public int ApproachTargetId { get; set; }
    public long ApproachArriveUnixMs { get; set; }
    public int ApproachOriginX { get; set; }
    public int ApproachOriginY { get; set; }
    public int ApproachDestX { get; set; }
    public int ApproachDestY { get; set; }
    public int ApproachTargetX { get; set; }
    public int ApproachTargetY { get; set; }

    public void ClearApproach()
    {
        ApproachTargetId = 0;
        ApproachArriveUnixMs = 0;
        PendingTalkNpcId = 0;
        PendingTalkAtUnixMs = 0;
    }

    /// <summary>Mobius AI_INTENTION_PICK_UP after moveToPawn: ground item to pick up once the run arrives.</summary>
    public int PendingPickupId { get; set; }
    public long PendingPickupAtUnixMs { get; set; }

    /// <summary>Mobius AI_INTENTION_INTERACT after moveToPawn: NPC to talk to once the run arrives.</summary>
    public int PendingTalkNpcId { get; set; }
    public long PendingTalkAtUnixMs { get; set; }

    /// <summary>Mobius AI_INTENTION_CAST after moveToPawn: skill to fire once the server-driven approach arrives.</summary>
    public int PendingCastSkillId { get; set; }
    public int PendingCastTargetId { get; set; }
    public bool PendingCastCtrlPressed { get; set; }
    public long PendingCastAtUnixMs { get; set; }

    /// <summary>Mobius saveNextIntention(MOVE_TO): move order received mid-cast, run when the cast ends.</summary>
    public Fineage.Protocol.MoveToMsg? QueuedMove { get; set; }

    /// <summary>Mobius RequestActionUse sit while moving: NextAction(EVT_ARRIVED) → sit on arrival.</summary>
    public bool SitOnArrive { get; set; }

    /// <summary>Mobius Wedding <c>goToLove</c> SoE cast before teleport.</summary>
    public int WeddingTeleportPartnerId { get; set; }
    public long WeddingTeleportAtUnixMs { get; set; }
}
