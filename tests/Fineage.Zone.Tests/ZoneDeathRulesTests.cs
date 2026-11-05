using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Slice P — death consequences against Mobius CT 0 Interlude.
/// <para>
/// <c>Player.calculateDeathExpPenalty</c> (<c>Player.java:5318-5363</c>) takes the level's
/// <c>PlayerXpPercentLostData</c> share off the bar between <c>expForLevel(level)</c> and <c>expForLevel(level + 1)</c>,
/// never off the absolute exp. <c>Player.calculateDeathPenaltyBuffLevel</c> (<c>:11690-11720</c>) rolls once from a
/// monster death and <c>increaseDeathPenaltyBuffLevel</c> applies skill 5076. <c>Creature.doRevive</c> (<c>:2524-2553</c>)
/// only lifts a bar that is still under <c>RespawnRestoreHP</c> / <c>RespawnRestoreMP</c>, which the reference
/// deployment sets to 65% and 0%.
/// </para>
/// </summary>
public sealed class ZoneDeathRulesTests
{
    /// <summary>Mobius <c>Character.ini RespawnRestoreHP = 65</c> / <c>RespawnRestoreMP = 0</c>.</summary>
    const double RespawnHp = 0.65d;

    /// <summary>Elven Fighter: base run 125, DEX 35 → stat bonus 1.15.</summary>
    const float ElfDexBonus = 1.15f;

    [Fact]
    public void Catalog_parses_the_mobius_table()
    {
        var rules = DeathRuleCatalog.LoadDefault();

        // playerXpPercentLost.xml: level 1 loses the whole 10% of the bar, and the share shrinks with the level.
        Assert.Equal(10.0d, rules.PercentForLevel(1), 6);
        Assert.Equal(7.625d, rules.PercentForLevel(20), 6);
        Assert.Equal(1.0d, rules.PercentForLevel(80), 6);
        // Above the table the last row holds, so a custom level cap still gets a sane share instead of a zero.
        Assert.Equal(rules.PercentForLevel(80), rules.PercentForLevel(120), 6);
        // Out of range below also clamps to the first row rather than throwing.
        Assert.Equal(10.0d, rules.PercentForLevel(0), 6);

        Assert.Equal(10, rules.DeathPenaltyChance);   // Character.ini DeathPenaltyChance
        Assert.Equal(RespawnHp, rules.RespawnRestoreHp, 6);
        Assert.Equal(0d, rules.RespawnRestoreMp, 6);
    }

    [Fact]
    public void ExpLost_is_the_share_of_the_level_bar()
    {
        var rules = DeathRuleCatalog.LoadDefault();
        var table = GameData.LoadDefault().Classes!.Experience;
        long bar = table.ExpToBeLevel(21) - table.ExpToBeLevel(20); // 1_023_775 - 835_854

        long lost = rules.ExpLost(20, bar);
        Assert.Equal((long)Math.Round(bar * 7.625d / 100d), lost);
        Assert.Equal(14_329, lost);

        // The multiplier models Stat.REDUCE_EXP_LOST_BY_* / RateKarmaExpLost.
        Assert.Equal(lost / 2, rules.ExpLost(20, bar, 0.5d));
        Assert.Equal(0, rules.ExpLost(20, 0));
    }

    /// <summary>
    /// The load-bearing difference from the naive "percent × total exp": a character parked just under the next level
    /// loses the same absolute amount as one standing on the first point of the bar.
    /// </summary>
    [Fact]
    public async Task Exp_loss_does_not_depend_on_where_in_the_bar_the_exp_sits()
    {
        var (kit, session, player) = await EnterAsync();
        var table = kit.Game.Classes!.Experience;
        long bar = table.ExpToBeLevel(2) - table.ExpToBeLevel(1);
        long expected = DeathRuleCatalog.LoadDefault().ExpLost(1, bar);

        // Bottom of the bar: the loss cannot push the exp under the level's start.
        player.Exp = 0;
        await KillPlayerAsync(kit, session, player);
        Assert.Equal(0, player.Exp);

        // Top of the bar: the whole share comes off, no more.
        player.Exp = bar - 1;
        await ReviveAsync(kit, session, player);
        await KillPlayerAsync(kit, session, player);
        Assert.Equal(bar - 1 - expected, player.Exp);
    }

