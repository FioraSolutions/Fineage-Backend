using Fineage.Accounts;
using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Siege;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius gaps that are unconditional: dual weapons roll two half hits, a fishing rod cannot swing,
/// and a siege polygon is PVP only while that siege is running. Same-side clans cannot hit each other then.
/// </summary>
public sealed class MobiusAttackGapTests
{
    const int MobId = 9_400_011;
    const int DualSword = 2516;
    const int FishingRod = 6529;

    // Interior of gludio_castle_battlefield_territory (castle 1).
    const int GludioX = -17952;
    const int GludioY = 110730;

    [Fact]
    public void Gludio_battlefield_is_a_siege_zone_and_not_an_arena()
    {
        var sieges = SiegeZones.LoadDefault();
        Assert.Equal(9, sieges.Count);
        Assert.Equal(1, sieges.CastleAt(GludioX, GludioY, 0));
        Assert.Equal(0, sieges.CastleAt(0, 0, 0));
        Assert.False(PvpZones.LoadDefault().Contains(GludioX, GludioY, 0));
    }

    [Fact]
    public async Task Dual_weapon_lands_two_half_hits()
    {
        var (kit, session, _, mob, rec) = await MobAsync();
        var bag = kit.World.TryGetLivePlayer(session, out var player) ? player.Inventory! : throw new InvalidOperationException();
        bag.Equip(bag.Add(DualSword));
        Assert.Equal(WeaponType.Dual, bag.GetEquipped(PaperdollSlot.RHand)!.Template.WeaponType);
        int hp = mob.CurHp;

        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));
        Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
        Assert.Equal(hp, mob.CurHp);

        long atk = ZoneCombat.AttackTimeMs(kit.World.PlayerStats(player).PAtkSpd);
        kit.Now = kit.Now.AddMilliseconds(atk / 4 + 1);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        int afterFirst = mob.CurHp;
        Assert.True(afterFirst < hp);

        kit.Now = kit.Now.AddMilliseconds(atk / 2);
        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.True(mob.CurHp < afterFirst);
        Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
    }

    [Fact]
    public async Task Fishing_rod_cannot_swing()
    {
        var (kit, session, player, mob, rec) = await MobAsync();
        var bag = player.Inventory!;
        bag.Equip(bag.Add(FishingRod));
        int hp = mob.CurHp;

        Assert.Null(await kit.World.TryAttackAsync(session, MobId, CancellationToken.None));

        Assert.Equal(0, session.AutoAttackTargetId);
        Assert.Equal(hp, mob.CurHp);
        Assert.Empty(rec.Of(nameof(IWorldEventPublisher.FanoutAttackState)));
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgFishingPole);
    }

    [Fact]
    public async Task Siege_allies_cannot_hit_each_other_and_the_field_is_pvp_only_while_the_siege_runs()
    {
        var kit = new NetTestKit();
        var leader = kit.Create(classId: 0, race: 0, level: 40, x: GludioX, y: GludioY, z: 0);
        var ally = kit.Create(classId: 0, race: 0, level: 40, x: GludioX + 40, y: GludioY, z: 0);
        var clan = kit.Store.InsertClan(
            new ClanRecord(
                0, "Gludio", 4, leader.CharacterId, 0, 0, 0,
                [
                    new ClanMemberRecord(leader.CharacterId, leader.Account, leader.Name, 0, 40, 0),
                    new ClanMemberRecord(ally.CharacterId, ally.Account, ally.Name, 0, 40, 0)
                ]),
            new CharacterClanState(leader.Account, leader.CharacterId, 0, 0, 0));
        Assert.NotNull(clan);
        Assert.True(kit.World.ReloadClansForTests());

        var (session, attacker) = await kit.EnterAsync(leader);
        var (other, victim) = await kit.EnterAsync(ally);
        Assert.Equal(clan!.Id, attacker.ClanId);
        Assert.Equal(clan.Id, victim.ClanId);
        victim.PvpFlagged = true;
        victim.MaxHp = 500;
        victim.CurHp = 500;

        var castle = kit.World.FindCastle(1)!;
        Assert.Equal(0, kit.World.ActiveSiegeCastle(GludioX, GludioY, 0));
        Assert.Null(await kit.World.TryAttackAsync(session, other.ObjectId, CancellationToken.None));
        Assert.Equal(other.ObjectId, session.AutoAttackTargetId);
        session.AutoAttackTargetId = 0;

        castle.OwnerClanId = clan.Id;
        castle.InProgress = true;
        Assert.Equal(1, kit.World.ActiveSiegeCastle(GludioX, GludioY, 0));

        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TryAttackAsync(session, other.ObjectId, CancellationToken.None));
        Assert.Equal(0, session.AutoAttackTargetId);
        Assert.Equal(500, victim.CurHp);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgSiegeFriend);

        await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        Assert.True(attacker.InArena);
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgEnteredCombatZone);
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, ZoneEntity Mob, NetBridgeRecorder Rec)> MobAsync()
    {
        var kit = new NetTestKit(SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["gap"] = new("gap", true,
            [
                new SpawnNpcDefinition(MobId, 90000 + 11, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ, 0, true, 50_000, 1f, 1f, 60, 0, "", "Monster",
                    Level: 1, AttackRange: 40, AttackSpeed: 253, Aggressive: false, AggroRange: 0, ClanHelpRange: 0, Clans: [], Evasion: 0)
            ])
        }), "gap", combatRandom: new Random(1));
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0, level: 40);
        Assert.True(kit.World.TryGetLivePlayer(session, out _));
        Assert.True(kit.World.TryGetEntityById(MobId, out var mob));
        mob.MaxHp = 50_000;
        mob.CurHp = 50_000;
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobId, CancellationToken.None));
        return (kit, session, player, mob, rec);
    }
}
