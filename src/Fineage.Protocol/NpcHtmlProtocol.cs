namespace Fineage.Protocol;

/// <summary>
/// NPC HTML dialog wire. <see cref="Bypass"/> is the Mobius <c>RequestBypassToServer</c> analogue.
/// <see cref="Html"/> is the Mobius <c>NpcHtmlMessage</c> analogue.
/// Quest steps stay on <see cref="QuestProtocol.Dialog"/> (1062).
/// Layout: <c>docs/npc-html-v2-protocol.md</c>.
/// </summary>
public static class NpcHtmlProtocol
{
    public const ushort Bypass = 95;
    public const ushort Html = 1094;

    /// <summary>
    /// Talk and bypass distance, 3D. Same value as <see cref="QuestProtocol.TalkRange"/>.
    /// Mobius Interlude <c>Npc.INTERACTION_DISTANCE</c> is 250; this server keeps 150
    /// so quest talk and the HTML window use one distance.
    /// </summary>
    public const int InteractionDistance = 150;

    /// <summary>The object is an attackable. Talk and bypass do not open a dialog.</summary>
    public const string Attackable = "npc_attackable";

    public static bool InRange(int x1, int y1, int z1, int x2, int y2, int z2, int range = InteractionDistance)
    {
        long dx = x1 - (long)x2;
        long dy = y1 - (long)y2;
        long dz = z1 - (long)z2;
        long limit = (long)range * range;
        return dx * dx + dy * dy + dz * dz <= limit;
    }
}
