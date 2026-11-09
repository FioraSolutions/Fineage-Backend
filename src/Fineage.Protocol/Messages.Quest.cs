namespace Fineage.Protocol;

// Plain DTOs for the zone world and the lobby. They are no longer serialized: the Net
// PacketEnvelope codec in Fineage.Net.Contract owns the wire, so the old key numbers are gone
// with the V2 transport.

/// <summary>Opcode <see cref="MsgType.RequestQuestList"/> (42). Empty body.</summary>
public sealed class RequestQuestListMsg
{
}

/// <summary>Opcode <see cref="MsgType.RequestQuestAbort"/> (43).</summary>
public sealed class RequestQuestAbortMsg
{
    public int QuestId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.NpcTalk"/> (44).
/// First-talk hook, or the NPC's HTML page when no quest step matches.
/// </summary>
public sealed class NpcTalkMsg
{
    public int NpcObjectId { get; set; }
}

/// <summary>
/// Opcode <see cref="MsgType.BypassRequest"/> (95). Mobius <c>RequestBypassToServer</c>.
/// <see cref="Command"/> may be <c>npc_&lt;objectId&gt;_Chat 1</c> or the command alone.
/// </summary>
public sealed class BypassRequestMsg
{
    public int NpcObjectId { get; set; }
    public string Command { get; set; } = "";
}

/// <summary>
/// Opcode <see cref="MsgType.QuestEvent"/> (45).
/// Empty <see cref="Event"/> is the talk hook. Any other token is the adv-event hook.
/// </summary>
public sealed class QuestEventMsg
{
    public int NpcObjectId { get; set; }
    public int QuestId { get; set; }
    public string Event { get; set; } = "";
}

/// <summary>One started quest in <see cref="QuestListMsg"/>.</summary>
public sealed class QuestJournalEntryMsg
{
    public int QuestId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>1 while started. Completed one-time quests are not in this list.</summary>
    public int State { get; set; }
    public int Cond { get; set; }
    /// <summary>Raw <c>__compltdStateFlags</c>. Zero when the journal uses <see cref="Cond"/>.</summary>
    public int ProgressFlags { get; set; }
    /// <summary>Flags when non-zero, otherwise <see cref="Cond"/>.</summary>
    public int JournalWord { get; set; }
    public bool Repeatable { get; set; }
}

/// <summary>Opcode <see cref="MsgType.QuestList"/> (1061).</summary>
public sealed class QuestListMsg
{
    public QuestJournalEntryMsg[] Active { get; set; } = [];
    /// <summary>128 bytes. Bit <c>id % 8</c> of byte <c>id / 8</c> for a completed one-time quest whose id is 0..1023.</summary>
    public byte[] CompletedMask { get; set; } = [];
    public int[] CompletedQuestIds { get; set; } = [];
    public int MaxActive { get; set; }
}

/// <summary>Opcode <see cref="MsgType.QuestDialog"/> (1062).</summary>
public sealed class QuestDialogMsg
{
    public int NpcObjectId { get; set; }
    public int NpcId { get; set; }
    public int QuestId { get; set; }
    /// <summary><c>first_talk</c>, <c>talk</c>, <c>event</c>, <c>kill</c>, or <c>default</c>.</summary>
    public string Hook { get; set; } = "";
    public string Event { get; set; } = "";
    public string Html { get; set; } = "";
}

/// <summary>Opcode <see cref="MsgType.QuestSound"/> (1063).</summary>
public sealed class QuestSoundMsg
{
    public string Name { get; set; } = "";
}

/// <summary>
/// Opcode <see cref="MsgType.NpcHtml"/> (1094). Mobius <c>NpcHtmlMessage</c>.
/// Quest steps stay on <see cref="QuestDialogMsg"/>. <see cref="ItemId"/> is 0 until item HTML exists.
/// </summary>
public sealed class NpcHtmlMsg
{
    public int NpcObjectId { get; set; }
    public int NpcId { get; set; }
    public string Html { get; set; } = "";
    public int ItemId { get; set; }
}
