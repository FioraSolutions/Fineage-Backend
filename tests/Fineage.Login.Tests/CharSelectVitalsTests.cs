using Fineage.Login;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Xunit;
using Fineage.Accounts;

public class CharSelectVitalsTests
{
    [Fact]
    public void LobbyWire_summary_carries_vitals_and_paperdoll_through_the_net_roster_codec()
    {
        var store = MemoryAccountStore.InMemory();
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

        var roster = NetCodec.DecodeCharacterRoster(NetCodec.EncodeCharacterRoster(new CharacterRosterMsg
        {
            Characters =
            [
                new CharacterSummaryNet
                {
                    CharacterId = row.CharacterId,
                    Name = row.Name,
                    ClassId = row.ClassId,
                    Race = row.Race,
                    Sex = row.Sex,
                    Level = row.Level,
                    Slot = row.Slot,
                    Hp = row.CurHp,
                    MaxHp = row.MaxHp,
                    Mp = row.CurMp,
                    MaxMp = row.MaxMp,
                    WeaponItemId = row.WeaponItemId,
                    Paperdoll = row.Paperdoll
                }
            ]
        }));
        var round = roster.Characters[0];
        Assert.Equal(row.MaxHp, round.MaxHp);
        Assert.Equal(row.CurHp, round.Hp);
        Assert.Equal(row.MaxMp, round.MaxMp);
        Assert.Equal(row.WeaponItemId, round.WeaponItemId);
        Assert.Equal(row.Paperdoll[7], round.Paperdoll[7]);
        Assert.Equal(row.Paperdoll[10], round.Paperdoll[10]);
    }
}
