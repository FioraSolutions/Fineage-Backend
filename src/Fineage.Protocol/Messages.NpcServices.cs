namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>Opcode <see cref="MsgType.TeleportSelectRequest"/> (105).</summary>
public sealed class TeleportSelectRequestMsg
{
    public int NpcObjectId { get; set; }
    public string ListName { get; set; } = "";
    public int LocationId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.SupportMagicRequest"/> (108).</summary>
public sealed class SupportMagicRequestMsg
{
    public int NpcObjectId { get; set; }
}

/// <summary>Opcode <see cref="MsgType.GiveBlessingRequest"/> (109).</summary>
public sealed class GiveBlessingRequestMsg
{
    public int NpcObjectId { get; set; }
}

/// <summary>One row of <see cref="TeleportListMsg"/>.</summary>
public sealed class TeleportLocationMsg
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int FeeItemId { get; set; }
    /// <summary>What this player pays now, after the free-level and evening discount rules. 0 is free.</summary>
    public int FeeCount { get; set; }
}

/// <summary>Opcode <see cref="MsgType.TeleportList"/> (1105).</summary>
public sealed class TeleportListMsg
{
    public int NpcObjectId { get; set; }
    public string ListName { get; set; } = "";
    public string ListType { get; set; } = "";
    public TeleportLocationMsg[] Locations { get; set; } = [];
}

/// <summary>Opcode <see cref="MsgType.NewbieBuffResult"/> (1108).</summary>
public sealed class NewbieBuffResultMsg
{
    public int NpcObjectId { get; set; }
    public int[] SkillIds { get; set; } = [];
    /// <summary>Empty when buffs were applied. Otherwise <see cref="NewbieBuffRefusal"/> name is not used; this is a short code: low, high, newbie, class, none.</summary>
    public string Refusal { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.BlessingResult"/> (1109).</summary>
public sealed class BlessingResultMsg
{
    public int NpcObjectId { get; set; }
    public int SkillId { get; set; }
    public bool Accepted { get; set; }
}
