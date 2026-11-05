using Fineage.Login;
using Fineage.Protocol;
using MessagePack;
using Xunit;

public class CharSelectVitalsTests
{
    [Fact]
    public void LobbyWire_summary_msgpack_has_26_keys_including_vitals_and_paperdoll()
    {
        var store = JsonAccountStore.InMemory();
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register("vitalstest", "pass1234").Ok);
        var created = service.CreateHuman("vitalstest", "MageOne", HumanClasses.Mystic);
        Assert.True(created.Ok && created.Value != null, created.Message);
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var row = LobbyWire.ToSummary(created.Value!, slot: 0, now, lastPlayedId: created.Value.Id);
        Assert.True(row.MaxHp > 0);
        Assert.True(row.MaxMp > 0);
        Assert.True(row.WeaponItemId > 0);
        Assert.Equal(CharSelectStats.PaperdollLength, row.Paperdoll.Length);
        Assert.Equal(row.WeaponItemId, row.Paperdoll[7]);

        byte[] body = MessagePackSerializer.Serialize(new CharacterListMsg { Characters = [row] });
        // CharacterListMsg = [ characters[] ]; each summary is a fixarray.
        Assert.Equal(0x91, body[0]); // array of 1
        Assert.True(body[1] == 0x91 || body[1] == 0xdc); // characters array
        int i = 1;
        // read characters array header
        byte b = body[i++];
        int count;
        if ((b & 0xf0) == 0x90) count = b & 0x0f;
        else throw new Xunit.Sdk.XunitException("expected characters array");
        Assert.Equal(1, count);
        b = body[i++];
        int n;
        if ((b & 0xf0) == 0x90) n = b & 0x0f;
        else if (b == 0xdc) { n = (body[i] << 8) | body[i + 1]; i += 2; }
        else throw new Xunit.Sdk.XunitException($"expected summary array got 0x{b:X2}");
        Assert.Equal(26, n);

        var round = MessagePackSerializer.Deserialize<CharacterListMsg>(body);
        Assert.Equal(row.MaxHp, round.Characters[0].MaxHp);
        Assert.Equal(row.CurHp, round.Characters[0].CurHp);
        Assert.Equal(row.MaxMp, round.Characters[0].MaxMp);
        Assert.Equal(row.WeaponItemId, round.Characters[0].WeaponItemId);
        Assert.Equal(row.Exp, round.Characters[0].Exp);
        Assert.Equal(row.Paperdoll[7], round.Characters[0].Paperdoll[7]);
        Assert.Equal(row.Paperdoll[10], round.Characters[0].Paperdoll[10]);
    }
}
