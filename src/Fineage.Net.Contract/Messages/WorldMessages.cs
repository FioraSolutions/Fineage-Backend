namespace Fineage.Net.Contract
{
    public sealed class ZoneAuthMsg
    {
        public string Account { get; set; } = "";
        public string Token { get; set; } = "";
    }

    public sealed class ZoneAuthResultMsg
    {
        public bool Ok { get; set; }
        public string Code { get; set; } = "";
        public string Message { get; set; } = "";
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
    }

    public sealed class EnterWorldMsg
    {
    }

    public enum EntityKindNet : byte
    {
        Player = 1,
        Npc = 2,
        Item = 3,
    }

    public sealed class EntitySpawnMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public EntityKindNet Kind { get; set; }
        public bool IsSelf { get; set; }
        public string Name { get; set; } = "";
        public int TemplateId { get; set; }
        public int ClassId { get; set; }
        public int Race { get; set; }
        public int Sex { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
        public float RunSpeed { get; set; }
        public float WalkSpeed { get; set; }
        public bool Running { get; set; } = true;
    }

    public sealed class PlayerStatsMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int Level { get; set; }
        public long Exp { get; set; }
        public int Sp { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Mp { get; set; }
        public int MaxMp { get; set; }
        public int Cp { get; set; }
        public int MaxCp { get; set; }
    }

    public sealed class MoveInputMsg
    {
        public int DestX { get; set; }
        public int DestY { get; set; }
        public int DestZ { get; set; }
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

    public sealed class PositionCorrectionMsg
    {
        public ulong EntityValue { get; set; }
        public uint EntityGeneration { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Heading { get; set; }
    }
}
