using System.Net;
using System.Net.Sockets;
using Fineage.Game.Quests;
using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.World;
using MessagePack;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneQuestWireTests
{
    [Fact]
    public async Task Smoke_quest_talks_reward_and_persist()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var store = JsonAccountStore.InMemory();
        var server = new ZoneServer(tokens, new ZoneWorld(accounts: store));
        var login = new LoginService(store, tokens);
        var ticket = login.Play("quester", "pass1234", "Quester", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        await using var link = await Link.Open(server);
        await link.EnterAsync(ticket.Value!);

        await link.SendAsync(MsgType.NpcTalk, new NpcTalkMsg { NpcObjectId = TalkingIsland.Villagers[0].ObjectId });
        var far = await link.ReadAsync<ErrorMsg>(MsgType.Error);
        Assert.Equal("range", far.Code);

        var roxxy = TalkingIsland.Villagers[0];
        var bitz = TalkingIsland.Villagers[1];
        await link.SendAsync(MsgType.MoveTo, new MoveToMsg { DestX = roxxy.X, DestY = roxxy.Y, DestZ = roxxy.Z });
        await link.ReadAsync<MoveToMsg>(MsgType.MoveToAck);

        await link.SendAsync(MsgType.NpcTalk, new NpcTalkMsg { NpcObjectId = roxxy.ObjectId });
        var offer = await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        Assert.Equal("first_talk", offer.Hook);
        Assert.Equal(999, offer.QuestId);
        Assert.Contains("quest_event:accept", offer.Html, StringComparison.Ordinal);
        var created = await link.ReadAsync<QuestListMsg>(MsgType.QuestList);
        Assert.Empty(created.Active);

        await link.SendAsync(MsgType.QuestEvent, new QuestEventMsg { NpcObjectId = roxxy.ObjectId, QuestId = 999, Event = "accept" });
        var acquired = await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(QuestProtocol.Acquired, acquired.Id);
        Assert.Equal(QuestSounds.Accept, (await link.ReadAsync<QuestSoundMsg>(MsgType.QuestSound)).Name);
        await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        var started = await link.ReadAsync<QuestListMsg>(MsgType.QuestList);
        var active = Assert.Single(started.Active);
        Assert.Equal(999, active.QuestId);
        Assert.Equal(1, active.Cond);
        Assert.Equal(1, active.JournalWord);
        Assert.Equal(QuestProtocol.Started, active.State);

        await link.SendAsync(MsgType.NpcTalk, new NpcTalkMsg { NpcObjectId = bitz.ObjectId });
        var bitzPage = await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        Assert.Contains("quest_event:heard", bitzPage.Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.QuestEvent, new QuestEventMsg { NpcObjectId = bitz.ObjectId, QuestId = 999, Event = "heard" });
        Assert.Equal(QuestSounds.Middle, (await link.ReadAsync<QuestSoundMsg>(MsgType.QuestSound)).Name);
        await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        var progressed = await link.ReadAsync<QuestListMsg>(MsgType.QuestList);
        Assert.Equal(2, Assert.Single(progressed.Active).Cond);

        await link.SendAsync(MsgType.NpcTalk, new NpcTalkMsg { NpcObjectId = roxxy.ObjectId });
        var rewardPage = await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        Assert.Contains("quest_event:reward", rewardPage.Html, StringComparison.Ordinal);

        await link.SendAsync(MsgType.QuestEvent, new QuestEventMsg { NpcObjectId = roxxy.ObjectId, QuestId = 999, Event = "reward" });
        var update = await link.ReadAsync<InventoryUpdateMsg>(MsgType.InventoryUpdate);
        Assert.Equal(2, update.Changes.Length);
        Assert.Contains(update.Changes, row => row.ItemId == 57 && row.Count == 25 && row.Change == InventoryChange.Add);
        Assert.Contains(update.Changes, row => row.ItemId == 736 && row.Count == 1 && row.Change == InventoryChange.Add);
        Assert.Equal(QuestProtocol.ItemEarned, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(QuestProtocol.Adena, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(QuestProtocol.ExpSp, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(QuestProtocol.Completed, (await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage)).Id);
        Assert.Equal(QuestSounds.ItemGet, (await link.ReadAsync<QuestSoundMsg>(MsgType.QuestSound)).Name);
        Assert.Equal(QuestSounds.Finish, (await link.ReadAsync<QuestSoundMsg>(MsgType.QuestSound)).Name);
        await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        var finished = await link.ReadAsync<QuestListMsg>(MsgType.QuestList);
        Assert.Empty(finished.Active);
        Assert.Equal(999, Assert.Single(finished.CompletedQuestIds));
        Assert.Equal(128, finished.CompletedMask[999 / 8]);
        Assert.Equal(QuestJournal.CompletedMaskLength, finished.CompletedMask.Length);
        Assert.Equal(25, finished.MaxActive);

        await link.CloseAsync();

        var saved = store.FindById(ticket.Value.Account, ticket.Value.CharacterId);
        Assert.NotNull(saved);
        Assert.Equal(50, saved!.Exp);
        Assert.Equal(5, saved.Sp);
        Assert.Equal(1, saved.Level);
        var quest = Assert.Single(saved.Quests!);
        Assert.Equal(999, quest.QuestId);
        Assert.Equal(QuestProtocol.CompletedState, quest.State);
        Assert.Contains(saved.Items!, item => item.ItemId == 736 && item.Count == 1);
        Assert.Contains(saved.Items!, item => item.ItemId == 57 && item.Count == 25);

        await using var again = await Link.Open(server);
        await again.EnterAsync(ticket.Value);
        await again.SendAsync(MsgType.RequestQuestList, new RequestQuestListMsg());
        var reloaded = await again.ReadAsync<QuestListMsg>(MsgType.QuestList);
        Assert.Equal(999, Assert.Single(reloaded.CompletedQuestIds));
        Assert.Equal(128, reloaded.CompletedMask[999 / 8]);
        await again.CloseAsync();
    }

    [Fact]
    public async Task Abort_removes_a_started_quest_so_it_can_be_accepted_again()
    {
        var tokens = new HandoffTokens(HandoffTokens.DefaultSecret);
        var store = JsonAccountStore.InMemory();
        var server = new ZoneServer(tokens, new ZoneWorld(accounts: store));
        var login = new LoginService(store, tokens);
        var ticket = login.Play("aborter", "pass1234", "Aborter", HumanClasses.Fighter);
        Assert.True(ticket.Ok && ticket.Value != null);

        await using var link = await Link.Open(server);
        await link.EnterAsync(ticket.Value!);
        var roxxy = TalkingIsland.Villagers[0];
        await link.SendAsync(MsgType.MoveTo, new MoveToMsg { DestX = roxxy.X, DestY = roxxy.Y, DestZ = roxxy.Z });
        await link.ReadAsync<MoveToMsg>(MsgType.MoveToAck);
        await link.SendAsync(MsgType.NpcTalk, new NpcTalkMsg { NpcObjectId = roxxy.ObjectId });
        await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        await link.ReadAsync<QuestListMsg>(MsgType.QuestList);
        await link.SendAsync(MsgType.QuestEvent, new QuestEventMsg { NpcObjectId = roxxy.ObjectId, QuestId = 999, Event = "accept" });
        await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage);
        await link.ReadAsync<QuestSoundMsg>(MsgType.QuestSound);
        await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        await link.ReadAsync<QuestListMsg>(MsgType.QuestList);

        await link.SendAsync(MsgType.RequestQuestAbort, new RequestQuestAbortMsg { QuestId = 999 });
        var cancelled = await link.ReadAsync<SystemMessageMsg>(MsgType.SystemMessage);
        Assert.Equal(QuestProtocol.Cancelled, cancelled.Id);
        Assert.Equal(QuestSounds.GiveUp, (await link.ReadAsync<QuestSoundMsg>(MsgType.QuestSound)).Name);
        Assert.Empty((await link.ReadAsync<QuestListMsg>(MsgType.QuestList)).Active);
        Assert.Empty(store.FindById(ticket.Value.Account, ticket.Value.CharacterId)!.Quests!);

        await link.SendAsync(MsgType.NpcTalk, new NpcTalkMsg { NpcObjectId = roxxy.ObjectId });
        var offer = await link.ReadAsync<QuestDialogMsg>(MsgType.QuestDialog);
        Assert.Contains("quest_event:accept", offer.Html, StringComparison.Ordinal);
        await link.ReadAsync<QuestListMsg>(MsgType.QuestList);
        await link.CloseAsync();
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

    sealed class Link : IAsyncDisposable
    {
        readonly TcpClient _client;
        readonly NetworkStream _stream;
        readonly CancellationTokenSource _cts;
        readonly Task _server;

        Link(TcpClient client, NetworkStream stream, CancellationTokenSource cts, Task server)
        {
            _client = client;
            _stream = stream;
            _cts = cts;
            _server = server;
        }

        public static async Task<Link> Open(ZoneServer server)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var cts = new CancellationTokenSource();
            var accept = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
                listener.Stop();
                await using var stream = accepted.GetStream();
                await server.HandleAsync(stream, cts.Token);
            });
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new Link(client, client.GetStream(), cts, accept);
        }

        public async Task EnterAsync(HandoffTicket ticket)
        {
            await SendAsync(MsgType.Hello, new HelloMsg());
            await ReadAsync<HelloAckMsg>(MsgType.HelloAck);
            await SendAsync(MsgType.Auth, ticket.ToZoneAuth());
            await ReadAsync<AuthOkMsg>(MsgType.AuthOk);
            await SendAsync(MsgType.EnterWorld, new EnterWorldMsg());
            await ReadAsync<UserInfoMsg>(MsgType.UserInfo);
            await ReadAsync<ItemListMsg>(MsgType.ItemList);
            await ReadAsync<SkillListMsg>(MsgType.SkillList);
            await ReadAsync<PlayerSpawnedMsg>(MsgType.PlayerSpawned);
            for (int i = 0; i < TalkingIsland.NpcCount; i++)
                await ReadAsync<NpcInfoMsg>(MsgType.NpcInfo);
            var welcome = await ReadAsync<SystemMessageMsg>(MsgType.SystemMessage);
            Assert.Equal(ChatProtocol.Welcome, welcome.Id);
        }

        public async Task SendAsync<T>(MsgType type, T message) =>
            await FrameIO.SendAsync(_stream, (ushort)type, MessagePackSerializer.Serialize(message));

        public async Task<T> ReadAsync<T>(MsgType type)
        {
            var frame = await FrameIO.ReadAsync(_stream);
            Assert.NotNull(frame);
            Assert.Equal((ushort)type, frame!.Value.Type);
            return MessagePackSerializer.Deserialize<T>(frame.Value.Body);
        }

        public async Task CloseAsync()
        {
            _client.Close();
            await _server.WaitAsync(TimeSpan.FromSeconds(5));
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            await _cts.CancelAsync();
            try
            {
                await _server.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}
