namespace Fineage.Net.Contract
{
    public sealed class MoveInputMsg
    {
        public int DestX { get; set; }
        public int DestY { get; set; }
        public int DestZ { get; set; }
    }

    public sealed class ValidatePositionMsg
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
    }

    public sealed class StopMoveMsg
    {
    }

    public sealed class MoveStateMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public int OriginZ { get; set; }
        public int DestX { get; set; }
        public int DestY { get; set; }
        public int DestZ { get; set; }
        public bool Running { get; set; } = true;
    }

    public sealed class ActionUseNetMsg
    {
        public int ActionId { get; set; }
        public bool CtrlPressed { get; set; }
        public bool ShiftPressed { get; set; }
    }

    /// <summary>Payload of <c>AutoAttackStart</c> / <c>AutoAttackStop</c>.</summary>

    public sealed class ChangeMoveTypeNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public bool Running { get; set; } = true;
        /// <summary>Effective L2 run speed now (swim speed when <see cref="InWater"/>).</summary>
        public float RunSpeed { get; set; }
        /// <summary>Effective L2 walk speed now (swim speed when <see cref="InWater"/>).</summary>
        public float WalkSpeed { get; set; }
        public bool InWater { get; set; }
    }

    /// <summary>
    /// Mobius <c>EtcStatusUpdate</c> (<c>EtcStatusUpdate.java:32-45</c>): the status-bar oddities. The client needs the
    /// weight penalty level to draw the overload icon and HP-bar tint, and the grade-penalty flag for its own icon.
    /// Sent to the owner only.
    /// </summary>

    public sealed class PositionCorrectionMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
    }

    /// <summary>Mobius/V2 ItemList row (self bag + paperdoll) for Net enter.</summary>

    public sealed class ChangeWaitTypeNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        /// <summary>0 = sitting, 1 = standing (Interlude <c>ChangeWaitType.WT_SITTING/WT_STANDING</c>).</summary>
        public int WaitType { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
    }

    /// <summary>Mobius <c>SocialAction</c> (gesture ids 2–14, 15 = level up).</summary>

    public sealed class SocialActionNetMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int ActionId { get; set; }
    }

    /// <summary>Forget / leave knownlist (Mobius DeleteObject) for Net peers.</summary>
}
