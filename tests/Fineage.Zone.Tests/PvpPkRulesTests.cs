using Fineage.Game.Services;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Death, karma and service rules for PvP / PK, matched against L2J Mobius CT_0 Interlude
/// (<c>Player.doDie</c>, <c>calculateDeathExpPenalty</c>, <c>onDieDropItem</c>, <c>Formulas.calculateKarmaLost</c>,
/// <c>AttackableAI.autoAttackCondition</c> for guards, <c>TeleportHolder</c> karma gate).
/// </summary>
public sealed class PvpPkRulesTests
{
    const int X = 49000, Y = 50000, Z = -3000;
    const int ArenaX = -87920, ArenaY = 142220, ArenaZ = -2000;
    const int GuardObjectId = 9_400_101;

    static async Task<(NetTestKit Kit, ZoneEntity Killer, ZoneEntity Victim)> PairAsync(
        int x = X, int y = Y, int z = Z, int victimLevel = 20, DeathDropRules? drops = null, Random? rnd = null)
    {
        var kit = new NetTestKit(SpawnCatalog.LoadDefault(), "elven_starting", deathDrop: drops, deathRandom: rnd);
        var (_, killer) = await kit.EnterAsync(classId: 0, race: 0, x: x, y: y, z: z);
        var (_, victim) = await kit.EnterAsync(classId: 0, race: 0, x: x + 30, y: y, z: z);
        victim.Level = victimLevel;
        victim.Exp = kit.Game.Classes!.Experience.ExpToBeLevel(victimLevel) + 1000;
        return (kit, killer, victim);
    }

    [Fact]
    public void Karma_formulas_follow_mobius_ct0()
    {
        Assert.Equal(720, ZoneWorld.CalculateKarmaGain(0, isSummon: false));
        Assert.Equal(43200, ZoneWorld.CalculateKarmaGain(180, isSummon: false));
        // calculateKarmaLost: exp / RateKarmaLost(=RateXp) / levelMod(1) / 30, never more than held.
        Assert.Equal(40, ZoneWorld.CalculateKarmaLost(1000, 1200, rateXp: 1));
        Assert.Equal(20, ZoneWorld.CalculateKarmaLost(1000, 1200, rateXp: 2));
        Assert.Equal(5, ZoneWorld.CalculateKarmaLost(5, 1_000_000, rateXp: 1));
        // doDie tempfix: keep 3/4, under 200 → 0.
        Assert.Equal(750, ZoneWorld.KarmaAfterDeath(1000));
        Assert.Equal(0, ZoneWorld.KarmaAfterDeath(199));
    }

    [Fact]
    public async Task A_pk_victim_loses_exp_outside_the_arena()
    {
        var (kit, killer, victim) = await PairAsync();
        long before = victim.Exp;

        kit.World.KillPlayerByNpcForTest(victim, killer);

        Assert.True(victim.Dead);
        Assert.True(victim.Exp < before, "Mobius doDie: calculateDeathExpPenalty runs for player kills outside PVP zones");
        Assert.Equal(1, killer.PkKills);
    }

    [Fact]
    public async Task Arena_death_costs_nothing_and_counts_nothing()
    {
        var (kit, killer, victim) = await PairAsync(ArenaX, ArenaY, ArenaZ);
        Assert.True(kit.World.InPvpArea(victim));
        long before = victim.Exp;

        kit.World.KillPlayerByNpcForTest(victim, killer);

        Assert.True(victim.Dead);
        Assert.Equal(before, victim.Exp);
        Assert.Equal(0, killer.PkKills);
        Assert.Equal(0, killer.PvpKills);
        Assert.Equal(0, killer.Karma);
    }

    [Fact]
    public async Task Arena_swings_do_not_flag()
    {
        var (kit, attacker, victim) = await PairAsync(ArenaX, ArenaY, ArenaZ);
        kit.World.UpdatePvPStatusForTest(attacker, victim);
        Assert.False(attacker.PvpFlagged);
    }

    [Fact]
    public async Task Hitting_a_karma_player_never_flags()
    {
        var (kit, attacker, victim) = await PairAsync();
        victim.Karma = 720;
        kit.World.UpdatePvPStatusForTest(attacker, victim);
        Assert.False(attacker.PvpFlagged);
    }

