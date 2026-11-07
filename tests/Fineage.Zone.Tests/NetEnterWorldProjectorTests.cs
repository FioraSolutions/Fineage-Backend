using Fineage.Game;
using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Zone.Net;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Net enter projects the live <see cref="ZoneEntity"/> after TryEnter — race/inventory/skills
/// come from world join, not a parallel store re-read with Human defaults.
/// </summary>
public class NetEnterWorldProjectorTests
{
    [Fact]
    public async Task Enter_projector_uses_entity_race_inventory_and_skills()
    {
        var store = MemoryAccountStore.InMemory();
        Assert.Equal(AddAccountStatus.Created, store.TryAddAccount("netenter", "hash", "salt", 1, null));
        var game = GameData.LoadDefault();
        var kit = CharacterInventory.Rows(game.CreateStarterInventory(25));
        var status = store.TryAddCharacter(
            "netenter",
            "ElfMage",
            classId: 25,
            race: 1,
            sex: 0,
            out var created,
            items: kit,
            hairStyle: 1,
            hairColor: 0,
            face: 1,
            hasPosition: true,
            x: 46045,
            y: 41251,
            z: -3440,
            level: 1,
            maxHp: 100,
            hp: 100,
            maxMp: 80,
            mp: 80);
        Assert.Equal(CreateCharacterStatus.Ok, status);
        CharacterRecord ch = created!;
        Assert.Equal(1, ch.Race);
        Assert.Equal(25, ch.ClassId);

        var world = new ZoneWorld(game: game, accounts: store);
        var session = new ZoneSession();
        session.Bind(ch.Id, ch.Account, ch.Name, ch.ClassId);

        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));
        Assert.True(world.TryGetLivePlayer(session, out var entity));
        Assert.Equal(1, entity.Race);
        Assert.Equal(25, entity.ClassId);
        Assert.NotNull(entity.Inventory);
        Assert.True(entity.Inventory!.Items.Count > 0);
        Assert.True(entity.KnownSkills.Count > 0);

        var self = EntityId.FromObjectId(ch.Id);
        var packets = NetEnterWorldProjector.FromEntity(entity, self, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        Assert.Equal(1, packets.Spawn.Race);
        Assert.Equal(25, packets.Spawn.ClassId);
        // Level/MaxHp drive the client's target colour and HP bar; they must reach the wire.
        Assert.Equal(entity.Level, packets.Spawn.Level);
        Assert.Equal(entity.MaxHp, packets.Spawn.MaxHp);
        Assert.True(packets.Spawn.Level > 0);
        Assert.Equal(entity.CurHp, packets.Stats.Hp);
        Assert.Equal(entity.MaxHp, packets.Stats.MaxHp);
        Assert.True(packets.Items.Items.Length > 0);
        Assert.Contains(packets.Items.Items, item => item.EquippedSlot >= 0);
        Assert.True(packets.Skills.Skills.Length > 0);

        var itemsAgain = NetCodec.DecodeItemList(NetCodec.EncodeItemList(packets.Items));
        Assert.Equal(packets.Items.Items.Length, itemsAgain.Items.Length);

        // Paperdoll/icon fields drive what the client draws: they must survive the projection
        // and the wire round-trip, not just sit in the V2 source message.
        Assert.Contains(packets.Items.Items, i => !string.IsNullOrEmpty(i.Kind));
        Assert.Contains(packets.Items.Items, i => !string.IsNullOrEmpty(i.BodySlot));
        Assert.All(packets.Items.Items, i => Assert.False(string.IsNullOrEmpty(i.Icon)));
        Assert.Contains(itemsAgain.Items, i => !string.IsNullOrEmpty(i.WeaponType));
        Assert.Contains(itemsAgain.Items, i => !string.IsNullOrEmpty(i.ArmorType));

        var skillsAgain = NetCodec.DecodeSkillList(NetCodec.EncodeSkillList(packets.Skills));
        Assert.Equal(packets.Skills.Skills.Length, skillsAgain.Skills.Length);
    }
}