    [Fact]
    public async Task Exp_loss_uses_the_next_level_bar_and_reports_the_new_value()
    {
        var (kit, session, player) = await EnterAsync();
        var table = kit.Game.Classes!.Experience;
        long start = table.ExpToBeLevel(20);
        long bar = table.ExpToBeLevel(21) - start;
        player.Level = 20;
        player.Exp = start + bar / 2;
        long expected = DeathRuleCatalog.LoadDefault().ExpLost(20, bar);

        await KillPlayerAsync(kit, session, player);

        Assert.Equal(20, player.Level);
        Assert.Equal(start + bar / 2 - expected, player.Exp);
    }

    /// <summary>
    /// PlayableStat.removeExp: the loss never sends the exp negative, and when it falls under the current level's
    /// floor the level follows it down — <c>addLevel(level - getLevel())</c> with a negative amount.
    /// </summary>
    [Fact]
    public async Task Exp_loss_delevels_instead_of_going_negative()
    {
        var (kit, session, player) = await EnterAsync();
        var table = kit.Game.Classes!.Experience;
        player.Level = 20;
        player.Exp = table.ExpToBeLevel(20) + 5;
        long lost = DeathRuleCatalog.LoadDefault().ExpLost(20, table.ExpToBeLevel(21) - table.ExpToBeLevel(20));

        await KillPlayerAsync(kit, session, player);

        Assert.Equal(table.ExpToBeLevel(20) + 5 - lost, player.Exp);
        // 821_530 is past the level-19 floor, so the character drops exactly one level and keeps the exp.
        Assert.Equal(19, player.Level);
    }

    /// <summary>Nothing can push the exp below zero, however small the bar and however large the share.</summary>
    [Fact]
    public async Task Exp_never_goes_negative()
    {
        var (kit, session, player) = await EnterAsync();
        player.Level = 1;
        player.Exp = 0;

        await KillPlayerAsync(kit, session, player);

        Assert.Equal(0, player.Exp);
        Assert.Equal(1, player.Level);
    }

    /// <summary>Admin kills carry no killer, so Mobius skips both the exp loss and the penalty roll.</summary>
    [Fact]
    public async Task Death_without_a_killer_costs_nothing()
    {
        var (kit, session, player) = await EnterAsync();
        var table = kit.Game.Classes!.Experience;
        player.Level = 20;
        player.Exp = table.ExpToBeLevel(20) + 500_000;
        long before = player.Exp;

        Assert.Null(await kit.World.TryAdminKillPlayerAsync(session, CancellationToken.None));

        Assert.Equal(before, player.Exp);
        Assert.Equal(0, player.DeathPenaltyLevel);
    }

    [Fact]
    public async Task Revive_restores_65_percent_hp_and_leaves_mp_alone()
    {
        var (kit, session, player) = await EnterAsync();
        player.MaxHp = 1000;
        player.MaxMp = 800;
        player.CurMp = 400;
        await KillPlayerAsync(kit, session, player);
        Assert.Equal(0, player.CurHp);

        await ReviveAsync(kit, session, player);

        // Creature.doDie only zeroes HP; MP is whatever the character died with, and RespawnRestoreMP = 0 does not
        // touch it either.
        Assert.Equal(650, player.CurHp);
        Assert.Equal(400, player.CurMp);
    }

    /// <summary>
    /// Creature.doRevive only *raises* a bar that is under the configured share; a resurrection that already restored
    /// more is never clipped back down to 65%.
    /// </summary>
    [Fact]
    public async Task Revive_does_not_clip_a_bar_that_is_already_higher()
    {
        var (kit, session, player) = await EnterAsync();
        player.MaxHp = 1000;
        player.MaxMp = 800;
        await KillPlayerAsync(kit, session, player);
        player.CurHp = 900;   // a resurrection spell restored more than the respawn share
        player.CurMp = 400;

        await ReviveAsync(kit, session, player);

        Assert.Equal(900, player.CurHp);
        Assert.Equal(400, player.CurMp);
    }

