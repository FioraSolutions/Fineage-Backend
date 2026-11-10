using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Ctrl force-use on friendly skills: without Ctrl a mob target is invalid (no self-redirect);
/// with Ctrl the cast lands and the caster is PvP-flagged. Resurrection ConfirmDlg restore path.
/// </summary>
public sealed class ForceSupportTargetTests
{
    const int Heal = 1011;
    const int Resurrection = 1016;

    [Fact]
    public async Task Heal_on_mob_without_ctrl_is_invalid_target_not_self()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (session, player) = await kit.EnterAsync(classId: 10, level: 40, x: mob.X, y: mob.Y, z: mob.Z);
        player.KnownSkills[Heal] = 1;
        player.CurMp = 500;
        player.CurHp = Math.Max(1, player.MaxHp / 2);
        int hpBefore = player.CurHp;
        session.SelectedTargetId = mob.ObjectId;

        Assert.Null(await kit.World.TrySkillUseAsync(
            session, Heal, mob.ObjectId, CancellationToken.None, ctrlPressed: false));

        Assert.Equal(hpBefore, player.CurHp);
        Assert.False(player.PvpFlagged);
        Assert.Null(player.PendingHit);
    }

    [Fact]
    public async Task Heal_on_mob_with_ctrl_flags_caster()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        mob.CurHp = Math.Max(1, mob.MaxHp / 2);
        var (session, player) = await kit.EnterAsync(classId: 10, level: 40, x: mob.X, y: mob.Y, z: mob.Z);
        player.KnownSkills[Heal] = 1;
        player.CurMp = 500;
        session.SelectedTargetId = mob.ObjectId;

        Assert.Null(await kit.World.TrySkillUseAsync(
            session, Heal, mob.ObjectId, CancellationToken.None, ctrlPressed: true));

        Assert.True(player.PvpFlagged);
        Assert.NotNull(player.PendingHit);
    }

    [Fact]
    public async Task Resurrection_offers_confirm_and_restores_exp_on_accept()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (healerSession, healer) = await kit.EnterAsync(classId: 10, level: 40, x: mob.X, y: mob.Y, z: mob.Z);
        var (deadSession, dead) = await kit.EnterAsync(classId: 0, level: 20, x: mob.X + 20, y: mob.Y, z: mob.Z);

        var table = kit.Game.Classes!.Experience;
        long start = table.ExpToBeLevel(20);
        long bar = table.ExpToBeLevel(21) - start;
        dead.Level = 20;
        dead.Exp = start + bar / 2;
        long beforeDeath = dead.Exp;

        kit.World.KillPlayerByNpcForTest(dead, mob);
        Assert.True(dead.Dead);
        Assert.Equal(beforeDeath, dead.ExpBeforeDeath);
        Assert.True(dead.Exp < beforeDeath);
        long postDeathExp = dead.Exp;
        long lost = dead.ExpBeforeDeath - postDeathExp;

        healer.KnownSkills[Resurrection] = 2; // power 20
        healer.CurMp = 500;
        healerSession.SelectedTargetId = dead.ObjectId;

        Assert.Null(await kit.World.TrySkillUseAsync(
            healerSession, Resurrection, dead.ObjectId, CancellationToken.None));
        await kit.World.CompleteCastForTestAsync(healerSession);

        Assert.True(dead.ReviveRequested);
        double power = dead.RevivePower;
        Assert.True(power > 0);

        Assert.Null(await kit.World.TryReviveAsync(
            deadSession, DeathProtocol.AcceptSkillResurrection, CancellationToken.None));
        Assert.False(dead.Dead);
        Assert.Equal(0, dead.ExpBeforeDeath);
        long expected = postDeathExp + (long)Math.Round(lost * power / 100d);
        Assert.Equal(expected, dead.Exp);
    }
}
