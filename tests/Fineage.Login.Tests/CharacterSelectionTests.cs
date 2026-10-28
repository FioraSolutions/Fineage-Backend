using System.Net;
using System.Net.Sockets;
using Fineage.Protocol;
using MessagePack;
using Xunit;

namespace Fineage.Login.Tests;

public class CharacterSelectionTests
{
    [Fact]
    public void Roster_is_creation_order_with_slot_preview_position_and_appearance()
    {
        var now = DateTimeOffset.UnixEpoch;
        var store = JsonAccountStore.InMemory();
        var service = Service(store, () => now);
        Assert.True(service.Register("alice", "secret1").Ok);
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
            "alice", "Later", HumanClasses.Fighter, 0, 0, out _, createdAt: now.AddMinutes(5)));
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
            "alice", "Earlier", HumanClasses.Mystic, 0, 0, out var earlier, createdAt: now.AddMinutes(1)));

        var roster = service.List("alice");
        Assert.True(roster.Ok, roster.Message);
        Assert.Equal(["Earlier", "Later"], roster.Value!.Select(c => c.Name).ToArray());
        Assert.Equal(0, earlier!.HairStyle);
        Assert.Equal(0, earlier.HairColor);
        Assert.Equal(0, earlier.Face);
        Assert.False(earlier.HasPosition);
        Assert.Equal(1, earlier.Level);

        var preview = ClassSpawn.Preview(HumanClasses.Mystic);
        var rows = LobbyWire.ToSummaries(roster.Value!, now);
        Assert.Equal(0, rows[0].Slot);
        Assert.Equal(1, rows[1].Slot);
        Assert.Equal(preview.X, rows[0].X);
        Assert.Equal(preview.Y, rows[0].Y);
        Assert.Equal(preview.Z, rows[0].Z);
        Assert.Equal(0, rows[0].DeleteSecondsLeft);
        Assert.False(rows[0].IsLastPlayed);
        Assert.False(rows[1].IsLastPlayed);
        Assert.Equal(0, LoginService.LastPlayedId(roster.Value!));
    }

    [Fact]
    public void Saved_position_replaces_the_creation_point_on_the_roster()
    {
        var now = DateTimeOffset.UnixEpoch;
        var store = JsonAccountStore.InMemory();
        var service = Service(store, () => now);
        var created = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter).Value!;
        Assert.True(store.TrySavePresence("alice", created.CharacterId, -70000, 250000, -3000, now.AddMinutes(1)));

        var roster = service.List("alice").Value!;
        var row = Assert.Single(LobbyWire.ToSummaries(roster, now.AddMinutes(1)));
        Assert.Equal(-70000, row.X);
        Assert.Equal(250000, row.Y);
        Assert.Equal(-3000, row.Z);
        Assert.True(row.IsLastPlayed);
    }

    [Fact]
    public void Select_by_slot_or_id_rejects_foreign_unknown_and_pending()
    {
        var now = DateTimeOffset.UnixEpoch;
        var store = JsonAccountStore.InMemory();
        var service = Service(store, () => now, deleteAfterDays: 7);
        Assert.True(service.Register("alice", "secret1").Ok);
        Assert.True(service.Register("bob", "secret1").Ok);
        Assert.True(service.CreateHuman("alice", "Aria", HumanClasses.Fighter).Ok);
        now = now.AddSeconds(1);
        Assert.True(service.CreateHuman("alice", "Bron", HumanClasses.Mystic).Ok);

        var bySlot = service.Select("alice", characterId: 0, slot: 1);
        Assert.True(bySlot.Ok, bySlot.Message);
        Assert.Equal("Bron", bySlot.Value!.CharacterName);

        var roster = service.List("alice").Value!;
        Assert.Equal(roster[1].Id, LoginService.LastPlayedId(roster));
        Assert.Equal(now, roster[1].LastAccess);
        Assert.Equal(default, roster[0].LastAccess);

        var aria = roster[0];
        Assert.Equal(LobbyProtocol.CharacterNotFound, service.Select("bob", aria.Id).Code);
        Assert.Equal(LobbyProtocol.CharacterNotFound, service.Select("alice", 9999).Code);
        Assert.Equal(LobbyProtocol.CharacterNotFound, service.Select("alice", 0, slot: 9).Code);
        Assert.Equal("bad_request", service.Select("alice", 0).Code);

        Assert.True(service.Delete("alice", characterId: aria.Id).Ok);
        var pending = service.Select("alice", aria.Id);
        Assert.Equal(LobbyProtocol.CharacterPendingDelete, pending.Code);
        Assert.Equal(default, service.List("alice").Value!.First(c => c.Id == aria.Id).LastAccess);
    }

    [Fact]
    public void Delete_sets_a_countdown_and_restore_clears_it()
    {
        var now = DateTimeOffset.UnixEpoch;
        var service = Service(null, () => now, deleteAfterDays: 7);
        Assert.True(service.Register("alice", "secret1").Ok);
        var aria = service.CreateHuman("alice", "Aria", HumanClasses.Fighter).Value!;

        var deleted = service.Delete("alice", slot: 0);
        Assert.True(deleted.Ok, deleted.Message);
        Assert.False(deleted.Value!.HardDeleted);
        Assert.Equal(0, deleted.Value.Slot);
        Assert.Equal(aria.Id, deleted.Value.CharacterId);
        var pending = Assert.Single(deleted.Value.Roster);
        Assert.Equal(7 * 24 * 60 * 60, pending.DeleteSecondsLeft(now));
        Assert.True(pending.IsPendingDelete);

        var rows = LobbyWire.ToSummaries(deleted.Value.Roster, now);
        Assert.Equal(7 * 24 * 60 * 60, rows[0].DeleteSecondsLeft);

        now = now.AddSeconds(90);
        Assert.Equal(7 * 24 * 60 * 60 - 90, service.List("alice").Value![0].DeleteSecondsLeft(now));

        var restored = service.Restore("alice", slot: 0);
        Assert.True(restored.Ok, restored.Message);
        var back = Assert.Single(restored.Value!);
        Assert.False(back.IsPendingDelete);
        Assert.Equal(0, back.DeleteSecondsLeft(now));
        Assert.True(service.Select("alice", back.Id).Ok);
    }

    [Fact]
    public void Delete_at_zero_days_removes_the_character_and_items_and_frees_the_name()
    {
        var store = JsonAccountStore.InMemory();
        var service = Service(store, deleteAfterDays: 0);
        var created = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter).Value!;
        Assert.True(store.TryReplaceItems("alice", created.CharacterId, [new StoredItem(57, 40, -1)]));

        var deleted = service.Delete("alice", characterId: created.CharacterId);
        Assert.True(deleted.Ok, deleted.Message);
        Assert.True(deleted.Value!.HardDeleted);
        Assert.Empty(deleted.Value.Roster);
        Assert.Null(store.FindById("alice", created.CharacterId));

        var again = service.CreateHuman("alice", "Aria", HumanClasses.Mystic);
        Assert.True(again.Ok, again.Message);
        Assert.NotEqual(created.CharacterId, again.Value!.Id);
        Assert.DoesNotContain(again.Value.Items!, item => item.ItemId == 57);
    }

    [Fact]
    public void Expired_characters_are_purged_on_roster_load_and_the_name_is_free()
    {
        var now = DateTimeOffset.UnixEpoch;
        var store = JsonAccountStore.InMemory();
        var service = Service(store, () => now, deleteAfterDays: 7);
        var created = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter).Value!;
        Assert.True(store.TryReplaceItems("alice", created.CharacterId, [new StoredItem(57, 40, -1)]));
        Assert.True(service.Delete("alice", characterId: created.CharacterId).Ok);

        now = now.AddDays(7);
        var still = service.List("alice").Value!;
        Assert.Single(still);
        Assert.Equal(0, still[0].DeleteSecondsLeft(now));
        Assert.Equal(LobbyProtocol.CharacterPendingDelete, service.Select("alice", created.CharacterId).Code);

        now = now.AddMilliseconds(1);
        Assert.Empty(service.List("alice").Value!);
        Assert.Null(store.FindById("alice", created.CharacterId));
        Assert.Equal(LobbyProtocol.CharacterNotFound, service.Select("alice", created.CharacterId).Code);

        var again = service.CreateHuman("alice", "aria", HumanClasses.Fighter);
        Assert.True(again.Ok, again.Message);
        Assert.NotNull(again.Value!.Items);
        Assert.DoesNotContain(again.Value.Items, item => item.ItemId == 57);
    }

    [Fact]
    public void Pending_delete_still_counts_toward_the_character_cap()
    {
        var service = Service(deleteAfterDays: 7);
        Assert.True(service.Register("alice", "secret1").Ok);
        for (int i = 0; i < 7; i++)
            Assert.True(service.CreateHuman("alice", "Alt" + i, HumanClasses.Fighter).Ok);
        Assert.True(service.Delete("alice", slot: 0).Ok);
        Assert.Equal("character_limit", service.CreateHuman("alice", "Extra", HumanClasses.Mystic).Code);
    }

    [Fact]
    public void Unknown_delete_is_an_error_and_unknown_restore_is_not_found()
    {
        var service = Service(deleteAfterDays: 7);
        Assert.True(service.Register("alice", "secret1").Ok);
        Assert.True(service.CreateHuman("alice", "Aria", HumanClasses.Fighter).Ok);

        Assert.Equal(LobbyProtocol.CharacterNotFound, service.Delete("alice", slot: 4).Code);
        Assert.Equal(LobbyProtocol.CharacterNotFound, service.Restore("alice", slot: 4).Code);
        Assert.False(service.List("alice").Value![0].IsPendingDelete);
    }

    [Fact]
    public void Delete_fields_roundtrip_through_the_account_file()
    {
        var now = DateTimeOffset.UnixEpoch.AddHours(3);
        string path = Path.Combine(Path.GetTempPath(), "fineage-select-" + Guid.NewGuid().ToString("n") + ".json");
        try
        {
            var store = JsonAccountStore.Open(path);
            var service = Service(store, () => now, deleteAfterDays: 7);
            var created = service.Play("alice", "secret1", "Aria", HumanClasses.Fighter).Value!;
            Assert.True(service.Delete("alice", characterId: created.CharacterId).Ok);
            Assert.True(store.TrySavePresence("alice", created.CharacterId, 1, 2, 3, now));

            var reloaded = JsonAccountStore.Open(path).FindById("alice", created.CharacterId);
            Assert.NotNull(reloaded);
            Assert.Equal(now.AddDays(7), reloaded.DeleteAt);
            Assert.Equal(now, reloaded.LastAccess);
            Assert.Equal(now, reloaded.CreatedAt);
            Assert.True(reloaded.HasPosition);
            Assert.Equal(1, reloaded.X);
            Assert.Equal(2, reloaded.Y);
            Assert.Equal(3, reloaded.Z);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Old_select_payload_keeps_slot_unspecified()
    {
        byte[] bytes = MessagePackSerializer.Serialize(new OldSelectMsg { CharacterId = 4, Name = "Aria" });
        var msg = MessagePackSerializer.Deserialize<SelectCharacterMsg>(bytes);
        Assert.Equal(4, msg.CharacterId);
        Assert.Equal("Aria", msg.Name);
        Assert.Equal(-1, msg.Slot);
    }

    [Fact]
    public async Task Wire_delete_restore_and_select_follow_the_lobby_set()
    {
        var now = DateTimeOffset.UnixEpoch;
        var service = Service(null, () => now, deleteAfterDays: 7);
        Assert.True(service.Register("hero", "pass1234").Ok);
        Assert.True(service.CreateHuman("hero", "Aria", HumanClasses.Fighter).Ok);
        now = now.AddSeconds(1);
        Assert.True(service.CreateHuman("hero", "Bron", HumanClasses.Mystic).Ok);

        var server = new LoginServer(service, new LoginServerOptions { SelectMinInterval = TimeSpan.Zero }, () => now);
        await using var link = await LobbyLink.Open(server);

        await link.SendAsync(MsgType.Auth, new AuthMsg { Account = "hero", Password = "pass1234" });
        await link.ExpectAsync(MsgType.LoginOk);

        await link.SendAsync(MsgType.ListCharacters, Array.Empty<byte>());
        var list = await link.ExpectAsync<CharacterListMsg>(MsgType.CharacterList);
        Assert.Equal(["Aria", "Bron"], list.Characters.Select(c => c.Name).ToArray());
        Assert.Equal(0, list.Characters[0].Slot);
        Assert.Equal(1, list.Characters[1].Slot);
        Assert.Equal(ClassSpawn.Preview(HumanClasses.Fighter).X, list.Characters[0].X);
        Assert.False(list.Characters[0].IsLastPlayed);

        await link.SendAsync(MsgType.DeleteCharacter, new DeleteCharacterMsg { Slot = 0 });
        var deleted = await link.ExpectAsync<CharacterDeletedMsg>(MsgType.CharacterDeleted);
        Assert.False(deleted.HardDeleted);
        Assert.Equal(0, deleted.Slot);
        var afterDelete = await link.ExpectAsync<CharacterListMsg>(MsgType.CharacterList);
        Assert.Equal(2, afterDelete.Characters.Length);
        Assert.Equal(7 * 24 * 60 * 60, afterDelete.Characters[0].DeleteSecondsLeft);
        Assert.Equal("Aria", afterDelete.Characters[0].Name);

        await link.SendAsync(MsgType.SelectCharacter, new SelectCharacterMsg { Slot = 0 });
        var pending = await link.ExpectAsync<ErrorMsg>(MsgType.Error);
        Assert.Equal(LobbyProtocol.CharacterPendingDelete, pending.Code);

        await link.SendAsync(MsgType.RestoreCharacter, new RestoreCharacterMsg { Slot = 0 });
        var restored = await link.ExpectAsync<CharacterListMsg>(MsgType.CharacterList);
        Assert.Equal(0, restored.Characters[0].DeleteSecondsLeft);

        await link.SendAsync(MsgType.RestoreCharacter, new RestoreCharacterMsg { Slot = 9 });
        await link.ExpectSilenceAsync();

        await link.SendAsync(MsgType.DeleteCharacter, new DeleteCharacterMsg { Slot = 9 });
        var failed = await link.ExpectAsync<CharacterDeleteFailedMsg>(MsgType.CharacterDeleteFailed);
        Assert.Equal(LobbyProtocol.CharacterNotFound, failed.Code);

        await link.SendAsync(MsgType.SelectCharacter, new SelectCharacterMsg { Slot = 1 });
        var handoff = await link.ExpectAsync<HandoffMsg>(MsgType.Handoff);
        Assert.Equal("Bron", handoff.CharacterName);
        Assert.StartsWith("fa1.", handoff.Token);

        await link.SendAsync(MsgType.SelectCharacter, new SelectCharacterMsg { Slot = 1 });
        await link.ExpectSilenceAsync();
    }

    [Fact]
    public async Task Select_is_flood_gated_until_the_interval_elapses()
    {
        var now = DateTimeOffset.UnixEpoch;
        var service = Service(null, () => now);
        Assert.True(service.Register("hero", "pass1234").Ok);
        Assert.True(service.CreateHuman("hero", "Aria", HumanClasses.Fighter).Ok);
        var server = new LoginServer(service, new LoginServerOptions { SelectMinInterval = TimeSpan.FromSeconds(1) }, () => now);
        await using var link = await LobbyLink.Open(server);

        await link.SendAsync(MsgType.Auth, new AuthMsg { Account = "hero", Password = "pass1234" });
        await link.ExpectAsync(MsgType.LoginOk);
        await link.SendAsync(MsgType.SelectCharacter, new SelectCharacterMsg { CharacterId = 9999 });
        var missing = await link.ExpectAsync<ErrorMsg>(MsgType.Error);
        Assert.Equal(LobbyProtocol.CharacterNotFound, missing.Code);

        await link.SendAsync(MsgType.SelectCharacter, new SelectCharacterMsg { Name = "Aria" });
        await link.ExpectSilenceAsync();

        now = now.AddSeconds(1);
        await link.SendAsync(MsgType.SelectCharacter, new SelectCharacterMsg { Name = "Aria" });
        var handoff = await link.ExpectAsync<HandoffMsg>(MsgType.Handoff);
        Assert.Equal("Aria", handoff.CharacterName);
    }

    static LoginService Service(IAccountStore? store = null, Func<DateTimeOffset>? clock = null, int? deleteAfterDays = null) =>
        new(store ?? JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret), clock, deleteAfterDays: deleteAfterDays);

    [MessagePackObject]
    public sealed class OldSelectMsg
    {
        [Key(0)] public int CharacterId { get; set; }
        [Key(1)] public string Name { get; set; } = "";
    }

    sealed class LobbyLink : IAsyncDisposable
    {
        readonly TcpListener _listener;
        readonly CancellationTokenSource _cts;
        readonly Task _accept;
        readonly TcpClient _client;
        readonly NetworkStream _stream;

        LobbyLink(TcpListener listener, CancellationTokenSource cts, Task accept, TcpClient client, NetworkStream stream)
        {
            _listener = listener;
            _cts = cts;
            _accept = accept;
            _client = client;
            _stream = stream;
        }

        public static async Task<LobbyLink> Open(LoginServer server)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var cts = new CancellationTokenSource();
            var accept = Task.Run(async () =>
            {
                using var accepted = await listener.AcceptTcpClientAsync(cts.Token);
                await using var stream = accepted.GetStream();
                await server.HandleAsync(stream, cts.Token);
            });

            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port);
            return new LobbyLink(listener, cts, accept, client, client.GetStream());
        }

        public Task SendAsync(MsgType type, byte[] body) => FrameIO.SendAsync(_stream, (ushort)type, body);

        public Task SendAsync<T>(MsgType type, T message) =>
            FrameIO.SendAsync(_stream, (ushort)type, MessagePackSerializer.Serialize(message));

        public async Task ExpectAsync(MsgType expected)
        {
            var frame = await ReadAsync();
            Assert.Equal((ushort)expected, frame.Type);
        }

        public async Task<T> ExpectAsync<T>(MsgType expected)
        {
            var frame = await ReadAsync();
            Assert.Equal((ushort)expected, frame.Type);
            return MessagePackSerializer.Deserialize<T>(frame.Body);
        }

        public async Task ExpectSilenceAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => FrameIO.ReadAsync(_stream, timeout.Token));
        }

        async Task<(ushort Type, byte[] Body)> ReadAsync()
        {
            var frame = await FrameIO.ReadAsync(_stream);
            Assert.NotNull(frame);
            return (frame!.Value.Type, frame.Value.Body);
        }

        public async ValueTask DisposeAsync()
        {
            _client.Dispose();
            _listener.Stop();
            await _cts.CancelAsync();
            try
            {
                await _accept.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
            {
            }

            _cts.Dispose();
        }
    }
}