    /// <summary>
    /// Skill 5076 is a passive, not an entry in the trainer book, so Death Penalty has to be folded by the stat
    /// pipeline the way 4267/4270 are. Level 1 is pAtk ×0.9, pDef ×0.91, runSpd ×0.95 and −5 acc / rEvas.
    /// </summary>
    [Fact]
    public async Task Death_penalty_level_folds_skill_5076()
    {
        var (kit, _, player) = await EnterAsync();
        float cleanRun = player.RunSpeed;

        player.DeathPenaltyLevel = 0;
        var none = kit.World.PlayerStatMods(player);
        Assert.Equal(100d, none.Apply(CombatStat.PAtk, 100d), 6);
        Assert.Equal(100d, none.Apply(CombatStat.PDef, 100d), 6);
        Assert.Equal(100d, none.Apply(CombatStat.Accuracy, 100d), 6);

        player.DeathPenaltyLevel = 1;
        try
        {
            var one = kit.World.PlayerStatMods(player);
            Assert.Equal(90d, one.Apply(CombatStat.PAtk, 100d), 6);
            Assert.Equal(90d, one.Apply(CombatStat.MAtk, 100d), 6);
            Assert.Equal(91d, one.Apply(CombatStat.PDef, 100d), 6);
            Assert.Equal(91d, one.Apply(CombatStat.MDef, 100d), 6);
            Assert.Equal(95d, one.Apply(CombatStat.Evasion, 100d), 6);
            // accCombat / rEvas are flat subtractions, not multipliers.
            Assert.Equal(95d, one.Apply(CombatStat.Accuracy, 100d), 6);
            // runSpd ×0.95 on the live (DEX-scaled) speed.
            Assert.Equal(cleanRun * 0.95f, kit.World.RunSpeedStat(player, one), 3);
        }
        finally
        {
            player.DeathPenaltyLevel = 0;
        }
    }

    [Fact]
    public async Task Death_penalty_scales_with_the_level_and_stops_at_15()
    {
        var (kit, _, player) = await EnterAsync();

        player.DeathPenaltyLevel = 15;
        var top = kit.World.PlayerStatMods(player);
        // Level 15 of the table: pAtk 0.25, pDef 0.4, runSpd 0.6, −50 acc / rEvas.
        Assert.Equal(25d, top.Apply(CombatStat.PAtk, 100d), 6);
        Assert.Equal(40d, top.Apply(CombatStat.PDef, 100d), 6);
        Assert.Equal(50d, top.Apply(CombatStat.Accuracy, 100d), 6);

        // A level past the table clamps to 15 instead of indexing out of the func table.
        player.DeathPenaltyLevel = 99;
        try
        {
            var clamped = kit.World.PlayerStatMods(player);
            Assert.Equal(25d, clamped.Apply(CombatStat.PAtk, 100d), 6);
        }
        finally
        {
            player.DeathPenaltyLevel = 0;
        }
    }

