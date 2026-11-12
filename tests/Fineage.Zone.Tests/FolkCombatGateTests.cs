using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius FolkStatus vs Attackable: town Folk take no damage and never retaliate; Guards (Attackable type,
/// XML attackable=false) still fight back when hit.
/// </summary>
public sealed class FolkCombatGateTests
{
    const int FolkId = 9_401_001;
    const int GuardId = 9_401_002;

    static SpawnCatalog Village() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["folk-gate"] = new("folk-gate", true,
            [
                new SpawnNpcDefinition(
                    FolkId, 30370, "Nerupa", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ,
                    0, false, 2444, 1f, 1f, 60, 0, "Nerupa", "Folk",
                    Level: 70, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: []),
                new SpawnNpcDefinition(
                    GuardId, 30041, "Arnold", NetTestKit.ElfX + 60, NetTestKit.ElfY, NetTestKit.ElfZ,
                    0, false, 3290, 1f, 1f, 60, 0, "Guard", "Guard",
                    Level: 80, AttackRange: 40, AttackSpeed: 253, Aggressive: true, AggroRange: 1000, ClanHelpRange: 300, Clans: []),
            ])
        });

    [Fact]
    public void Combat_npc_type_matches_mobius_attackable_hierarchy()
    {
        Assert.True(ZoneCombat.IsCombatNpcType("Guard"));
        Assert.True(ZoneCombat.IsCombatNpcType("Monster"));
        Assert.False(ZoneCombat.IsCombatNpcType("Folk"));
        Assert.False(ZoneCombat.IsCombatNpcType("Merchant"));
        Assert.False(ZoneCombat.IsCombatNpcType("Trainer"));
        Assert.False(ZoneCombat.IsCombatNpcType("Teleporter"));
        // Smoke rows without type: Attackable flag wins.
        Assert.True(ZoneCombat.TakesNpcCombatDamage("", attackable: true));
        Assert.False(ZoneCombat.TakesNpcCombatDamage("", attackable: false));
        Assert.True(ZoneCombat.TakesNpcCombatDamage("Guard", attackable: false));
        Assert.False(ZoneCombat.TakesNpcCombatDamage("Folk", attackable: true));
    }

    [Fact]
    public async Task Hitting_folk_does_not_damage_or_aggro()
    {
        var kit = new NetTestKit(Village(), "folk-gate");
        var (session, _) = await kit.EnterAsync(classId: 0, race: 0);
        Assert.True(kit.World.TryGetEntityById(FolkId, out var folk));
        int hp = folk.CurHp;

        Assert.Null(await kit.World.TrySelectTargetAsync(session, FolkId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(session, FolkId, CancellationToken.None));
        for (int i = 0; i < 8; i++)
        {
            kit.Now = kit.Now.AddMilliseconds(250);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        }

        Assert.Equal(hp, folk.CurHp);
        Assert.Equal(0, folk.AggroTargetId);
        Assert.Empty(folk.HateList);
    }

    [Fact]
    public async Task Hitting_guard_still_aggros_even_when_xml_attackable_is_false()
    {
        var kit = new NetTestKit(Village(), "folk-gate");
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        Assert.True(kit.World.TryGetEntityById(GuardId, out var guard));
        Assert.False(guard.Attackable);

        Assert.Null(await kit.World.TrySelectTargetAsync(session, GuardId, CancellationToken.None));
        Assert.Null(await kit.World.TryAttackAsync(session, GuardId, CancellationToken.None));
        for (int i = 0; i < 8; i++)
        {
            kit.Now = kit.Now.AddMilliseconds(250);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        }

        Assert.True(
            guard.CurHp < guard.MaxHp || guard.HateList.ContainsKey(player.ObjectId) || guard.AggroTargetId == player.ObjectId,
            "Guard must take damage or hate the attacker (Mobius Attackable), even with XML attackable=false");
    }
}
