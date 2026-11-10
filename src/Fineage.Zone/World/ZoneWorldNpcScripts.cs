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
        if (scriptName.Equals("SymbolMaker", StringComparison.OrdinalIgnoreCase))
            return SymbolMakerBypass(session, player, npc, scriptEvent);
        return VillageMasterBypass(session, player, npc, scriptName, scriptEvent);
    }
}
