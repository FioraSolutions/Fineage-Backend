using Fineage.Accounts;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Issue #950: every gatekeeper standing in the full world opens its own Mobius page and every row of every list it
/// offers lands the player there. A destination may only be refused for an explained reason (the refusal code and
/// the system message the player sees); nothing is refused silently.
/// </summary>
public sealed class GatekeeperTeleportCoverageTests
{
    const int Adena = 57;
    const string NothingToSay = "I have nothing to say to you.";

    /// <summary>
    /// Teleporters whose first page Mobius writes from a script, not from <c>html/teleporter</c>: Kurfa and Teranu
    /// Mardui talk through <c>ai/areas/KetraOrcSupport</c> / <c>VarkaSilenosSupport</c>, which Fineage has not ported.
    /// Castle chamberlains (Merchant), clan hall managers and dungeon gatekeepers also open script pages, so the page
    /// check covers plain <c>Teleporter</c> NPCs only; their destinations are all checked below.
    /// </summary>
    static readonly HashSet<int> ScriptPageTeleporters = [31376, 31383];

    /// <summary>Every Mobius type whose class overrides <c>getHtmlPath</c>: it owns a typed folder.</summary>
    static bool OwnsTypedPage(string? npcType) =>
        npcType is not null && NpcHtmlPaths.Folder(npcType) != "default";

    /// <summary>
    /// The NPC types the report called out by name, where a default page is never legitimate: a ziggurat's page is the
    /// only way into the Necropolis/Catacomb and a tomb NPC's page is its whole function. Mobius' datapack ships a page
    /// for all of them, so an NPC of these types that serves "I have nothing to say" is a missing import.
    /// </summary>
    static bool MustOwnPage(string? npcType) => npcType is "DungeonGatekeeper" or "SepulcherNpc";

    [Fact]
    public async Task Every_gatekeeper_page_and_every_destination_lands_or_refuses_with_a_reason()
    {
        var teleports = TeleportCatalog.LoadDefault();
        var bench = new WorldBench(includeEnabledMaps: true, teleportOffset: 0);
        var gatekeepers = new List<ZoneEntity>();
        var pageOwners = new List<ZoneEntity>();
        var seen = new HashSet<int>();
        var pageSeen = new HashSet<int>();
        foreach (int objectId in bench.World.NpcObjectIds)
        {
            Assert.True(bench.World.TryGetEntityById(objectId, out var npc));
            if (teleports.ListsOf(npc.NpcId).Count > 0 && seen.Add(npc.NpcId))
                gatekeepers.Add(npc);
            if (OwnsTypedPage(npc.NpcType) && pageSeen.Add(npc.NpcId))
                pageOwners.Add(npc);
        }

        Assert.Contains(gatekeepers, npc => npc.NpcId == 30080); // Clarissa, Giran (the report).
        Assert.True(gatekeepers.Count >= 100, $"only {gatekeepers.Count} spawned gatekeepers");

        // Every fee item any list charges, so a refusal can only come from the destination, never the purse.
        var fees = teleports.GatekeeperIds
            .SelectMany(teleports.ListsOf)
            .SelectMany(list => list.Locations)
            .Select(loc => loc.FeeItemId)
            .Append(Adena)
            .Distinct()
            .Select(itemId => new StoredItem(itemId, 1_000_000_000, -1))
            .ToArray();
        var first = gatekeepers[0];
        BenchPlayer actor = await bench.EnterAsync(
            "gates", "Wanderer", items: fees, level: 76, x: first.X + 30, y: first.Y, z: first.Z);
        Assert.True(bench.World.TrySetNoble(actor.ObjectId, true));

        var silentPages = new List<string>();
        var failures = new List<string>();
        int landed = 0;
        var refusals = new Dictionary<string, int>();

        // The types the report named must serve a page of their own, not the default one. Other typed folders
        // (Merchant, Trainer, ...) legitimately fall through to npcdefault.htm when the datapack has no page for
        // that npcId — Mobius' own getHtmlPath returns the same default in that case.
        foreach (var npc in pageOwners)
        {
            if (!MustOwnPage(npc.NpcType))
                continue;
            actor.MoveTo(npc.X + 30, npc.Y, npc.Z);
            bench.Rec.Calls.Clear();
            Assert.Null(await actor.Talk(npc.ObjectId));
            if (LastHtml(bench).Contains(NothingToSay, StringComparison.Ordinal))
                silentPages.Add($"{npc.NpcId} {npc.Name} ({npc.NpcType})");
        }

        foreach (var npc in gatekeepers)
        {
            actor.MoveTo(npc.X + 30, npc.Y, npc.Z);
            bench.Rec.Calls.Clear();
            Assert.Null(await actor.Talk(npc.ObjectId));

            foreach (var list in teleports.ListsOf(npc.NpcId))
            {
                foreach (var loc in list.Locations)
                {
                    actor.MoveTo(npc.X + 30, npc.Y, npc.Z);
                    bench.Rec.Calls.Clear();
                    string? error = await actor.Teleport(npc.ObjectId, list.Name, loc.Id);
                    string where = $"{npc.NpcId} {npc.Name} {list.Name}#{loc.Id} {loc.Name}";
                    if (error != null)
                    {
                        failures.Add($"{where}: error {error}");
                        continue;
                    }

                    int message = LastSystemMessageId(bench);
                    if (message == NpcServiceProtocol.Teleported)
                    {
                        if (actor.Player.X != loc.X || actor.Player.Y != loc.Y)
                            failures.Add($"{where}: reported teleported but stands at {actor.Player.X},{actor.Player.Y}");
                        else
                            landed++;
                        continue;
                    }

                    // Explained refusal: the code names why, the message is what the player reads.
                    string code = NpcServiceProtocol.TeleportRefusalCode(message);
                    refusals[code] = refusals.GetValueOrDefault(code) + 1;
                    if (code != "siege")
                        failures.Add($"{where}: refused {code}");
                }
            }
        }

        Assert.True(silentPages.Count == 0 && failures.Count == 0,
            "gatekeepers without their own page:\n" + string.Join("\n", silentPages) + "\nfailures:\n" + string.Join("\n", failures));
        Assert.True(failures.Count == 0, $"landed={landed}\n" + string.Join("\n", failures));
        Assert.True(landed > 1000, $"landed={landed} refusals={string.Join(",", refusals)}");
    }

    static string LastHtml(WorldBench bench) =>
        Assert.IsType<NpcHtmlMsg>(bench.Rec.Of("FanoutNpcHtml").Last()[1]).Html;

    static int LastSystemMessageId(WorldBench bench) =>
        Assert.IsType<SystemMessageMsg>(bench.Rec.Of("FanoutSystemMessage").Last()[1]).Id;
}
