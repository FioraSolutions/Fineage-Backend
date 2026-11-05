
namespace Fineage.Zone;

public sealed class ZoneSession
{
    public ZoneSession()
    {
    }

    public int ObjectId { get; private set; }
    public string Account { get; private set; } = "";
    public string Name { get; private set; } = "";
    public int ClassId { get; private set; }
    /// <summary>From handoff claims; used when store lag would otherwise default race to Human.</summary>
    public int Race { get; private set; }
    public int Sex { get; private set; }
    public int WireSessionId { get; set; }
    public ushort LastPacketId { get; set; }
    internal bool FailureRecorded { get; set; }
    public bool InWorld { get; internal set; }
    public int X { get; internal set; }
    public int Y { get; internal set; }
    public int Z { get; internal set; }
    public int SelectedTargetId { get; set; }

    /// <summary>
    /// The trainer NPC whose window this player last opened. It lives on the session, not the world:
    /// a single world-wide value let one player opening a trainer decide which NPC answered
    /// <b>everyone's</b> learn-skill request until someone else opened a window.
    /// </summary>
    public int TrainerNpcId { get; set; }

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
    }

    /// <summary>Mobius AI_INTENTION_PICK_UP after moveToPawn: ground item to pick up once the run arrives.</summary>
    public int PendingPickupId { get; set; }
    public long PendingPickupAtUnixMs { get; set; }

    /// <summary>Mobius AI_INTENTION_CAST after moveToPawn: skill to fire once the server-driven approach arrives.</summary>
    public int PendingCastSkillId { get; set; }
    public int PendingCastTargetId { get; set; }
    public long PendingCastAtUnixMs { get; set; }

    /// <summary>Mobius saveNextIntention(MOVE_TO): move order received mid-cast, run when the cast ends.</summary>
    public Fineage.Protocol.MoveToMsg? QueuedMove { get; set; }

    /// <summary>Mobius RequestActionUse sit while moving: NextAction(EVT_ARRIVED) → sit on arrival.</summary>
    public bool SitOnArrive { get; set; }

    /// <summary>Unix milliseconds of the last accepted say. 0 means this session has not spoken.</summary>
    public long LastChatUnixMs { get; set; }

    public bool Bound => ObjectId != 0;

    public void Bind(int objectId, string account, string name, int classId, int race = 0, int sex = 0)
    {
        ObjectId = objectId;
        Account = account;
        Name = name;
        ClassId = classId;
        Race = race;
        Sex = sex;
        InWorld = false;
    }

    /// <summary>Mobius <c>setClassId</c> after a village-master transfer.</summary>
    internal void ChangeClass(int classId) => ClassId = classId;

    internal void NoteForLog(string account)
    {
        if (!string.IsNullOrEmpty(account))
            Account = account;
    }

    public void Clear()
    {
        ObjectId = 0;
        Account = "";
        Name = "";
        ClassId = 0;
        Race = 0;
        Sex = 0;
        InWorld = false;
        SelectedTargetId = 0;
        TrainerNpcId = 0;
        AutoAttackTargetId = 0;
        NextAutoAttackUnixMs = 0;
        PendingCastSkillId = 0;
        PendingCastTargetId = 0;
        PendingCastAtUnixMs = 0;
        QueuedMove = null;
        SitOnArrive = false;
        ClearApproach();
        LastChatUnixMs = 0;
    }
}
