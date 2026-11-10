using Fineage.Game.Quests;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// The story quest end to end: offer, accept, progress, reward, abort, and what survives a relog. The retired V2
/// suite walked a full Hello/Auth/EnterWorld handshake before every step; the quest engine does not care about the
/// transport, so this drives the world and reads the recorded Net fanout instead.
/// </summary>
public sealed class NetQuestTests
{
    const int RoxxyIndex = 0;
    const int BitzIndex = 1;
    const int QuestId = 998; // Fineage Roxxy smoke (Mobius id 999 is Newbie Tutorial)

    [Fact]
    public async Task Smoke_quest_talks_reward_and_persist()
    {
        var bench = new WorldBench(clock: FixedClock);
        BenchPlayer actor = await bench.EnterAsync("quester", "Quester");

        var roxxy = TalkingIsland.Villagers[RoxxyIndex];
        var bitz = TalkingIsland.Villagers[BitzIndex];

        // Both NPCs are bootstrapped by the world, but the player starts out of talk range.
        actor.MoveTo(roxxy.X, roxxy.Y, roxxy.Z);
        bench.Rec.Calls.Clear();

        Assert.Null(await actor.Talk(roxxy.ObjectId));
        // Net NPC HTML carries the NPC ids and the page; the hook and quest id live in the quest_event links,
        // which is exactly what the client echoes back.
        var offer = LastDialog(bench.Rec);
        Assert.Equal(roxxy.ObjectId, offer.NpcObjectId);
        Assert.Contains($"quest_event:{QuestId}:accept", offer.Html, StringComparison.Ordinal);
        Assert.Empty(LastQuestList(bench.Rec).Active);

        Assert.Null(await actor.QuestEvent(roxxy.ObjectId, QuestId, "accept"));
        Assert.Equal(QuestProtocol.Acquired, LastSystemMessage(bench.Rec).Id);
        var active = Assert.Single(LastQuestList(bench.Rec).Active);
        Assert.Equal(QuestId, active.QuestId);
        Assert.Equal(1, active.Cond);
        Assert.Equal(1, active.JournalWord);
        Assert.Equal(QuestProtocol.Started, active.State);
        Assert.Contains("Bitz", LastDialog(bench.Rec).Html, StringComparison.Ordinal);

        // The second villager hears the next event without moving: both stand at the same spot.
        Assert.Null(await actor.QuestEvent(bitz.ObjectId, QuestId, "heard"));
        Assert.Equal(2, Assert.Single(LastQuestList(bench.Rec).Active).Cond);

        Assert.Null(await actor.Talk(roxxy.ObjectId));
        Assert.Contains($"quest_event:{QuestId}:reward", LastDialog(bench.Rec).Html, StringComparison.Ordinal);

        Assert.Null(await actor.QuestEvent(roxxy.ObjectId, QuestId, "reward"));
        var update = LastInventory(bench.Rec);
        Assert.Equal(2, update.Changes.Length);
        Assert.Contains(update.Changes,
            row => row.ItemId == 57 && row.Count == 25 && row.Change == InventoryChange.Add);
        Assert.Contains(update.Changes, row => row.ItemId == 736 && row.Change == InventoryChange.Modify);
        // The reward notices arrive in order on their own run; the earlier accept notice is still in the recorder.
        var notices = SystemMessageIds(bench.Rec);
        int reward = notices.IndexOf(QuestProtocol.ItemEarned);
        Assert.True(reward >= 0);
        Assert.Equal(
            [QuestProtocol.ItemEarned, QuestProtocol.Adena, QuestProtocol.ExpSp, QuestProtocol.Completed],
            notices.GetRange(reward, 4));

        var finished = LastQuestList(bench.Rec);
        Assert.Empty(finished.Active);
        Assert.Equal(QuestId, Assert.Single(finished.CompletedQuestIds));
        Assert.Equal(1 << (QuestId % 8), finished.CompletedMask[QuestId / 8]);
        Assert.Equal(25, finished.MaxActive);

        // The reward was written through before anything else happened.
        var saved = actor.SavedRecord();
        Assert.Equal(50, saved.Exp);
        Assert.Equal(5, saved.Sp);
        Assert.Equal(1, saved.Level);
        var quest = Assert.Single(saved.Quests!);
        Assert.Equal(QuestId, quest.QuestId);
        Assert.Equal(QuestProtocol.CompletedState, quest.State);
        Assert.Contains(saved.Items!, item => item.ItemId == 736 && item.Count >= 1);
        Assert.Contains(saved.Items!, item => item.ItemId == 57 && item.Count == 25);

        // A fresh session sees the completed journal, which is what the Net enter burst sends.
        await actor.World.LeaveAsync(actor.Session, CancellationToken.None);
        BenchPlayer again = await bench.EnterAsync("quester", "Quester", []);
        var reloaded = bench.World.NetQuestList(again.Session);
        Assert.Equal(QuestId, Assert.Single(reloaded.CompletedQuestIds));
        Assert.Equal(1 << (QuestId % 8), reloaded.CompletedMask[QuestId / 8]);
    }

