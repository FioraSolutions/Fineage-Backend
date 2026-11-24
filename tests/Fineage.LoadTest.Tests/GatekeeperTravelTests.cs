using Fineage.LoadTest;
using Fineage.LoadTest.Bot;
using Fineage.LoadTest.Models;
using Fineage.LoadTest.Reporting;
using Fineage.LoadTest.Travel;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Xunit;

namespace Fineage.LoadTest.Tests;

public class GatekeeperTravelTests
{
    static readonly Lazy<GatekeeperDirectory> Repo = new(() =>
        GatekeeperDirectory.Load(GatekeeperDirectory.ResolveDataDir(null)));

    const int SelfId = 268_500_001;
    const int ClarissaObject = 70_001;
    const int Clarissa = 30080; // Giran gatekeeper

    [Fact]
    public void Every_town_gatekeeper_offers_another_town()
    {
        var dir = Repo.Value;
        foreach (var spawn in dir.Spawns)
        {
            Assert.Contains(dir.Destinations(spawn.NpcId), d =>
                dir.IsTown(d.X, d.Y) &&
                !GatekeeperDirectory.Within(d.X, d.Y, spawn.X, spawn.Y, GatekeeperDirectory.TownRadius));
        }

        // Village gatekeepers of every Interlude town are travel stops.
        int[] towns = [30006, 30059, 30080, 30134, 30146, 30177, 30233, 30256, 30320, 30540, 30576, 30848, 30899, 31275, 31320, 31964];
        var missing = towns.Where(id => !dir.NpcIds.Contains(id)).ToList();
        Assert.True(missing.Count == 0, "town gatekeepers missing: " + string.Join(", ", missing));
        // In-building lifts (Ivory Tower floors, Rune temple shuttle) only move within one town.
        Assert.Contains(dir.NonTownGatekeepers, s => s.NpcId == 30716);
        Assert.DoesNotContain(dir.NonTownGatekeepers, s => towns.Contains(s.NpcId));
    }

    [Fact]
    public void Datapack_gatekeeper_pages_lead_to_teleport_links()
    {
        // Old snapshot pages (Talking Island: Chat 1 -> teleport NORMAL n) and upstream ones (Giran: showTeleports).
        string html = Path.Combine(Repo.Value.DataDir, "html", "teleporter");
        var ti = GatekeeperHtml.Links(File.ReadAllText(Path.Combine(html, "30006.htm")));
        Assert.Contains(ti, l => l.Kind == GatekeeperLinkKind.Chat && l.Id == 1);
        var tiList = GatekeeperHtml.Links(File.ReadAllText(Path.Combine(html, "30006-1.htm")));
        Assert.Contains(tiList, l => l.Kind == GatekeeperLinkKind.Teleport && l.List == "NORMAL");
        var giran = GatekeeperHtml.Links(File.ReadAllText(Path.Combine(html, "30080.htm")));
        Assert.Contains(giran, l => l.Kind == GatekeeperLinkKind.ShowTeleports);
        Assert.DoesNotContain(giran, l => l.Command.StartsWith("Quest", StringComparison.Ordinal));
    }

    [Fact]
    public void Traveller_talks_opens_list_teleports_and_verifies_arrival()
    {
        var (t, now) = AtGiranGatekeeper();

        var talk = t.Step(now);
        Assert.Equal(NetOpcode.NpcTalk, talk!.Value.Opcode);
        Assert.Equal(ClarissaObject, NetCodec.DecodeNpcTalk(talk.Value.Payload).NpcObjectId);

        t.OnFrame(NetOpcode.NpcHtml, Page("<a action=\"bypass npc_70001_showTeleports\">Teleport</a>" +
                                          "<a action=\"bypass -h npc_70001_Quest NoblesseTeleport\">Nobles</a>"));
        var show = t.Step(now);
        Assert.Equal("npc_70001_showTeleports", NetCodec.DecodeBypassRequest(show!.Value.Payload).Command);

        var dest = DestinationsOf(Clarissa).First(d => Repo.Value.IsTown(d.X, d.Y) &&
            !GatekeeperDirectory.Within(d.X, d.Y, 83400, 147943, GatekeeperDirectory.TownRadius));
        t.OnFrame(NetOpcode.NpcHtml, Page($"<a action=\"bypass -h npc_70001_teleport NORMAL {dest.Id}\">{dest.Name}</a>"));
        var go = t.Step(now);
        Assert.Equal($"npc_70001_teleport NORMAL {dest.Id}", NetCodec.DecodeBypassRequest(go!.Value.Payload).Command);
        Assert.Equal(1, t.Stats.Attempted);

        t.OnFrame(NetOpcode.SystemMessage, Sys(NpcServiceProtocol.Teleported));
        t.OnFrame(NetOpcode.TeleportToLocation, Tele(dest.X + 30, dest.Y - 30, dest.Z));
        Assert.Null(t.Step(now));
        Assert.Equal(1, t.Stats.Succeeded);
        Assert.Equal(0, t.Stats.UnexplainedCount);
        Assert.Equal((dest.X + 30, dest.Y - 30, dest.Z), t.Position);
    }