    [Fact]
    public async Task A_dying_pk_keeps_three_quarters_of_its_karma_and_drops_by_karma_rates_even_equipped()
    {
        var rules = DeathDropRules.Parse("""
            {
              "playerDropLimit": 0, "playerRateDrop": 0, "playerRateDropItem": 0,
              "playerRateDropEquip": 0, "playerRateDropEquipWeapon": 0,
              "karmaDropLimit": 100, "karmaRateDrop": 100, "karmaRateDropItem": 100,
              "karmaRateDropEquip": 100, "karmaRateDropEquipWeapon": 100,
              "karmaPkLimit": 5, "nonDroppableItemIds": [57]
            }
            """);
        var (kit, killer, pk) = await PairAsync(drops: rules, rnd: new AlwaysLow());
        pk.Karma = 1000;
        pk.PkKills = 5;
        Assert.Null(await kit.World.TryGiveItemAsync(pk.Session!, 1864, 2, default));

        kit.World.KillPlayerByNpcForTest(pk, killer);

        Assert.Equal(750, pk.Karma);
        Assert.Equal(0, pk.Inventory!.CountOf(1864));
        Assert.True(kit.World.GroundItemCountForTest() >= 1);
        Assert.Equal(0, killer.PkKills); // killing a karma player is neither PvP nor PK (AwardPKKillPVPPoint=false)
        Assert.Equal(0, killer.Karma);
        Assert.Equal(750, kit.Store.FindById(pk.Session!.Account, pk.ObjectId)!.Karma);
    }

    [Fact]
    public async Task Guards_aggro_karma_players_only()
    {
        var map = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["guards"] = new("guards", true,
            [
                new SpawnNpcDefinition(
                    GuardObjectId, 30041, "Arnold", X + 200, Y, Z,
                    0, false, 3290, 1f, 1f, 60, 0, "Guard", "Guard",
                    Level: 80, AttackRange: 40, AttackSpeed: 253, Aggressive: true, AggroRange: 1000, ClanHelpRange: 300, Clans: []),
            ])
        });
        var kit = new NetTestKit(map, "guards");
        var (_, white) = await kit.EnterAsync(classId: 0, race: 0, x: X, y: Y, z: Z);
        Assert.True(kit.World.TryGetEntityById(GuardObjectId, out var guard));

        for (int i = 0; i < 20; i++)
        {
            kit.Now = kit.Now.AddMilliseconds(1000);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        }
        Assert.Equal(0, guard.AggroTargetId);

        white.Karma = 720;
        for (int i = 0; i < 5 && guard.AggroTargetId == 0; i++)
        {
            kit.Now = kit.Now.AddMilliseconds(1000);
            await kit.World.RunPlayerCombatTickAsync(CancellationToken.None);
        }
        Assert.Equal(white.ObjectId, guard.AggroTargetId);
    }

    [Fact]
    public async Task Gatekeeper_turns_away_karma_players()
    {
        var kit = new NetTestKit(SpawnCatalog.LoadDefault(), "elven_starting");
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        var (session, player) = await kit.EnterAsync(classId: 0, race: 0);
        player.Karma = 1;

        Assert.Null(kit.World.NpcServiceForTest(session, 30146, NpcServiceIntent.TeleportList));
        Assert.Contains(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            c => c[1] is SystemMessageMsg m && m.Id == NpcServiceProtocol.KarmaTeleportRefused);
    }

    [Fact]
    public void Attack_request_without_the_forced_byte_decodes_as_legacy()
    {
        var legacy = new byte[] { 7, 0, 0, 0 };
        Assert.Null(NetCodec.DecodeAttackRequest(legacy).Forced);
        var plain = NetCodec.DecodeAttackRequest(NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = 7, Forced = false }));
        Assert.Equal(7, plain.TargetObjectId);
        Assert.False(plain.Forced);
        Assert.True(NetCodec.DecodeAttackRequest(NetCodec.EncodeAttackRequest(new AttackRequestNetMsg { TargetObjectId = 7, Forced = true })).Forced);
    }

    [Fact]
    public void User_info_carries_pvp_and_pk_counts_and_old_payloads_still_decode()
    {
        var full = NetCodec.EncodeUserInfo(new UserInfoNetMsg { Name = "a", Karma = 720, PvpFlag = 2, PvpKills = 3, PkKills = 1 });
        var back = NetCodec.DecodeUserInfo(full);
        Assert.Equal((720, (byte)2, 3, 1), (back.Karma, back.PvpFlag, back.PvpKills, back.PkKills));

        // Strip the Block C clan tail (title u16 + 7 ints + leader byte) and the pvp/pk counts.
        const int clanTail = 2 + 7 * 4 + 1 + 2 + 2 * 4 + 2 + 1; // + G1 noble/hero bytes, base class / class index, empty cubic list, duel team
        var old = NetCodec.DecodeUserInfo(full.AsSpan(0, full.Length - 8 - clanTail));
        Assert.Equal((720, (byte)2, 0, 0), (old.Karma, old.PvpFlag, old.PvpKills, old.PkKills));
    }

    sealed class AlwaysLow : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }
}
