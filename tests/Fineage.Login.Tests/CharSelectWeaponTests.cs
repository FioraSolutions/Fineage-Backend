using Fineage.Game;
using Fineage.Login;
using Fineage.Protocol;
using MessagePack;
using Xunit;
using Xunit.Abstractions;

public class CharSelectWeaponTests
{
    readonly ITestOutputHelper _out;
    public CharSelectWeaponTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData(HumanClasses.Fighter)]
    [InlineData(HumanClasses.Mystic)]
    [InlineData(53)] // Dwarf Fighter
    public void LobbyWire_WeaponItemId_matches_equipped_RHand(int classId)
    {
        var store = JsonAccountStore.InMemory();
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register("weapontest" + classId, "pass1234").Ok);
        int race = classId switch
        {
            53 => 4,
            _ => 0
        };
        var created = classId == 53
            ? service.CreateHuman("weapontest" + classId, "W" + classId, classId, race: race, sex: 0, hairStyle: 0, hairColor: 0, face: 0)
            : service.CreateHuman("weapontest" + classId, "W" + classId, classId);
        Assert.True(created.Ok && created.Value != null, created.Message);
        var c = created.Value!;
        _out.WriteLine($"class={classId} items:");
        foreach (var item in c.Items ?? [])
            _out.WriteLine($"  itemId={item.ItemId} slot={item.EquippedSlot} count={item.Count}");

        var stats = CharSelectStats.For(c);
        var row = LobbyWire.ToSummary(c, 0, DateTimeOffset.Parse("2026-01-01T00:00:00Z"), c.Id);
        _out.WriteLine($"CharSelectStats.WeaponItemId={stats.WeaponItemId} LobbyWire={row.WeaponItemId}");

        int rhand = 0;
        if (c.Items != null)
            foreach (var item in c.Items)
                if (item.EquippedSlot == (int)PaperdollSlot.RHand) { rhand = item.ItemId; break; }

        Assert.Equal(rhand, row.WeaponItemId);
        Assert.True(row.WeaponItemId > 0, "starter kit must equip an RHand weapon for CharSelect");
        Assert.Equal(CharSelectStats.PaperdollLength, row.Paperdoll.Length);
        Assert.Equal(rhand, row.Paperdoll[(int)PaperdollSlot.RHand]);
        Assert.True(row.Paperdoll[(int)PaperdollSlot.Chest] > 0, "starter kit must equip chest for CharSelect paperdoll");
        Assert.True(row.Paperdoll[(int)PaperdollSlot.Legs] > 0, "starter kit must equip legs for CharSelect paperdoll");

        byte[] body = MessagePackSerializer.Serialize(new CharacterListMsg { Characters = [row] });
        var round = MessagePackSerializer.Deserialize<CharacterListMsg>(body);
        Assert.Equal(row.WeaponItemId, round.Characters[0].WeaponItemId);
        Assert.Equal(row.Paperdoll[(int)PaperdollSlot.RHand], round.Characters[0].Paperdoll[(int)PaperdollSlot.RHand]);
        Assert.Equal(row.Paperdoll[(int)PaperdollSlot.Chest], round.Characters[0].Paperdoll[(int)PaperdollSlot.Chest]);
    }
}