    [Fact]
    public void Server_refusal_with_a_reason_code_is_explained()
    {
        var t = Teleporting(out var now);
        t.OnFrame(NetOpcode.SystemMessage, Sys(NpcServiceProtocol.DestinationUnloaded));
        Assert.Null(t.Step(now));
        Assert.Equal(1, t.Stats.RejectedByReason["destination_unloaded"]);
        Assert.Equal(0, t.Stats.UnexplainedCount);
    }

    [Fact]
    public void Reject_frame_timeout_and_wrong_landing_are_unexplained()
    {
        var a = Teleporting(out var now);
        a.OnFrame(NetOpcode.Reject, NetCodec.EncodeReject(new RejectMsg { Code = "bad_request", Message = "Bypass failed." }));
        a.Step(now);
        Assert.Equal(1, a.Stats.UnexplainedCount);
        Assert.Contains("bad_request", a.Stats.Unexplained.Single());

        var b = Teleporting(out now);
        b.Step(now + GatekeeperTraveler.TeleportTimeout + TimeSpan.FromSeconds(1));
        Assert.Equal(1, b.Stats.UnexplainedCount);

        var c = Teleporting(out now);
        c.OnFrame(NetOpcode.TeleportToLocation, Tele(83400, 147943, -3400));
        c.Step(now);
        Assert.Equal(1, c.Stats.UnexplainedCount);
        Assert.Equal(0, c.Stats.Succeeded);
    }

    [Fact]
    public void A_page_without_a_town_teleport_is_unexplained()
    {
        var (t, now) = AtGiranGatekeeper();
        t.Step(now);
        t.OnFrame(NetOpcode.NpcHtml, Page("<a action=\"bypass -h npc_70001_Quest\">Quest</a>"));
        Assert.Null(t.Step(now));
        Assert.Equal(1, t.Stats.UnexplainedCount);
        Assert.Equal(0, t.Stats.Attempted);
    }

    [Fact]
    public void Far_from_any_gatekeeper_the_traveller_walks_toward_the_nearest()
    {
        var stats = new TravelStats();
        var t = new GatekeeperTraveler(Repo.Value, 1, TimeSpan.Zero, stats);
        // Elven starting area (classes.json spawn), about 10k from the elven village gatekeeper.
        t.OnFrame(NetOpcode.EntitySpawn, SelfSpawn(46045, 41251, -3440));
        var walk = t.Step(DateTimeOffset.UnixEpoch);
        Assert.Equal(NetOpcode.MoveInput, walk!.Value.Opcode);
        var move = NetCodec.DecodeMoveInput(walk.Value.Payload);
        var gk = Repo.Value.Nearest(46045, 41251);
        Assert.True(GatekeeperDirectory.DistSq(move.DestX, move.DestY, gk.X, gk.Y) <
                    GatekeeperDirectory.DistSq(46045, 41251, gk.X, gk.Y));
        Assert.Equal(1, stats.WalkHops);
    }

