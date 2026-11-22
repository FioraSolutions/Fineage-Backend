using Fineage.Game.Services;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    NpcScriptRegistry? _npcScripts;
    readonly HashSet<string> _loggedUnknownNpcScripts = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _loggedNpcScriptMiss = new(StringComparer.OrdinalIgnoreCase);
    internal int UnknownNpcScriptLogCount { get; private set; }
    internal NpcScriptRegistry NpcScripts => _npcScripts ??= NpcScriptRegistry.LoadDefault();
    string? NpcScriptBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptName, string scriptEvent)
    {
        scriptName = (scriptName ?? "").Trim();
        scriptEvent ??= "";
        if (!NpcScripts.TryGet(scriptName, out var entry))
        {
            LogUnknownNpcScript(scriptName, npc.NpcId);
            SendScriptUnavailable(session, player, npc);
            return null;
        }
        return entry.Pipeline switch
        {
            "village_master" => VillageMasterBypass(session, player, npc, scriptName, scriptEvent),
            "pledge" => TryClanMasterScript(session, player, npc, scriptName, scriptEvent) ? null : FinishUnhandledScriptPage(session, player, npc, scriptName, scriptEvent, "pledge declined"),
            "noblesse_teleport" => NoblesseTeleportBypass(session, player, npc),
            "monument_of_heroes" => MonumentOfHeroesBypass(session, player, npc, scriptEvent),
            "symbol_maker" => SymbolMakerBypass(session, player, npc, scriptEvent),
            "wedding" => WeddingNpcBypass(session, player, npc, scriptEvent),
            "newbie_travel" => _newbieBuffs.TravelToken is { } travel ? NewbieTravel(session, player, npc, travel, scriptEvent) : FinishUnhandledScriptPage(session, player, npc, scriptName, scriptEvent, "travelToken data missing"),
            "newbie_helper" => FinishUnhandledScriptPage(session, player, npc, scriptName, scriptEvent, "use NpcTalk first-talk"),
            "oracle_teleport" => OracleTeleportBypass(session, player, npc, scriptEvent),
            "monster_race_teleport" => TeleportToRaceTrackBypass(session, player, npc, scriptEvent),
            "raid_boss_info" => RaidbossInfoBypass(session, player, npc, scriptEvent),
            "stub" => FinishStubScript(session, player, npc),
            _ => FinishUnhandledScriptPage(session, player, npc, scriptName, scriptEvent, $"unknown pipeline '{entry.Pipeline}'")
        };
    }
    string? NoblesseTeleportBypass(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        SendNoblesPage(session, player, npc, player.Noble);
        return null;
    }
    string? FinishStubScript(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        SendScriptUnavailable(session, player, npc);
        return null;
    }
    string? FinishUnhandledScriptPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, string scriptName, string scriptEvent, string detail)
    {
        LogNpcScriptMiss(scriptName, npc.NpcId, scriptEvent, detail);
        SendScriptUnavailable(session, player, npc);
        return null;
    }
    void SendScriptUnavailable(ZoneSession session, ZoneEntity player, ZoneEntity npc) => SendHtml(session, player, npc, QuestBypassHandler.Unavailable);
    void LogUnknownNpcScript(string scriptId, int npcId)
    {
        if (!_loggedUnknownNpcScripts.Add($"unknown:{scriptId}")) return;
        UnknownNpcScriptLogCount++;
        ZoneLog.Warn($"[npc-script] unhandled script id='{scriptId}' npcId={npcId} — showed unavailable page (not in {NpcScriptRegistry.FileName})");
    }
    void LogNpcScriptMiss(string scriptId, int npcId, string scriptEvent, string detail)
    {
        if (!_loggedNpcScriptMiss.Add($"{scriptId}:{npcId}:{scriptEvent}")) return;
        ZoneLog.Warn($"[npc-script] script '{scriptId}' npcId={npcId} event='{scriptEvent}' — {detail}; showed unavailable page");
    }
    internal string? NpcScriptBypassForTest(ZoneSession session, int npcId, string script, string evt, out string html)
    {
        html = "";
        using (_affinity.Step())
        {
            if (!TryPlayer(session, out var player)) return "world";
            var npc = new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = npcId, Name = "Npc", ObjectId = 90_001, NpcType = "Folk" };
            var error = NpcScriptBypass(session, player, npc, script, evt);
            html = LastNpcHtmlForTest ?? "";
            return error;
        }
    }
}
