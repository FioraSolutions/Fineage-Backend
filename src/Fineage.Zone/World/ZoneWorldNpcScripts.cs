namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>
    /// Mobius <c>Quest &lt;ScriptName&gt; [event]</c> dispatch.
    /// Named AI scripts (SymbolMaker, …) first; village_master catalog is the catch-all.
    /// </summary>
    string? NpcScriptBypass(
        ZoneSession session,
        ZoneEntity player,
        ZoneEntity npc,
        string scriptName,
        string scriptEvent)
    {
        // Gatekeeper "[Noblesse Exclusive] Teleport" links a NoblesseTeleport script CT0 does not ship; it is the
        // showNoblesSelect page (nobles_select.htm / not_nobles.htm).
        if (scriptName.Equals("NoblesseTeleport", StringComparison.OrdinalIgnoreCase))
        {
            SendNoblesPage(session, player, npc, player.Noble);
            return null;
        }
        if (scriptName.Equals("SymbolMaker", StringComparison.OrdinalIgnoreCase))
            return SymbolMakerBypass(session, player, npc, scriptEvent);
        return VillageMasterBypass(session, player, npc, scriptName, scriptEvent);
    }
}
