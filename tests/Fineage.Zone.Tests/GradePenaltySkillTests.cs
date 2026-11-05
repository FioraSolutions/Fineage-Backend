using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Grade Penalty 4267 (<c>data/skill-passives.json:5004</c>) folded through the ordinary
/// <see cref="CombatStatMods"/> pipeline, the same way Weight Penalty 4270 already is.
/// <para>
/// Mobius <c>Player.refreshExpertisePenalty</c> (<c>Player.java:2017-2068</c>) adds
/// <c>addSkill(4267, Math.max(weaponPenalty, armorPenalty))</c>, so the penalty is a passive skill like any other and
/// reaches attack speed, evasion, movement and regen through the stat calculators. Before this slice the server
/// computed the penalty and published the <c>EtcStatusUpdate</c> flag, but never folded the skill, so a level-1
/// character in D-grade gear ran and swung at full speed.
/// </para>
/// </summary>
public sealed class GradePenaltySkillTests
{
    /// <summary>Elven Fighter 18: base run 125 / walk 90, DEX 35 → stat bonus 1.15.</summary>
    const float ElfDexBonus = 1.15f;

    static async Task<(NetTestKit Kit, ZoneEntity Player)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (_, player) = await kit.EnterAsync(classId: 18);
        WeightPenaltyWorldTests.StripToPaperdoll(player);
        Assert.True(kit.World.RefreshWeightPenalty(player));
        Assert.Equal(0, player.WeightPenaltyLevel);
        return (kit, player);
    }

    /// <summary>Folds the live player's stats with the grade level forced to <paramref name="level"/>.</summary>
    static (float Run, double RegHp, double PAtkSpd, double Evasion, double Accuracy) FoldedAt(
        NetTestKit kit, ZoneEntity player, int level)
    {
        int before = player.GradePenaltyLevel;
        player.GradePenaltyLevel = level;
        try
        {
            var mods = kit.World.PlayerStatMods(player);
            return (kit.World.RunSpeedStat(player, mods),
                mods.Apply(CombatStat.RegHp, 10d),
                mods.Apply(CombatStat.PAtkSpd, 100d),
                mods.Apply(CombatStat.Evasion, 100d),
                mods.Apply(CombatStat.Accuracy, 100d));
        }
        finally
        {
            player.GradePenaltyLevel = before;
        }
    }

    [Fact]
    public void Catalog_carries_the_4267_funcs()
    {
        var passives = SkillStatEffectCatalog.LoadPassives();
        Assert.True(passives.TryGet(GradePenalty.SkillId, out var effect),
            "Grade Penalty 4267 must be in data/skill-passives.json");
        Assert.Equal(10, effect.Levels);

        // Levels 1–4 are the ones the penalty can reach (GradePenalty.MaxLevel).
        Assert.Equal([0.22d, 0.19d, 0.16d, 0.13d], effect.Funcs.Single(f => f.Stat == CombatStat.RunSpd).Values[..4]);

        // Accuracy is a `sub` func carrying the magnitude 20, so the fold subtracts rather than multiplies.
        var accuracy = effect.Funcs.Single(f => f.Stat == CombatStat.Accuracy);
        Assert.Equal('s', accuracy.Op);
        Assert.Equal(20d, accuracy.Values[0]);
    }

    [Fact]
    public async Task Level_zero_leaves_every_stat_untouched()
    {
        var (kit, player) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;

        var at0 = FoldedAt(kit, player, 0);
        Assert.Equal(baseRun, at0.Run, 3);
        Assert.Equal(10d, at0.RegHp, 6);
        Assert.Equal(100d, at0.PAtkSpd, 6);
        Assert.Equal(100d, at0.Evasion, 6);
        Assert.Equal(100d, at0.Accuracy, 6);
    }

    [Theory]
    [InlineData(1, 0.22d)]
    [InlineData(2, 0.19d)]
    [InlineData(3, 0.16d)]
    [InlineData(4, 0.13d)]
    public async Task Each_level_scales_speed_attack_and_regen(int level, double factor)
    {
        var (kit, player) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;

        var at = FoldedAt(kit, player, level);
        // runSpd, pAtkSpd, rEvas, regHp and regMp all share the same factor at these levels.
        Assert.Equal(baseRun * (float)factor, at.Run, 3);
        Assert.Equal(100d * factor, at.PAtkSpd, 6);
        Assert.Equal(100d * factor, at.Evasion, 6);
        Assert.Equal(10d * factor, at.RegHp, 6);
        // Accuracy is a flat subtraction, not a multiplier.
        Assert.Equal(80d, at.Accuracy, 6);
    }

    /// <summary>
    /// The penalty is a passive like any other, so it composes with buffs in Mobius order (MUL 20 then ADD 30)
    /// instead of overwriting them.
    /// </summary>
    [Fact]
    public async Task Penalty_composes_with_a_speed_buff_instead_of_replacing_it()
    {
        var (kit, player) = await SetupAsync();
        float baseRun = player.BaseRunSpeed * ElfDexBonus;

        ZoneAbnormals.Apply(player.Abnormals, 1204, 1, 1200, kit.Now.ToUnixTimeMilliseconds(), 1);
        try
        {
            var at2 = FoldedAt(kit, player, 2);
            // Wind Walk's +20 lands after the ×0.19, exactly like 4270's ×0.5 case.
            Assert.Equal(baseRun * 0.19f + 20f, at2.Run, 3);
        }
        finally
        {
            player.Abnormals.Clear();
        }
    }

    /// <summary>
    /// End to end: wearing gear above your expertise must move the live speed. This is the bug the slice fixes —
    /// the penalty used to be computed and shipped to the client, but never reached the stat pipeline.
    /// </summary>
    [Fact]
    public async Task Wearing_out_of_grade_gear_slows_the_live_pawn()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        WeightPenaltyWorldTests.StripToPaperdoll(player);
        float clean = player.RunSpeed;
        Assert.Equal(0, player.GradePenaltyLevel);

        // A level-1 character has expertise rank 0, so any D-grade weapon already sits above it.
        Assert.Null(await kit.World.TryGiveItemAsync(session,
            new GiveItemMsg { ItemId = DGradeWeaponId, Count = 1 }, CancellationToken.None));
        int weapon = player.Inventory!.FindByItemId(DGradeWeaponId)!.ObjectId;
        Assert.Null(await kit.World.TryUseItemAsync(session, new UseItemMsg { ObjectId = weapon }, CancellationToken.None));

        Assert.True(player.GradePenaltyLevel > 0, "equipping above expertise must set the grade level");
        Assert.True(player.RunSpeed < clean, $"run speed {player.RunSpeed} should be below the clean {clean}");
    }

    /// <summary>Unequipping walks the level back to 0 and restores the clean speed.</summary>
    [Fact]
    public async Task Removing_the_gear_restores_the_clean_speed()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        WeightPenaltyWorldTests.StripToPaperdoll(player);
        float clean = player.RunSpeed;

        Assert.Null(await kit.World.TryGiveItemAsync(session,
            new GiveItemMsg { ItemId = DGradeWeaponId, Count = 1 }, CancellationToken.None));
        int weapon = player.Inventory!.FindByItemId(DGradeWeaponId)!.ObjectId;
        Assert.Null(await kit.World.TryUseItemAsync(session, new UseItemMsg { ObjectId = weapon }, CancellationToken.None));
        Assert.True(player.RunSpeed < clean);

        var worn = player.Inventory.Find(weapon);
        Assert.NotNull(worn);
        Assert.NotNull(worn!.EquippedSlot);
        Assert.Null(await kit.World.TryUnequipItemAsync(session,
            new UnequipItemMsg { PaperdollSlot = (int)worn.EquippedSlot.Value }, CancellationToken.None));
        Assert.Equal(0, player.GradePenaltyLevel);
        Assert.Equal(clean, player.RunSpeed, 3);
    }

    /// <summary>Bastard Sword: a real D-grade one-handed sword from <c>data/items.json</c>.</summary>
    const int DGradeWeaponId = 69;
}
