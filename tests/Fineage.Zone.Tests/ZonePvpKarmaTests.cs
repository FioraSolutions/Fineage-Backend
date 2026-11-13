using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius PvP flag + karma/PK (#271).</summary>
public sealed class ZonePvpKarmaTests
{
    [Fact]
    public void CalculateKarmaGain_first_pk_is_720()
    {
        Assert.Equal(720, ZoneWorld.CalculateKarmaGain(0, isSummon: false));
        Assert.Equal(1080, ZoneWorld.CalculateKarmaGain(1, isSummon: false));
    }

    [Fact]
    public async Task Wind_strike_on_a_player_deals_damage_and_flags()
    {
        // Outside elf_town_peace1. A cast inside the polygon is refused with no flag and no HP loss.
        const int x = 49000;
        const int y = 50000;
        const int z = -3000;
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var (_, attacker) = await kit.EnterAsync(classId: 10, race: 0, x: x, y: y, z: z);
        var (_, victim) = await kit.EnterAsync(classId: 10, race: 0, x: x + 40, y: y, z: z);
        int hp = victim.CurHp;

        Assert.Null(await kit.World.TrySkillUseAsync(attacker.Session!, 1177, victim.ObjectId, CancellationToken.None));
        await kit.World.CompleteCastForTestAsync(attacker.Session!);

        Assert.True(victim.CurHp < hp);
        Assert.True(attacker.PvpFlagged);
    }

    [Fact]
    public async Task Damaging_white_player_flags_attacker()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var (_, attacker) = await kit.EnterAsync(classId: 54, race: 4);
        var (_, victim) = await kit.EnterAsync(classId: 54, race: 4, x: attacker.X + 20, y: attacker.Y, z: attacker.Z);

        Assert.False(attacker.PvpFlagged);
        kit.World.UpdatePvPStatusForTest(attacker, victim);
        Assert.True(attacker.PvpFlagged);
        Assert.True(attacker.PvpFlagUntilMs > 0);
        Assert.False(victim.PvpFlagged);
    }

    [Fact]
    public async Task Killing_white_player_adds_karma_and_pk()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var (_, killer) = await kit.EnterAsync(classId: 54, race: 4);
        var (_, victim) = await kit.EnterAsync(classId: 54, race: 4, x: killer.X + 20, y: killer.Y, z: killer.Z);

        kit.World.OnKillUpdatePvPKarmaForTest(killer, victim);
        Assert.Equal(720, killer.Karma);
        Assert.Equal(1, killer.PkKills);
        Assert.Equal(0, killer.PvpKills);

        var stored = kit.Store.FindById(killer.Session!.Account, killer.ObjectId);
        Assert.NotNull(stored);
        Assert.Equal(720, stored!.Karma);
        Assert.Equal(1, stored.PkKills);
    }

    [Fact]
    public async Task Killing_flagged_player_adds_pvp_kill()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var (_, killer) = await kit.EnterAsync(classId: 54, race: 4);
        var (_, victim) = await kit.EnterAsync(classId: 54, race: 4, x: killer.X + 20, y: killer.Y, z: killer.Z);
        victim.PvpFlagged = true;

        kit.World.OnKillUpdatePvPKarmaForTest(killer, victim);
        Assert.Equal(0, killer.Karma);
        Assert.Equal(0, killer.PkKills);
        Assert.Equal(1, killer.PvpKills);
    }

    [Fact]
    public async Task Karma_persists_across_reenter()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var who = kit.Create(classId: 54, race: 4);
        var (session, player) = await kit.EnterAsync(who);
        Assert.True(kit.Store.TrySaveKarmaPk(session.Account, player.ObjectId, 1500, 2, 3));

        var kit2 = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting", store: kit.Store);
        var (_, again) = await kit2.EnterAsync(who);
        Assert.Equal(1500, again.Karma);
        Assert.Equal(2, again.PkKills);
        Assert.Equal(3, again.PvpKills);
    }
}
