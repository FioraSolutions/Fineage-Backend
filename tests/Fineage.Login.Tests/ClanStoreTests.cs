using Fineage.Login;
using Fineage.Protocol;
using Xunit;

namespace Fineage.Login.Tests;

public class ClanStoreTests
{
    [Fact]
    public void Clans_round_trip_through_accounts_json_and_block_deletion()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fineage-clans-{Guid.NewGuid():N}.json");
        try
        {
            var store = JsonAccountStore.Open(path);
            var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
            Assert.True(service.Register("leader", "secret1").Ok);
            Assert.True(service.Register("member", "secret1").Ok);
            Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
                "leader", "Leader", HumanClasses.Fighter, 0, 0, out var leader, level: 10));
            Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
                "member", "Member", HumanClasses.Mystic, 0, 0, out var member, level: 12));

            var saved = store.InsertClan(new ClanRecord(
                0,
                "Fine",
                0,
                leader!.Id,
                ClanProtocol.CrestId,
                0,
                0,
                [
                    new ClanMemberRecord(leader.Id, "leader", "Leader", HumanClasses.Fighter, 10, 0),
                    new ClanMemberRecord(member!.Id, "member", "Member", HumanClasses.Mystic, 12, 0)
                ]), new CharacterClanState("leader", leader.Id, 0, 0, 0));
            Assert.NotNull(saved);
            Assert.Equal(1, saved!.Id);
            Assert.Equal("Fine", saved.Name);

            var reloaded = JsonAccountStore.Open(path);
            var clan = Assert.Single(reloaded.ListClans());
            Assert.Equal(saved.Id, clan.Id);
            Assert.Equal(2, clan.Members.Count);
            Assert.Equal("Fine", clan.Name);
            Assert.Equal(leader.Id, clan.LeaderId);
            Assert.Equal(clan.Id, reloaded.FindById("leader", leader.Id)!.ClanId);
            Assert.Equal(clan.Id, reloaded.FindById("member", member.Id)!.ClanId);
            Assert.Equal(clan.Id, reloaded.FindClanByName("fine")!.Id);

            var leaderDelete = service.Delete("leader", characterId: leader.Id);
            Assert.False(leaderDelete.Ok);
            Assert.Equal(LobbyProtocol.DeleteFailed, leaderDelete.Code);
            Assert.Contains("leader", leaderDelete.Message, StringComparison.OrdinalIgnoreCase);

            var memberDelete = new LoginService(reloaded, new HandoffTokens(HandoffTokens.DefaultSecret))
                .Delete("member", characterId: member.Id);
            Assert.False(memberDelete.Ok);
            Assert.Contains("member", memberDelete.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(reloaded.TryHardDelete("member", member.Id));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Legacy_accounts_file_without_clans_still_loads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fineage-legacy-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "version": 1,
                  "nextCharacterId": 2,
                  "accounts": [
                    {
                      "name": "alice",
                      "passwordHash": "x",
                      "passwordSalt": "y",
                      "iterations": 1,
                      "characters": [
                        { "id": 1, "name": "Aria", "classId": 0, "level": 1 }
                      ]
                    }
                  ]
                }
                """);
            var store = JsonAccountStore.Open(path);
            Assert.Empty(store.ListClans());
            var aria = store.FindById("alice", 1);
            Assert.NotNull(aria);
            Assert.Equal(0, aria!.ClanId);
            Assert.Equal(0, aria.ClanJoinExpiryUnixMs);
            Assert.Equal(0, aria.ClanCreateExpiryUnixMs);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Penalties_survive_a_clan_update()
    {
        var store = JsonAccountStore.InMemory();
        Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("leader", "h", "s", 1, null));
        Assert.Equal(CreateCharacterStatus.Ok, store.TryAddCharacter(
            "leader", "Leader", 0, 0, 0, out var leader, level: 10));
        long join = 50;
        long create = 80;
        var clan = store.InsertClan(new ClanRecord(
            0, "Fine", 0, leader!.Id, 0, 0, 0,
            [new ClanMemberRecord(leader.Id, "leader", "Leader", 0, 10, 0)]),
            new CharacterClanState("leader", leader.Id, 0, join, create));
        Assert.NotNull(clan);
        Assert.True(store.UpdateClan(clan! with { DissolvingExpiryUnixMs = 99 }, []));
        Assert.Equal(99, store.FindClan(clan.Id)!.DissolvingExpiryUnixMs);
        Assert.Equal(join, store.FindById("leader", leader.Id)!.ClanJoinExpiryUnixMs);
        Assert.Equal(create, store.FindById("leader", leader.Id)!.ClanCreateExpiryUnixMs);
    }
}
