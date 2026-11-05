using System.Net;
using System.Net.Sockets;
using Fineage.Login;
using Fineage.Login.Net;
using Fineage.Protocol;
using MessagePack;
using Xunit;
using NetCreateCharacterMsg = Fineage.Net.Contract.CreateCharacterMsg;
using NetListCharactersMsg = Fineage.Net.Contract.ListCharactersMsg;
using NetDeleteCharacterMsg = Fineage.Net.Contract.DeleteCharacterMsg;
using NetRestoreCharacterMsg = Fineage.Net.Contract.RestoreCharacterMsg;
using NetSelectCharacterMsg = Fineage.Net.Contract.SelectCharacterMsg;
using NetOpcode = Fineage.Net.Contract.NetOpcode;
using NetCodec = Fineage.Net.Contract.NetCodec;

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

        var preview = ClassTemplates.Preview(HumanClasses.Mystic);
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

        var server = new NetLoginServer(service, clock: () => now);
        await using var link = await NetLink.OpenAsync(server);
        await link.AutoAuthAsync("hero", "pass1234");

        await link.SendAsync(NetOpcode.ListCharacters, NetCodec.EncodeListCharacters(new NetListCharactersMsg()));
        var list = NetCodec.DecodeCharacterRoster(await link.ExpectPayloadAsync(NetOpcode.CharacterRoster));
        Assert.Equal(["Aria", "Bron"], list.Characters.Select(c => c.Name).ToArray());
        Assert.Equal(0, list.Characters[0].Slot);
        Assert.Equal(1, list.Characters[1].Slot);

        await link.SendAsync(NetOpcode.DeleteCharacter, NetCodec.EncodeDeleteCharacter(new NetDeleteCharacterMsg { Slot = 0 }));
        var deleted = NetCodec.DecodeDeleteResult(await link.ExpectPayloadAsync(NetOpcode.DeleteResult));
        Assert.True(deleted.Ok);
        Assert.Equal(0, deleted.Slot);

        var afterDelete = NetCodec.DecodeCharacterRoster(await link.ExpectPayloadAsync(NetOpcode.CharacterRoster));
        Assert.Equal(2, afterDelete.Characters.Length);
        Assert.Equal(7 * 24 * 60 * 60, afterDelete.Characters[0].DeleteSecondsLeft);
        Assert.Equal("Aria", afterDelete.Characters[0].Name);

        await link.SendAsync(NetOpcode.SelectCharacter, NetCodec.EncodeSelectCharacter(new NetSelectCharacterMsg { Slot = 0 }));
        var pending = NetCodec.DecodeReject(await link.ExpectPayloadAsync(NetOpcode.Reject));
        Assert.Equal(LobbyProtocol.CharacterPendingDelete, pending.Code);

        await link.SendAsync(NetOpcode.RestoreCharacter, NetCodec.EncodeRestoreCharacter(new NetRestoreCharacterMsg { Slot = 0 }));
        var restoreAck = NetCodec.DecodeDeleteResult(await link.ExpectPayloadAsync(NetOpcode.DeleteResult));
        Assert.True(restoreAck.Ok);
        var restored = NetCodec.DecodeCharacterRoster(await link.ExpectPayloadAsync(NetOpcode.CharacterRoster));
        Assert.Equal(0, restored.Characters[0].DeleteSecondsLeft);

        await link.SendAsync(NetOpcode.SelectCharacter, NetCodec.EncodeSelectCharacter(new NetSelectCharacterMsg { Slot = 1 }));
        var handoff = NetCodec.DecodeHandoff(await link.ExpectPayloadAsync(NetOpcode.Handoff));
        Assert.Equal("Bron", handoff.CharacterName);
        Assert.StartsWith("fa1.", handoff.Token);
    }

    [Fact]
    public void TrySaveVitals_persists_current_hp_and_mp()
    {
        var store = JsonAccountStore.InMemory();
        var service = Service(store);
        Assert.True(service.Register("alice", "secret1").Ok);
        Assert.Equal(CreateCharacterStatus.Ok,
            store.TryAddCharacter("alice", "Aria", HumanClasses.Fighter, 0, 0, out var created));
        Assert.True(store.TrySaveVitals("alice", created!.Id, hp: 90, mp: 12));
        var reloaded = store.FindById("alice", created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(90, reloaded.Hp);
        Assert.Equal(12, reloaded.Mp);
    }

    static LoginService Service(IAccountStore? store = null, Func<DateTimeOffset>? clock = null, int? deleteAfterDays = null) =>
        new(store ?? JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret), clock, deleteAfterDays: deleteAfterDays);

    [MessagePackObject]
    public sealed class OldSelectMsg
    {
        [Key(0)] public int CharacterId { get; set; }
        [Key(1)] public string Name { get; set; } = "";
    }
}