    /// <summary>A successful roll raises the level, sends message 1916 with the new level, and persists it.</summary>
    [Fact]
    public async Task Monster_death_rolls_the_penalty_and_tells_the_owner()
    {
        var kit = new NetTestKit(deathRules: AlwaysRoll(true));
        var (session, player) = await kit.EnterAsync();
        int npcId = kit.World.NpcObjectIds.First();

        await KillPlayerAsync(kit, session, player, npcId);

        Assert.Equal(1, player.DeathPenaltyLevel);
        var stored = kit.Store.FindById(session.Account, session.ObjectId);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.DeathPenaltyLevel);
    }

    /// <summary>Chance 0 means the roll never fires, exactly like <c>DeathPenaltyChance = 0</c> in Mobius.</summary>
    [Fact]
    public async Task Zero_chance_never_raises_the_penalty()
    {
        var kit = new NetTestKit(deathRules: AlwaysRoll(false));
        var (session, player) = await kit.EnterAsync();
        int npcId = kit.World.NpcObjectIds.First();

        for (int i = 0; i < 5; i++)
        {
            await KillPlayerAsync(kit, session, player, npcId, revive: true);
        }

        Assert.Equal(0, player.DeathPenaltyLevel);
    }

    [Fact]
    public async Task Penalty_level_caps_at_15()
    {
        var kit = new NetTestKit(deathRules: AlwaysRoll(true));
        var (session, player) = await kit.EnterAsync();
        int npcId = kit.World.NpcObjectIds.First();
        player.DeathPenaltyLevel = 15;

        await KillPlayerAsync(kit, session, player, npcId);

        Assert.Equal(15, player.DeathPenaltyLevel);
    }

    /// <summary>Mobius loadCharFromDB:6578 restores the level, so the debuff survives a relog.</summary>
    [Fact]
    public async Task Stored_penalty_comes_back_on_enter()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync();
        Assert.Equal(0, player.DeathPenaltyLevel);

        Assert.True(kit.Store.TrySaveDeathPenalty(session.Account, session.ObjectId, 7));
        Assert.True(await kit.World.TryEnterAsync(session, CancellationToken.None));
        Assert.True(kit.World.TryGetLivePlayer(session, out var again));
        Assert.Equal(7, again.DeathPenaltyLevel);

        // The fold is live from the first tick: the re-entered character already runs slower.
        Assert.True(again.RunSpeed < again.BaseRunSpeed * ElfDexBonus,
            $"penalised run speed {again.RunSpeed} should be below the clean {again.BaseRunSpeed * ElfDexBonus}");
    }

    /// <summary>
    /// A delevel through the real death path re-derives the book and the gear penalties (Mobius
    /// <c>checkPlayerSkills</c> + <c>refreshOverloaded</c>). The row here is still inside the nine-level window, so it
    /// must survive — Mobius does not strip a skill for a one-level delevel.
    /// </summary>
    [Fact]
    public async Task Delevel_keeps_a_skill_that_is_still_in_range()
    {
        var (kit, session, player) = await EnterAsync();
        var table = kit.Game.Classes!.Experience;
        player.Level = 20;
        player.Exp = table.ExpToBeLevel(20);

        // Skill 141 row 3 has getLevel 15, so a delevel to 19 is still nine levels inside the window.
        player.LearnedSkills[141] = 3;
        kit.World.RefreshKnownSkillsForTest(player);

        await KillPlayerAsync(kit, session, player);

        Assert.Equal(19, player.Level);
        Assert.True(player.LearnedSkills.TryGetValue(141, out int kept));
        Assert.Equal(3, kept);
    }

    /// <summary>
    /// Mobius <c>Player.checkPlayerSkills</c> (<c>:12602-12640</c>): a learned row more than nine levels above the
    /// character drops to the highest row still in range, or leaves the book when there is none. Verified directly,
    /// because reaching that window through a death needs an exp loss of dozens of levels.
    /// </summary>
    [Fact]
    public async Task Out_of_range_learned_skill_is_dropped_and_a_lower_row_is_relearned()
    {
        var (kit, _, player) = await EnterAsync();

        // Skill 141 rows are getLevel 5 / 10 / 15. At level 1 the nine-level window reaches down to getLevel 10
        // (1 >= 10 - 9), so a learned row 3 falls back to row 2 — Mobius' "highest row still in range".
        player.LearnedSkills[141] = 3;
        kit.World.DecreaseSkillsOnDelevelForTest(player);
        Assert.Equal(2, player.LearnedSkills[141]);

        // Skill 1405's only rows sit at getLevel 61/76: nothing is in range at level 1, so it leaves the book.
        player.LearnedSkills[1405] = 1;
        kit.World.DecreaseSkillsOnDelevelForTest(player);
        Assert.False(player.LearnedSkills.ContainsKey(1405));
    }

    static DeathRuleCatalog AlwaysRoll(bool raise) =>
        DeathRuleCatalog.ForTest(chance: raise ? 100 : 0, respawnHp: 0.65, respawnMp: 0.0);

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player)> EnterAsync()
    {
        var kit = new NetTestKit();
        var (session, player) = await kit.EnterAsync(classId: 18);
        return (kit, session, player);
    }

    /// <summary>
    /// Kills the player through the real mob-death path so <c>ApplyDeathConsequences</c> runs with an NPC killer; the
    /// admin kill deliberately carries no killer and must stay free (see <see cref="Death_without_a_killer_costs_nothing"/>).
    /// </summary>
    static async Task KillPlayerAsync(NetTestKit kit, ZoneSession session, ZoneEntity player, int killerNpcId = 0, bool revive = false)
    {
        int npcId = killerNpcId != 0 ? killerNpcId : kit.World.NpcObjectIds.First();
        Assert.True(kit.World.TryGetEntityById(npcId, out var npc));

        kit.World.KillPlayerByNpcForTest(player, npc);
        Assert.True(player.Dead, $"player {player.ObjectId} did not die from npc {npcId}");

        if (revive)
            await ReviveAsync(kit, session, player);
    }

    static async Task ReviveAsync(NetTestKit kit, ZoneSession session, ZoneEntity player)
    {
        Assert.True(player.Dead);
        Assert.Null(await kit.World.TryReviveAsync(session, new ReviveRequestMsg { Action = DeathProtocol.ReviveToVillage }, CancellationToken.None));
        Assert.False(player.Dead);
    }
}
