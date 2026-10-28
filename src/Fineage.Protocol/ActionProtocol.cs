namespace Fineage.Protocol;

/// <summary>
/// Zone character actions (Mobius <c>RequestActionUse</c> slice). Request <b>51</b> after skills (<see cref="SkillProtocol.UseRequest"/>).
/// Replies <b>1067–1069</b> after skills (<see cref="SkillProtocol.MagicSkillUse"/>).
/// </summary>
/// <remarks>
/// Mobius Interlude: sit/stand and walk/run via action ids 0–1; social gestures 12–14;
/// fan-out <c>ChangeWaitType</c>, <c>SocialAction</c>, movement mode on the actor.
/// Layout: <c>docs/action-v2-protocol.md</c>.
/// </remarks>
public static class ActionProtocol
{
    public const ushort Request = 51;

    public const ushort ChangeWaitType = 1067;
    public const ushort SocialAction = 1068;
    public const ushort ChangeMoveType = 1069;

    /// <summary>Mobius action table: Sit / Stand toggle.</summary>
    public const int SitStand = 0;

    /// <summary>Mobius action table: Walk / Run toggle.</summary>
    public const int WalkRun = 1;

    /// <summary>Panel Attack — same as <see cref="MsgType.AttackRequest"/>.</summary>
    public const int Attack = 2;

    /// <summary>Next hostile target in knownlist (Interlude short-range next target).</summary>
    public const int NextTarget = 4;

    /// <summary>Social: Greeting.</summary>
    public const int SocialGreeting = 12;

    /// <summary>Social: Victory.</summary>
    public const int SocialVictory = 13;

    /// <summary>Social: Advance.</summary>
    public const int SocialAdvance = 14;

    /// <summary>Interlude <c>ChangeWaitType</c> wait type: sitting.</summary>
    public const int WaitSitting = 0;

    /// <summary>Interlude <c>ChangeWaitType</c> wait type: standing.</summary>
    public const int WaitStanding = 1;

    public const string Dead = "action_dead";
    public const string Blocked = "action_blocked";
    public const string Sitting = "action_sitting";
    public const string Unknown = "action_unknown";
    public const string Unsupported = "action_unsupported";
}
