using Fineage.Protocol;
using Fineage.Zone.SevenSigns;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class SevenSignsFestivalParityTests
{
    [Fact]
    public void Spawn_catalog_loads_primary_wave_for_dawn_tier_0()
    {
        var spawns = SevenSignsFestivalSpawnCatalog.LoadDefault();
        var wave = spawns.Spawns(SevenSignsCabal.Dawn, 0, FestivalMobWave.Primary);
        Assert.NotEmpty(wave);
        Assert.All(wave, row => Assert.True(row.NpcId > 0));
    }

    [Fact]
    public void Accumulated_bonus_tracks_seal_stone_value()
    {
        var catalog = SevenSignsFestivalCatalog.LoadDefault();
        var board = new SevenSignsFestivalBoard(catalog);
        board.AddAccumulatedBonus(0, SevenSignsProtocol.BlueSealStoneId, 10);
        Assert.Equal(10L * SevenSignsProtocol.BlueSealStoneValue, board.AccumulatedBonus(0));
        long share = board.TakeAccumulatedShare(0, 5);
        Assert.Equal(6, share);
        Assert.Equal(24, board.AccumulatedBonus(0));
    }

    [Fact]
    public void Festival_guide_maps_npc_31127_to_tier_0_dawn()
    {
        Assert.True(SevenSignsFestivalGuide.TryProfile(31127, out var profile));
        Assert.Equal(0, profile.TierId);
        Assert.Equal(SevenSignsCabal.Dawn, profile.Cabal);
        Assert.Equal(900, profile.BlueStones);
    }

    [Fact]
    public void Board_mobius_cycle_windows()
    {
        var catalog = SevenSignsFestivalCatalog.LoadDefault();
        var board = new SevenSignsFestivalBoard(catalog);
        const long anchor = 1_000_000;
        board.BeginCompetition(anchor);
        long signupClose = catalog.SignupCloseMs;
        Assert.True(board.SignupOpen(anchor));
        Assert.False(board.FestivalSliceActive(anchor));
        Assert.True(board.SignupOpen(anchor + signupClose - 1));
        Assert.False(board.SignupOpen(anchor + signupClose));
        Assert.True(board.FestivalSliceActive(anchor + signupClose));
        Assert.True(board.FestivalSliceActive(anchor + signupClose + catalog.FestivalLengthMs - 1));
        Assert.False(board.FestivalSliceActive(anchor + signupClose + catalog.FestivalLengthMs));
    }
}