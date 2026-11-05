using Fineage.Protocol;
using Fineage.Zone.Dungeons;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class DungeonAccessRuleTests
{
    const int Sacrifice = 1;
    const int Heretics = 9;

    [Fact]
    public void Catalog_has_eight_necropolises_and_six_catacombs_at_the_ziggurat_points()
    {
        var catalog = DungeonCatalog.LoadDefault();
        Assert.Equal(14, catalog.All.Count);
        Assert.All(catalog.All, site => Assert.Equal(0, site.FeeCount));

        Assert.True(catalog.TryGet(Sacrifice, out var sacrifice));
        Assert.Equal(DungeonKind.Necropolis, sacrifice.Kind);
        Assert.Equal("Necropolis of Sacrifice", sacrifice.Name);
        Assert.Equal(31095, sacrifice.EntranceNpcId);
        Assert.Equal((-41570, 209785, -5089), (sacrifice.EntranceX, sacrifice.EntranceY, sacrifice.EntranceZ));
        Assert.Equal(31103, sacrifice.ExitNpcId);
        Assert.Equal((-41567, 209292, -5091), (sacrifice.ExitX, sacrifice.ExitY, sacrifice.ExitZ));

        Assert.True(catalog.TryGet(Heretics, out var heretics));
        Assert.Equal(DungeonKind.Catacomb, heretics.Kind);
        Assert.Equal("Heretics Catacomb", heretics.Name);
        Assert.Equal(31114, heretics.EntranceNpcId);
        Assert.Equal((-53174, -250275, -7911), (heretics.EntranceX, heretics.EntranceY, heretics.EntranceZ));
        Assert.Equal(31120, heretics.ExitNpcId);
        Assert.Equal((42514, 143917, -5385), (heretics.ExitX, heretics.ExitY, heretics.ExitZ));

        Assert.Equal(8, catalog.All.Count(site => site.Kind == DungeonKind.Necropolis));
        Assert.Equal(6, catalog.All.Count(site => site.Kind == DungeonKind.Catacomb));
    }

    [Theory]
    [InlineData(SevenSignsPeriod.Recruiting)]
    [InlineData(SevenSignsPeriod.Competition)]
    [InlineData(SevenSignsPeriod.Results)]
    public void Outside_validation_either_cabal_enters_and_the_unregistered_are_refused(SevenSignsPeriod period)
    {
        var access = Gate(period, SevenSignsCabal.Dawn, SevenSignsCabal.Dawn, SevenSignsCabal.Dawn);
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dawn), Sacrifice));
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dusk), Heretics));
        Assert.Equal(DungeonAccessCode.NotRegistered, access.CanEnter(Who(SevenSignsCabal.None), Sacrifice));
        Assert.Equal(DungeonAccessCode.NotRegistered, access.CanEnter(Who(SevenSignsCabal.None), Heretics));
        var lines = access.RefusalLines(DungeonAccessCode.NotRegistered, Sacrifice);
        Assert.Equal(DungeonProtocol.GateRefusedMessage, lines[0].Id);
        Assert.Equal(DungeonProtocol.GateRefusedText, lines[0].Text);
    }

    [Fact]
    public void Seal_validation_gives_necropolises_to_the_avarice_winner_and_catacombs_to_the_gnosis_winner()
    {
        var dawn = Gate(SevenSignsPeriod.SealValidation, SevenSignsCabal.Dawn, SevenSignsCabal.Dawn, SevenSignsCabal.None);
        Assert.Equal(DungeonAccessCode.Allowed, dawn.CanEnter(Who(SevenSignsCabal.Dawn), Sacrifice));
        Assert.Equal(DungeonAccessCode.DawnOnly, dawn.CanEnter(Who(SevenSignsCabal.Dusk), Sacrifice));
        Assert.Equal(DungeonAccessCode.DawnOnly, dawn.CanEnter(Who(SevenSignsCabal.None), Sacrifice));
        Assert.Equal(DungeonAccessCode.DawnOnly, dawn.CanEnter(Who(SevenSignsCabal.Dawn), Heretics));

        var dusk = Gate(SevenSignsPeriod.SealValidation, SevenSignsCabal.Dusk, SevenSignsCabal.None, SevenSignsCabal.Dusk);
        Assert.Equal(DungeonAccessCode.Allowed, dusk.CanEnter(Who(SevenSignsCabal.Dusk), Heretics));
        Assert.Equal(DungeonAccessCode.DuskOnly, dusk.CanEnter(Who(SevenSignsCabal.Dawn), Heretics));
        Assert.Equal(DungeonAccessCode.DuskOnly, dusk.CanEnter(Who(SevenSignsCabal.Dusk), Sacrifice));

        var dawnLines = dawn.RefusalLines(DungeonAccessCode.DawnOnly, Sacrifice);
        Assert.Equal(DungeonProtocol.DawnOnlyMessage, dawnLines[0].Id);
        Assert.Equal(DungeonProtocol.DawnOnlyText, dawnLines[0].Text);
        Assert.Equal(DungeonProtocol.GateRefusedMessage, dawnLines[1].Id);
        var duskLines = dusk.RefusalLines(DungeonAccessCode.DuskOnly, Heretics);
        Assert.Equal(DungeonProtocol.DuskOnlyMessage, duskLines[0].Id);
        Assert.Equal(DungeonProtocol.DuskOnlyText, duskLines[0].Text);
    }

    [Fact]
    public void A_tie_during_validation_admits_either_cabal_and_refuses_the_unregistered()
    {
        var access = Gate(SevenSignsPeriod.SealValidation, SevenSignsCabal.None, SevenSignsCabal.Dawn, SevenSignsCabal.Dusk);
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dawn), Sacrifice));
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dusk), Heretics));
        Assert.Equal(DungeonAccessCode.NotRegistered, access.CanEnter(Who(SevenSignsCabal.None), Sacrifice));
        Assert.Equal(DungeonProtocol.Cabal, DungeonAccess.ToWire(DungeonAccessCode.NotRegistered));
        Assert.Equal(DungeonProtocol.Dawn, DungeonAccess.ToWire(DungeonAccessCode.DawnOnly));
        Assert.Equal(DungeonProtocol.Dusk, DungeonAccess.ToWire(DungeonAccessCode.DuskOnly));
    }

    [Fact]
    public void Open_flags_skip_the_cabal_check_for_that_kind_only()
    {
        var access = Gate(
            SevenSignsPeriod.SealValidation,
            SevenSignsCabal.Dawn,
            SevenSignsCabal.Dawn,
            SevenSignsCabal.Dawn,
            new DungeonAccessOptions { OpenNecropolis = true });
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.None), Sacrifice));
        Assert.Equal(DungeonAccessCode.DawnOnly, access.CanEnter(Who(SevenSignsCabal.Dusk), Heretics));
    }

    [Fact]
    public void Dead_and_fee_are_checked_only_after_the_cabal_gate()
    {
        var priced = new DungeonCatalog(
        [
            new DungeonSite(3, DungeonKind.Necropolis, "Priced", 1, 2, 1, 2, 3, 4, 5, 6, DungeonProtocol.AdenaItemId, 100),
            new DungeonSite(4, DungeonKind.Catacomb, "Ancient", 1, 2, 1, 2, 3, 4, 5, 6, DungeonProtocol.AncientAdenaItemId, 10)
        ]);
        var access = new DungeonAccess(priced);
        access.Use(new DungeonAccessState(SevenSignsPeriod.Competition, SevenSignsCabal.None, SevenSignsCabal.None, SevenSignsCabal.None));

        Assert.Equal(DungeonAccessCode.NotRegistered, access.CanEnter(Who(SevenSignsCabal.None, dead: true), 3));
        Assert.Equal(DungeonAccessCode.Dead, access.CanEnter(Who(SevenSignsCabal.Dawn, dead: true, adena: 100), 3));
        Assert.Equal(DungeonAccessCode.FeeRequired, access.CanEnter(Who(SevenSignsCabal.Dawn, adena: 99), 3));
        Assert.Equal(DungeonAccessCode.Allowed, access.CanEnter(Who(SevenSignsCabal.Dawn, adena: 100), 3));
        Assert.Equal(DungeonProtocol.NotEnoughAdenaMessage, access.RefusalLines(DungeonAccessCode.FeeRequired, 3)[0].Id);

        Assert.Equal(DungeonAccessCode.FeeRequired, access.CanEnter(new DungeonCharacter(SevenSignsCabal.Dusk, false, _ => 0), 4));
        Assert.Equal(DungeonProtocol.NotEnoughAncientAdenaText, access.RefusalLines(DungeonAccessCode.FeeRequired, 4)[0].Text);
        Assert.Equal(DungeonAccessCode.UnknownDungeon, access.CanEnter(Who(SevenSignsCabal.Dawn), 99));
    }

    [Fact]
    public void Staying_inside_ignores_a_fee_the_visitor_already_paid()
    {
        var priced = new DungeonCatalog(
        [
            new DungeonSite(1, DungeonKind.Necropolis, "Priced", 1, 2, 1, 2, 3, 4, 5, 6, DungeonProtocol.AdenaItemId, 100)
        ]);
        var access = new DungeonAccess(priced);
        access.Use(new DungeonAccessState(SevenSignsPeriod.Competition, SevenSignsCabal.None, SevenSignsCabal.None, SevenSignsCabal.None));
        Assert.True(access.MayRemain(SevenSignsCabal.Dawn, 1));
        Assert.False(access.MayRemain(SevenSignsCabal.None, 1));

        access.Use(new DungeonAccessState(SevenSignsPeriod.SealValidation, SevenSignsCabal.Dusk, SevenSignsCabal.Dusk, SevenSignsCabal.Dusk));
        Assert.False(access.MayRemain(SevenSignsCabal.Dawn, 1));
        Assert.True(access.MayRemain(SevenSignsCabal.Dusk, 1));
        Assert.Equal(DungeonProtocol.KickedValidationMessage, DungeonAccess.KickNotice(SevenSignsPeriod.SealValidation).Id);
        Assert.Equal(DungeonProtocol.KickedUnsignedMessage, DungeonAccess.KickNotice(SevenSignsPeriod.Recruiting).Id);
    }

    static DungeonAccess Gate(
        SevenSignsPeriod period,
        SevenSignsCabal winner,
        SevenSignsCabal avarice,
        SevenSignsCabal gnosis,
        DungeonAccessOptions? options = null)
    {
        var access = new DungeonAccess(DungeonCatalog.LoadDefault(), options);
        access.Use(new DungeonAccessState(period, winner, avarice, gnosis));
        return access;
    }

    static DungeonCharacter Who(SevenSignsCabal cabal, bool dead = false, int adena = 0) =>
        new(cabal, dead, itemId => itemId == DungeonProtocol.AdenaItemId ? adena : 0);
}