    [Fact]
    public async Task Abort_removes_a_started_quest_so_it_can_be_accepted_again()
    {
        var bench = new WorldBench(clock: FixedClock);
        BenchPlayer actor = await bench.EnterAsync("aborter", "Aborter");
        var roxxy = TalkingIsland.Villagers[RoxxyIndex];

        actor.MoveTo(roxxy.X, roxxy.Y, roxxy.Z);
        Assert.Null(await actor.Talk(roxxy.ObjectId));
        Assert.Null(await actor.QuestEvent(roxxy.ObjectId, QuestId, "accept"));
        Assert.Single(bench.World.NetQuestList(actor.Session).Active);
        bench.Rec.Calls.Clear();

        Assert.Null(await actor.AbortQuest(QuestId));
        Assert.Equal(QuestProtocol.Cancelled, LastSystemMessage(bench.Rec).Id);
        Assert.Empty(LastQuestList(bench.Rec).Active);
        Assert.Empty(actor.SavedRecord().Quests!);

        Assert.Null(await actor.Talk(roxxy.ObjectId));
        Assert.Contains($"quest_event:{QuestId}:accept", LastDialog(bench.Rec).Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_talk_needs_an_npc_in_range_and_a_readable_request()
    {
        var bench = new WorldBench(clock: FixedClock);
        BenchPlayer actor = await bench.EnterAsync("range", "Ranger");
        var roxxy = TalkingIsland.Villagers[RoxxyIndex];

        Assert.Equal("range", await actor.Talk(roxxy.ObjectId));
        Assert.Equal("bad_request", await actor.Talk(0));
        Assert.Equal(QuestProtocol.Npc, await actor.Talk(999_999));
        Assert.Equal("bad_request", await actor.QuestEvent(roxxy.ObjectId, 0, "accept"));
        Assert.Equal("bad_request", await actor.AbortQuest(0));

        actor.MoveTo(roxxy.X, roxxy.Y, roxxy.Z);
        Assert.Equal("bad_request", await actor.QuestEvent(roxxy.ObjectId, QuestId, new string('a', 65)));
        Assert.Equal("bad_request", await actor.QuestEvent(roxxy.ObjectId, QuestId, "a b"));

        Assert.True(bench.World.TrySetPlayerDead(actor.Ticket.CharacterId, true));
        Assert.Equal(InventoryProtocol.Dead, await actor.Talk(roxxy.ObjectId));
    }

    [Fact]
    public void Notice_ids_match_the_wire_constants()
    {
        Assert.Equal(QuestProtocol.Acquired, (int)QuestNotice.Acquired);
        Assert.Equal(QuestProtocol.TooMany, (int)QuestNotice.TooMany);
        Assert.Equal(QuestProtocol.Completed, (int)QuestNotice.Completed);
        Assert.Equal(QuestProtocol.Cancelled, (int)QuestNotice.Cancelled);
        Assert.Equal(QuestProtocol.ItemEarned, (int)QuestNotice.ItemEarned);
        Assert.Equal(QuestProtocol.Adena, (int)QuestNotice.Adena);
        Assert.Equal(QuestProtocol.ExpSp, (int)QuestNotice.ExpSp);
        Assert.Equal(QuestProtocol.AlreadyFinished, (int)QuestNotice.AlreadyFinished);
        Assert.Equal(QuestProtocol.NotAbortable, (int)QuestNotice.NotAbortable);
    }

    static DateTimeOffset FixedClock() => DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000);

    static QuestListMsg LastQuestList(NetBridgeRecorder rec) =>
        Assert.IsType<QuestListMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutQuestList)).Last()[1]);

    static SystemMessageMsg LastSystemMessage(NetBridgeRecorder rec) =>
        Assert.IsType<SystemMessageMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Last()[1]);

    static List<int> SystemMessageIds(NetBridgeRecorder rec) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Select(args => ((SystemMessageMsg)args[1]).Id)
            .ToList();

    static InventoryUpdateMsg LastInventory(NetBridgeRecorder rec) =>
        Assert.IsType<InventoryUpdateMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutInventoryUpdate)).Last()[1]);

    /// <summary>The quest dialog HTML the client opens; a Net page arrives as NPC HTML, not the V2 dialog.</summary>
    static NpcHtmlMsg LastDialog(NetBridgeRecorder rec) =>
        Assert.IsType<NpcHtmlMsg>(rec.Of(nameof(IWorldEventPublisher.FanoutNpcHtml)).Last()[1]);
}
