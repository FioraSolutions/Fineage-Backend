using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Every Interlude Say2 channel with several sessions in the world (Mobius CT_0 chat handlers): who hears the
/// line, what the speaker gets back, block list / message refusal, chat ban, level and hero gates.
/// </summary>
public sealed class NetChatChannelTests
{
    static readonly DateTimeOffset T0 = new(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
    static CancellationToken Ct => CancellationToken.None;

    // Two spots in different map regions (Mobius getMapRegionLocId): the elven village and Gludin.
    const int GludinX = -84000, GludinY = 150000, GludinZ = -3000;

    sealed record Scene(NetTestKit Kit, NetBridgeRecorder Rec);

    static Scene New()
    {
        var kit = new NetTestKit();
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        return new Scene(kit, rec);
    }

    static List<(IReadOnlyList<int> Watchers, CreatureSayMsg Line)> Says(NetBridgeRecorder rec) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutCreatureSay))
            .Select(args => ((IReadOnlyList<int>)args[0]!, (CreatureSayMsg)args[1]!))
            .ToList();

    static List<CreatureSayMsg> Heard(NetBridgeRecorder rec, ZoneEntity who) =>
        Says(rec).Where(s => s.Watchers.Contains(who.ObjectId)).Select(s => s.Line).ToList();

    static List<SystemMessageMsg> LinesTo(NetBridgeRecorder rec, ZoneEntity who) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Where(args => ((IReadOnlyList<int>)args[0]!).Contains(who.ObjectId))
            .Select(args => (SystemMessageMsg)args[1]!)
            .ToList();

    [Fact]
    public async Task All_skips_listeners_who_block_the_speaker_but_the_speaker_still_sees_the_line()
    {
        var (kit, rec) = New();
        var (s, speaker) = await kit.EnterAsync(x: 0, y: 0);
        var (_, near) = await kit.EnterAsync(x: 300, y: 0);
        var (_, blocker) = await kit.EnterAsync(x: 300, y: 50);
        var (_, refusing) = await kit.EnterAsync(x: 300, y: 100);
        kit.World.SocialListsForTests(blocker).Blocked[speaker.ObjectId] = speaker.Name;
        kit.World.SocialListsForTests(refusing).BlockAll = true;
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(s, "hi", ChatProtocol.All, T0, Ct));
        Assert.Single(Heard(rec, speaker));
        Assert.Equal(speaker.Name, Assert.Single(Heard(rec, near)).Name);
        Assert.Empty(Heard(rec, blocker));
        Assert.Empty(Heard(rec, refusing));
    }

    [Fact]
    public async Task Shout_and_trade_reach_the_speakers_map_region_only_and_trade_needs_level_20()
    {
        var regions = Fineage.Game.Services.MapRegionCatalog.LoadDefault();
        Assert.NotEqual(regions.At(NetTestKit.ElfX, NetTestKit.ElfY)?.LocId ?? 0, regions.At(GludinX, GludinY)?.LocId ?? 0);

        var (kit, rec) = New();
        var (s, speaker) = await kit.EnterAsync(level: 20);
        var (_, sameRegionFar) = await kit.EnterAsync(x: NetTestKit.ElfX + 6000, y: NetTestKit.ElfY);
        var (_, otherRegion) = await kit.EnterAsync(x: GludinX, y: GludinY, z: GludinZ);
        var (_, blocker) = await kit.EnterAsync(x: NetTestKit.ElfX + 100, y: NetTestKit.ElfY);
        kit.World.SocialListsForTests(blocker).Blocked[speaker.ObjectId] = speaker.Name;
        Assert.Equal(regions.At(speaker.X, speaker.Y)?.LocId ?? 0, regions.At(sameRegionFar.X, sameRegionFar.Y)?.LocId ?? 0);
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(s, "WTS sword", ChatProtocol.Trade, T0, Ct));
        Assert.Equal(ChatProtocol.Trade, Assert.Single(Heard(rec, sameRegionFar)).ChatType);
        Assert.Single(Heard(rec, speaker));
        Assert.Empty(Heard(rec, otherRegion));
        Assert.Empty(Heard(rec, blocker));

        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "help!", ChatProtocol.Shout, T0.AddSeconds(2), Ct));
        Assert.Equal(ChatProtocol.Shout, Assert.Single(Heard(rec, sameRegionFar)).ChatType);
        Assert.Single(Heard(rec, speaker));
        Assert.Empty(Heard(rec, otherRegion));
        Assert.Empty(Heard(rec, blocker));

        // Mobius ChatShout also drops listeners the speaker blocks; trade does not.
        kit.World.SocialListsForTests(speaker).Blocked[sameRegionFar.ObjectId] = sameRegionFar.Name;
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "again", ChatProtocol.Shout, T0.AddSeconds(4), Ct));
        Assert.Empty(Heard(rec, sameRegionFar));
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "WTB", ChatProtocol.Trade, T0.AddSeconds(6), Ct));
        Assert.Single(Heard(rec, sameRegionFar));

        var (low, lowEntity) = await kit.EnterAsync(level: 19);
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(low, "cheap", ChatProtocol.Trade, T0.AddSeconds(8), Ct));
        Assert.Empty(Says(rec));
        Assert.Equal("Players can use Trade chat after Lv. 20.", Assert.Single(LinesTo(rec, lowEntity)).Text);
    }

    [Fact]
    public async Task Whisper_reaches_one_player_by_name_and_reports_offline_refusal_and_ban()
    {
        var (kit, rec) = New();
        var (s, speaker) = await kit.EnterAsync(x: 0, y: 0);
        var (r, receiver) = await kit.EnterAsync(x: 50_000, y: 0);
        var (_, bystander) = await kit.EnterAsync(x: 10, y: 0);
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(s, "psst", ChatProtocol.Tell, T0, Ct, receiver.Name.ToUpperInvariant()));
        var got = Assert.Single(Heard(rec, receiver));
        Assert.Equal((speaker.ObjectId, speaker.Name, "psst", ChatProtocol.Tell), (got.ObjectId, got.Name, got.Text, got.ChatType));
        Assert.Equal("->" + receiver.Name, Assert.Single(Heard(rec, speaker)).Name);
        Assert.Empty(Heard(rec, bystander));

        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "hello?", ChatProtocol.Tell, T0.AddSeconds(2), Ct, "NoSuchHero"));
        Assert.Empty(Says(rec));
        Assert.Equal(ChatProtocol.PlayerNotOnline, Assert.Single(LinesTo(rec, speaker)).Id);

        kit.World.SocialListsForTests(receiver).Blocked[speaker.ObjectId] = speaker.Name;
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "unblock me", ChatProtocol.Tell, T0.AddSeconds(4), Ct, receiver.Name));
        Assert.Empty(Says(rec));
        Assert.Equal(ChatProtocol.MessageRefusal, Assert.Single(LinesTo(rec, speaker)).Id);

        kit.World.SocialListsForTests(receiver).Blocked.Clear();
        kit.World.SocialListsForTests(receiver).BlockAll = true;
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "still?", ChatProtocol.Tell, T0.AddSeconds(6), Ct, receiver.Name));
        Assert.Equal(ChatProtocol.MessageRefusal, Assert.Single(LinesTo(rec, speaker)).Id);

        // A chat-banned receiver is in "message refusal" for others; a banned speaker cannot whisper at all.
        kit.World.SocialListsForTests(receiver).BlockAll = false;
        kit.World.SetChatBan(r, 60_000);
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "banned?", ChatProtocol.Tell, kit.Now.AddSeconds(8), Ct, receiver.Name));
        Assert.Equal(ChatProtocol.MessageRefusal, Assert.Single(LinesTo(rec, speaker)).Id);

        // The receiver logs out: "not online".
        kit.World.SetChatBan(r, 0);
        await kit.World.LeaveAsync(r, Ct);
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "gone?", ChatProtocol.Tell, kit.Now.AddSeconds(10), Ct, receiver.Name));
        Assert.Equal(ChatProtocol.PlayerNotOnline, Assert.Single(LinesTo(rec, speaker)).Id);
    }

    [Fact]
    public async Task Party_and_clan_lines_reach_online_members_anywhere_minus_blockers()
    {
        var (kit, rec) = New();
        var (s, speaker) = await kit.EnterAsync(x: 0, y: 0, level: 40);
        var (far, mateFar) = await kit.EnterAsync(x: 60_000, y: 0, level: 20);
        var (_, mateBlocking) = await kit.EnterAsync(x: 10, y: 0);
        var (_, stranger) = await kit.EnterAsync(x: 20, y: 0);
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(s, "inv me", ChatProtocol.Party, T0, Ct));
        Assert.Empty(Says(rec));
        Assert.Equal("You are not in a party.", Assert.Single(LinesTo(rec, speaker)).Text);
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "clan?", ChatProtocol.Clan, T0.AddSeconds(2), Ct));
        Assert.Equal("You are not in a clan.", Assert.Single(LinesTo(rec, speaker)).Text);

        kit.World.JoinPartyForTest(speaker, mateFar, mateBlocking);
        kit.World.SocialListsForTests(mateBlocking).Blocked[speaker.ObjectId] = speaker.Name;
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "pull", ChatProtocol.Party, T0.AddSeconds(4), Ct));
        Assert.Equal(ChatProtocol.Party, Assert.Single(Heard(rec, mateFar)).ChatType);
        Assert.Single(Heard(rec, speaker));
        Assert.Empty(Heard(rec, mateBlocking));
        Assert.Empty(Heard(rec, stranger));

        Assert.Null(await kit.World.TryClanCreateAsync(s, new ClanCreateRequestMsg { Name = "Talkers" }, kit.Now, Ct));
        Assert.Null(await kit.World.TryClanInviteAsync(s, new ClanInviteRequestMsg { TargetObjectId = mateFar.ObjectId }, kit.Now, Ct));
        Assert.Null(await kit.World.TryClanAnswerAsync(far,
            new ClanAnswerJoinRequestMsg { RequestorObjectId = speaker.ObjectId, Response = 1 }, kit.Now, Ct));
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "clan meet", ChatProtocol.Clan, T0.AddSeconds(6), Ct));
        Assert.Equal(ChatProtocol.Clan, Assert.Single(Heard(rec, mateFar)).ChatType);
        Assert.Single(Heard(rec, speaker));
        Assert.Empty(Heard(rec, stranger));
        Assert.Empty(Heard(rec, mateBlocking));

        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "ally", ChatProtocol.Alliance, T0.AddSeconds(8), Ct));
        Assert.Empty(Says(rec));
        Assert.Equal("You are not in an alliance.", Assert.Single(LinesTo(rec, speaker)).Text);
    }

    [Fact]
    public async Task Hero_voice_is_for_heroes_reaches_everyone_and_allows_one_line_per_10_seconds()
    {
        var (kit, rec) = New();
        var (s, speaker) = await kit.EnterAsync(x: 0, y: 0);
        var (_, far) = await kit.EnterAsync(x: GludinX, y: GludinY, z: GludinZ);
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(s, "glory", ChatProtocol.Hero, T0, Ct));
        Assert.Empty(Says(rec));
        Assert.Equal("Only Heroes can enter the Hero channel.", Assert.Single(LinesTo(rec, speaker)).Text);

        speaker.Hero = true;
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "glory", ChatProtocol.Hero, T0.AddSeconds(2), Ct));
        Assert.Equal(ChatProtocol.Hero, Assert.Single(Heard(rec, far)).ChatType);
        Assert.Single(Heard(rec, speaker));

        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "again", ChatProtocol.Hero, T0.AddSeconds(5), Ct));
        Assert.Empty(Says(rec));
        Assert.StartsWith("Action failed. Heroes", Assert.Single(LinesTo(rec, speaker)).Text);

        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "now", ChatProtocol.Hero, T0.AddSeconds(12), Ct));
        Assert.Single(Heard(rec, far));
    }

    [Fact]
    public async Task Chat_ban_closes_general_shout_trade_hero_and_whisper_but_not_party()
    {
        var (kit, rec) = New();
        var (s, speaker) = await kit.EnterAsync(level: 20);
        var (_, mate) = await kit.EnterAsync(x: NetTestKit.ElfX + 50, y: NetTestKit.ElfY);
        speaker.Hero = true;
        kit.World.JoinPartyForTest(speaker, mate);
        kit.World.SetChatBan(s, 3_600_000);
        DateTimeOffset now = kit.Now;
        foreach (int type in new[] { ChatProtocol.All, ChatProtocol.Shout, ChatProtocol.Trade, ChatProtocol.Hero, ChatProtocol.Tell })
        {
            rec.Calls.Clear();
            now = now.AddSeconds(11);
            Assert.Null(await kit.World.TrySayAsync(s, "spam", type, now, Ct, mate.Name));
            Assert.Empty(Says(rec));
            Assert.Equal(ChatProtocol.ChattingProhibited, Assert.Single(LinesTo(rec, speaker)).Id);
        }

        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "sorry", ChatProtocol.Party, now.AddSeconds(2), Ct));
        Assert.Single(Heard(rec, mate));

        kit.World.SetChatBan(s, 0);
        rec.Calls.Clear();
        Assert.Null(await kit.World.TrySayAsync(s, "free", ChatProtocol.All, now.AddSeconds(4), Ct));
        Assert.Single(Heard(rec, mate));
    }

    [Fact]
    public async Task Petition_without_a_consultation_and_party_rooms_without_a_room()
    {
        var (kit, rec) = New();
        var (s, speaker) = await kit.EnterAsync();
        rec.Calls.Clear();

        Assert.Null(await kit.World.TrySayAsync(s, "help gm", ChatProtocol.PetitionPlayer, T0, Ct));
        Assert.Equal(ChatProtocol.NotInPetitionChat, Assert.Single(LinesTo(rec, speaker)).Id);

        DateTimeOffset now = T0;
        foreach (int type in new[] { ChatProtocol.PartyMatchRoom, ChatProtocol.PartyRoomCommander, ChatProtocol.PartyRoomAll })
        {
            rec.Calls.Clear();
            now = now.AddSeconds(2);
            Assert.Null(await kit.World.TrySayAsync(s, "room", type, now, Ct));
            Assert.Empty(Says(rec));
            Assert.Empty(LinesTo(rec, speaker)); // Mobius returns silently
        }
    }

    [Fact]
    public async Task Announcements_reach_every_online_player_as_type_10_or_18_without_a_speaker()
    {
        var (kit, rec) = New();
        var (_, a) = await kit.EnterAsync(x: 0, y: 0);
        var (_, b) = await kit.EnterAsync(x: GludinX, y: GludinY, z: GludinZ);
        rec.Calls.Clear();

        kit.World.AnnounceToAll("Server restart in 5 minutes.");
        kit.World.AnnounceToAll("Siege begins!", critical: true);
        foreach (var who in new[] { a, b })
        {
            var heard = Heard(rec, who);
            Assert.Equal(2, heard.Count);
            Assert.Equal((0, "", ChatProtocol.Announcement), (heard[0].ObjectId, heard[0].Name, heard[0].ChatType));
            Assert.Equal(ChatProtocol.CriticalAnnounce, heard[1].ChatType);
        }
    }
}
