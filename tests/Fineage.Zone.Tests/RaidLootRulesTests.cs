using Fineage.Zone.Drops;
using Fineage.Zone.Party;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class RaidLootRulesTests
{
    [Fact]
    public void Top_damage_party_leader_gets_loot_protection_owner()
    {
        var npc = new ZoneEntity
        {
            ObjectId = 90_000,
            Kind = ZoneEntityKind.Npc,
            X = 100,
            Y = 100,
            Level = 30
        };
        var leader = Player(1, 110, 100);
        var member = Player(2, 105, 100);
        var outsider = Player(3, 5000, 100);

        npc.DamageBy[member.ObjectId] = 5_000;
        npc.DamageBy[leader.ObjectId] = 1_000;
        npc.DamageBy[outsider.ObjectId] = 50_000;

        var lookup = new Dictionary<int, ZoneEntity>
        {
            [leader.ObjectId] = leader,
            [member.ObjectId] = member,
            [outsider.ObjectId] = outsider
        };
        var party = new ZoneParty { PartyId = 1, LeaderObjectId = leader.ObjectId };
        party.MemberIds.Add(leader.ObjectId);
        party.MemberIds.Add(member.ObjectId);

        int owner = ZoneWorld.RaidLootOwnerObjectId(
            npc,
            killer: member,
            id => lookup.GetValueOrDefault(id),
            id => id is 1 or 2 ? party : null);

        Assert.Equal(leader.ObjectId, owner);
    }

    static ZoneEntity Player(int id, int x, int y) => new()
    {
        ObjectId = id,
        Kind = ZoneEntityKind.Player,
        Session = new ZoneSession(),
        X = x,
        Y = y,
        Level = 40,
        MaxHp = 100,
        CurHp = 100
    };
}