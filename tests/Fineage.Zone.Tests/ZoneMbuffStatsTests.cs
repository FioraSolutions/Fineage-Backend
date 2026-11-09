using Fineage.Game;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>//mbuff mage pack folds song/dance combat funcs into live PlayerStats.</summary>
public sealed class ZoneMbuffStatsTests
{
    [Fact]
    public void Mage_pack_raises_mAtkSpd_and_includes_songs()
    {
        Assert.Contains(268, ZoneBuffs.MageSelfBuffs); // Song of Wind
        Assert.Contains(273, ZoneBuffs.MageSelfBuffs); // Dance of the Mystic
        Assert.Contains(276, ZoneBuffs.MageSelfBuffs); // Dance of Concentration
        Assert.Contains(365, ZoneBuffs.MageSelfBuffs); // Siren's Dance

        var game = GameData.LoadDefault();
        var bag = new PlayerInventory(game.Items);
        var player = new ZoneEntity { ObjectId = 1, Kind = ZoneEntityKind.Player, ClassId = 10, Level = 40, Inventory = bag };
        var world = new ZoneWorld(game: game);

        int before = world.PlayerStats(player).MAtkSpd;
        long now = 1_000_000;
        foreach (int skillId in ZoneBuffs.MageSelfBuffs)
        {
            var skill = game.Skills.Find(skillId);
            if (skill == null || !skill.TryLevelData(Math.Max(1, skill.MaxLevel), out var data))
                continue;
            ZoneAbnormals.Apply(
                player.Abnormals,
                skill.SkillId,
                data.AbnormalLevel,
                skill.AbnormalTimeSeconds,
                now,
                data.Level);
        }

        Assert.Contains(player.Abnormals, a => a.Id == 268);
        Assert.Contains(player.Abnormals, a => a.Id == 276);
        int after = world.PlayerStats(player).MAtkSpd;
        Assert.True(after > before, $"mbuff MAtkSpd {after} should exceed bare {before}");
    }
}