    [Theory]
    [InlineData(30256, -12736, 122816, -3114)] // Gludio: Chat 4 is an info dead end, Chat 1 holds the list
    [InlineData(30006, -84108, 244604, -3728)] // Talking Island
    [InlineData(30080, 83396, 147904, -3404)] // Giran: showTeleports
    public void Traveller_reaches_a_town_teleport_through_the_datapack_pages(int npcId, int x, int y, int z)
    {
        string dir = Path.Combine(Repo.Value.DataDir, "html", "teleporter");
        var t = new GatekeeperTraveler(Repo.Value, 1, TimeSpan.Zero, new TravelStats());
        t.OnFrame(NetOpcode.EntitySpawn, SelfSpawn(x + 50, y + 50, z));
        t.OnFrame(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
        {
            EntityValue = ClarissaObject,
            Kind = EntityKindNet.Npc,
            TemplateId = npcId,
            X = x,
            Y = y,
            Z = z
        }));
        var now = DateTimeOffset.UnixEpoch;
        Assert.Equal(NetOpcode.NpcTalk, t.Step(now)!.Value.Opcode);
        string page = File.ReadAllText(Path.Combine(dir, $"{npcId}.htm"));
        for (int i = 0; i < GatekeeperTraveler.MaxPagesPerTalk; i++)
        {
            t.OnFrame(NetOpcode.NpcHtml, NetCodec.EncodeNpcHtml(new NpcHtmlNetMsg { NpcObjectId = ClarissaObject, NpcId = npcId, Html = page }));
            var next = t.Step(now);
            Assert.True(next != null, string.Join(" | ", t.Stats.Unexplained));
            string cmd = NetCodec.DecodeBypassRequest(next!.Value.Payload).Command["npc_70001_".Length..];
            if (cmd.StartsWith("teleport ", StringComparison.Ordinal))
            {
                Assert.Equal(1, t.Stats.Attempted);
                return;
            }

            // A missing Chat page is served as npcdefault.htm by the zone (logged as [npc-html]).
            string chat = Path.Combine(dir, $"{npcId}-{(cmd.Length > 5 ? cmd[5..] : "")}.htm");
            page = !cmd.StartsWith("Chat ", StringComparison.Ordinal) ? ServerTeleportList(npcId)
                : File.Exists(chat) ? File.ReadAllText(chat)
                : File.ReadAllText(Path.Combine(Repo.Value.DataDir, "html", "npcdefault.htm"));
        }

