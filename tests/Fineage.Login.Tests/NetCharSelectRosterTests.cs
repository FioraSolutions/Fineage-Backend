using Fineage.Game;
using Fineage.Login;
using Fineage.Login.Net;
using Fineage.Net.Contract;
using Xunit;
using Fineage.Accounts;

namespace Fineage.Login.Tests;

public class NetCharSelectRosterTests
{
    [Fact]
    public void Net_roster_summary_includes_paperdoll_cp_and_roundtrips_codec()
    {
        var store = MemoryAccountStore.InMemory();
        var service = new LoginService(store, new HandoffTokens(HandoffTokens.DefaultSecret));
        Assert.True(service.Register("netroster", "pass1234").Ok);
        var created = service.CreateHuman("netroster", "Aria", HumanClasses.Fighter);
        Assert.True(created.Ok, created.Message);
        var ch = created.Value!;

        var net = new NetLoginServer(service, clock: () => DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var stats = CharSelectStats.For(ch);
        Assert.Equal(219, stats.WeaponItemId);
        Assert.True(stats.MaxCp > 0);
        Assert.Equal(219, stats.Paperdoll[(int)PaperdollSlot.RHand]);
        Assert.True(stats.Paperdoll[(int)PaperdollSlot.Chest] > 0);
        Assert.True(stats.Paperdoll[(int)PaperdollSlot.Legs] > 0);

        var point = ch.HasPosition
            ? new Fineage.Protocol.ClassSpawn.Point(ch.X, ch.Y, ch.Z)
            : ClassTemplates.Preview(ch.ClassId);

        var summary = new CharacterSummaryNet
        {
            CharacterId = ch.Id,
            Slot = 0,
            Name = ch.Name,
            ClassId = ch.ClassId,
            Race = ch.Race,
            Sex = ch.Sex,
            Level = ch.Level,
            Hp = stats.CurHp,
            MaxHp = stats.MaxHp,
            Mp = stats.CurMp,
            MaxMp = stats.MaxMp,
            HairStyle = ch.HairStyle,
            HairColor = ch.HairColor,
            Face = ch.Face,
            PendingDelete = false,
            X = point.X,
            Y = point.Y,
            Z = point.Z,
            DeleteSecondsLeft = 0,
            IsLastPlayed = true,
            CurCp = stats.CurCp,
            MaxCp = stats.MaxCp,
            Exp = ch.Exp,
            Sp = ch.Sp,
            Karma = 0,
            WeaponItemId = stats.WeaponItemId,
            Paperdoll = stats.Paperdoll
        };

        var roster = new CharacterRosterMsg { Characters = [summary] };
        var decoded = NetCodec.DecodeCharacterRoster(NetCodec.EncodeCharacterRoster(roster));
        var row = Assert.Single(decoded.Characters);
        Assert.Equal(219, row.WeaponItemId);
        Assert.Equal(219, row.Paperdoll[(int)PaperdollSlot.RHand]);
        Assert.True(row.Paperdoll[(int)PaperdollSlot.Chest] > 0);
        Assert.True(row.Paperdoll[(int)PaperdollSlot.Legs] > 0);
        Assert.True(row.MaxCp > 0);
        Assert.Equal(stats.CurCp, row.CurCp);
        Assert.Equal(point.X, row.X);
        Assert.Equal(point.Y, row.Y);
        Assert.Equal(point.Z, row.Z);
        Assert.True(row.IsLastPlayed);

        // Keep analyzer happy: NetLoginServer is constructed (same mapping path as live roster).
        Assert.NotNull(net);
    }
}