        Assert.Fail("no teleport bypass within the page budget");
    }

    /// <summary>The zone's showTeleports page (ZoneWorldNpcServices.ShowTeleportList) for a NORMAL list.</summary>
    static string ServerTeleportList(int npcId) =>
        "<html><body>" + string.Concat(DestinationsOf(npcId).Select(d =>
            $"<a action=\"bypass -h npc_70001_teleport {d.List} {d.Id}\">{d.Name}</a><br1>")) + "</body></html>";

    [Fact]
    public void A_gatekeeper_missing_from_the_knownlist_is_unexplained()
    {
        var stats = new TravelStats();
        var t = new GatekeeperTraveler(Repo.Value, 1, TimeSpan.Zero, stats);
        t.OnFrame(NetOpcode.EntitySpawn, SelfSpawn(83400, 147943, -3404)); // Giran square, Clarissa never spawned
        Assert.Null(t.Step(DateTimeOffset.UnixEpoch));
        Assert.Contains("not in the knownlist", stats.Unexplained.Single());
    }

    [Fact]
    public void Position_timeline_answers_where_a_bot_was_at_a_time()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var frames = new[]
        {
            new ObservedFrame(t0, NetOpcode.EntitySpawn, SelfSpawn(46045, 41251, -3440)),
            new ObservedFrame(t0.AddSeconds(10), NetOpcode.TeleportToLocation, Tele(83400, 147943, -3400))
        };
        var line = Fineage.LoadTest.Consistency.PositionTimeline.From(frames);
        Assert.True(line.TryAt(t0.AddSeconds(5), out int x, out _));
        Assert.Equal(46045, x);
        Assert.True(line.TryAt(t0.AddSeconds(11), out x, out _));
        Assert.Equal(83400, x);
        Assert.False(line.TryAt(t0.AddSeconds(-1), out _, out _));
    }

    [Theory]
    [InlineData(100, 0.2, 20)]
    [InlineData(50, 0.2, 10)]
    [InlineData(7, 0.2, 1)]
    [InlineData(100, 0.0, 0)]
    [InlineData(10, 1.0, 10)]
    public void Travel_share_picks_exactly_that_many_bots(int bots, double share, int expected)
    {
        var s = new LoadScenario { GatekeeperTravelShare = share };
        Assert.Equal(expected, Enumerable.Range(0, bots).Count(s.IsGatekeeperTraveler));
    }

    [Fact]
    public void Default_share_is_twenty_percent_and_bad_values_throw()
    {
        Assert.Equal(0.2, new LoadScenario().ResolvedGatekeeperTravelShare);
        Assert.Throws<InvalidOperationException>(() => new LoadScenario { GatekeeperTravelShare = 1.5 }.ResolvedGatekeeperTravelShare);
    }

    [Fact]
    public void Every_shipped_scenario_loads_and_the_full_world_mix_travels()
    {
        string dir = Path.Combine(Path.GetDirectoryName(Repo.Value.DataDir)!, "tools", "Fineage.LoadTest", "scenarios");
        foreach (string path in Directory.EnumerateFiles(dir, "*.yaml"))
        {
            var s = ScenarioLoader.Load(path);
            Assert.True(s.ResolvedGatekeeperTravelShare > 0, $"{Path.GetFileName(path)} has no gatekeeper travellers");
        }

        Assert.Equal(0.2, ScenarioLoader.Load(Path.Combine(dir, "bots-1000-mixed.yaml")).ResolvedGatekeeperTravelShare);
    }

    [Fact]
    public void Unknown_action_weight_fails_the_scenario()
    {
        var s = new LoadScenario { Name = "x" };
        s.ActionWeights["teleport"] = 0.03;
        var ex = Assert.Throws<InvalidOperationException>(() => ScenarioLoader.Validate(s));
        Assert.Contains("gatekeeperTravelShare", ex.Message);
    }

    [Fact]
    public void Summary_fails_the_run_only_on_unexplained_and_renders()
    {
        var ok = new TravelStats { Attempted = 3, Succeeded = 2 };
        ok.Rejected("siege");
        var summary = TravelSummary.From([ok]);
        Assert.Null(summary.FailReason());
        Assert.Equal(1, summary.Rejected);

        var bad = new TravelStats { Attempted = 1 };
        bad.Fail("teleport npc=30080 -> Dion: no TeleportToLocation in 15s");
        var failing = TravelSummary.From([ok, bad]);
        Assert.Contains("1 unexplained", failing.FailReason());

        var report = new RunReport { ScenarioName = "t", Target = "fineage", GatekeeperTravel = failing };
        string md = CompactMarkdownRenderer.Render(report);
        Assert.Contains("## Gatekeeper travel", md);
        Assert.Contains("rejected `siege`: 1", md);
        Assert.Contains("UNEXPLAINED: teleport npc=30080", md);
    }

    [Fact]
    public void Tracker_follows_a_teleport_snap()
    {
        var log = new BotObservationLog(0);
        log.Record(NetOpcode.EntitySpawn, SelfSpawn(-84108, 244604, -3728), DateTimeOffset.UnixEpoch);
        log.Record(NetOpcode.TeleportToLocation, Tele(83400, 147943, -3400), DateTimeOffset.UnixEpoch);
        var self = log.Tracker.Entities[(ulong)SelfId];
        Assert.Equal((83400, 147943), (self.X, self.Y));
    }

    static IEnumerable<TeleportDestination> DestinationsOf(int npcId) => Repo.Value.Destinations(npcId);

    static (GatekeeperTraveler, DateTimeOffset) AtGiranGatekeeper()
    {
        var t = new GatekeeperTraveler(Repo.Value, 1, TimeSpan.Zero, new TravelStats());
        t.OnFrame(NetOpcode.EntitySpawn, SelfSpawn(83400, 147943, -3404));
        t.OnFrame(NetOpcode.EntitySpawn, NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
        {
            EntityValue = ClarissaObject,
            Kind = EntityKindNet.Npc,
            TemplateId = Clarissa,
            Name = "Clarissa",
            X = 83396,
            Y = 147904,
            Z = -3404
        }));
        return (t, DateTimeOffset.UnixEpoch);
    }

    static GatekeeperTraveler Teleporting(out DateTimeOffset now)
    {
        var (t, start) = AtGiranGatekeeper();
        now = start;
        t.Step(now);
        var dest = DestinationsOf(Clarissa).First(d => Repo.Value.IsTown(d.X, d.Y) &&
            !GatekeeperDirectory.Within(d.X, d.Y, 83400, 147943, GatekeeperDirectory.TownRadius));
        t.OnFrame(NetOpcode.NpcHtml, Page($"<a action=\"bypass -h npc_70001_teleport NORMAL {dest.Id}\">{dest.Name}</a>"));
        Assert.Equal(NetOpcode.BypassRequest, t.Step(now)!.Value.Opcode);
        return t;
    }

    static byte[] SelfSpawn(int x, int y, int z) => NetCodec.EncodeEntitySpawn(new EntitySpawnMsg
    {
        EntityValue = SelfId,
        Kind = EntityKindNet.Player,
        IsSelf = true,
        Name = "Bot0001",
        X = x,
        Y = y,
        Z = z,
        RunSpeed = 120
    });

    static byte[] Page(string body) => NetCodec.EncodeNpcHtml(new NpcHtmlNetMsg
    {
        NpcObjectId = ClarissaObject,
        NpcId = Clarissa,
        Html = "<html><body>" + body + "</body></html>"
    });

    static byte[] Sys(int id) => NetCodec.EncodeSystemMessage(new SystemMessageNetMsg { Id = id, Text = "" });

    static byte[] Tele(int x, int y, int z) => NetCodec.EncodeTeleportToLocation(new TeleportToLocationNetMsg
    {
        ObjectId = SelfId,
        X = x,
        Y = y,
        Z = z
    });
}
