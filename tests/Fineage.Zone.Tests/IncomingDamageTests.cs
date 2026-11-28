using Fineage.Game;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Goldens for Mobius CT0 <c>PlayerStatus.reduceHp</c> redirects (Transfer Pain, Arcane Shield,
/// transfer to a party caster) and the matching <c>SummonStatus.reduceHp</c> branch. Arithmetic is
/// the Java integer casts, not a hand-entered table.
/// </summary>
public sealed class IncomingDamagePlanTests
{
    [Fact]
    public void Transfer_pain_sends_the_percent_to_the_servitor_and_leaves_the_rest()
    {
        // Transfer Pain lv5 is +50 transDam. 100 incoming, servitor has 500 HP: 50 goes to the pet, 50 remains.
        var plan = IncomingDamage.PlanPlayer(100, transDamPercent: 50, servitorHp: 500, 0, 0, 0, 0, 0);
        Assert.Equal(new IncomingDamagePlan(50, 0, false, false, 0, 0, 50), plan);
    }

    [Fact]
    public void Transfer_pain_never_kills_the_servitor()
    {
        var plan = IncomingDamage.PlanPlayer(100, transDamPercent: 50, servitorHp: 20, 0, 0, 0, 0, 0);
        Assert.Equal(19, plan.ToServitor);
        Assert.Equal(81, plan.Remaining);
    }

    [Fact]
    public void Arcane_shield_70_pays_30_percent_as_mp_and_absorbs_the_hit()
    {
        // manaShield add 70 (skill 1556). 100 incoming → raw 70, cost 30 MP, shield holds, no CP/HP, no party transfer.
        var plan = IncomingDamage.PlanPlayer(100, 0, 0, manaShieldPercent: 70, currentMp: 200, 0, 0, 0);
        Assert.True(plan.ShieldAbsorbed);
        Assert.False(plan.ShieldBroken);
        Assert.Equal(30, plan.MpLoss);
        Assert.Equal(0, plan.Remaining);
        Assert.Equal(0, plan.ToPartyCaster);
    }

    [Fact]
    public void Arcane_shield_breaks_when_mp_cannot_cover_the_remainder()
    {
        var plan = IncomingDamage.PlanPlayer(100, 0, 0, manaShieldPercent: 70, currentMp: 10, 0, 0, 0);
        Assert.False(plan.ShieldAbsorbed);
        Assert.True(plan.ShieldBroken);
        Assert.Equal(10, plan.MpLoss);
        Assert.Equal(20, plan.Remaining);
    }

    [Fact]
    public void Transfer_to_party_caster_splits_by_members_in_range()
    {
        // 100 incoming, 50% to caster with 500 HP, 2 other members in range: share 25, remaining 50.
        var plan = IncomingDamage.PlanPlayer(100, 0, 0, 0, 0, transDamToPlayerPercent: 50, casterHp: 500, membersInRange: 2);
        Assert.Equal(50, plan.ToPartyCaster);
        Assert.Equal(25, plan.PartyShare);
        Assert.Equal(50, plan.Remaining);
    }

    [Fact]
    public void Transfer_to_party_caster_does_not_deduct_from_the_target_without_members_in_range()
    {
        // Mobius still computes transferDmg and may reduceCp, but skips reduceCurrentHp and amount -=.
        var plan = IncomingDamage.PlanPlayer(100, 0, 0, 0, 0, 50, 500, membersInRange: 0, casterCp: 400);
        Assert.Equal(50, plan.ToPartyCaster);
        Assert.Equal(50, plan.CpOnCaster);
        Assert.Equal(0, plan.PartyShare);
        Assert.Equal(100, plan.Remaining);
    }

    [Fact]
    public void Transfer_to_party_caster_mutates_the_share_when_cp_does_not_cover_it()
    {
        // 50 raw, caster CP 20, 2 members: transferDmg becomes 30, share 15, remaining 70.
        var plan = IncomingDamage.PlanPlayer(100, 0, 0, 0, 0, 50, 500, membersInRange: 2, casterCp: 20);
        Assert.Equal(50, plan.ToPartyCaster);
        Assert.Equal(20, plan.CpOnCaster);
        Assert.Equal(15, plan.PartyShare);
        Assert.Equal(70, plan.Remaining);
    }

    [Fact]
    public void Npc_attacker_does_not_spend_caster_cp_before_the_share()
    {
        var plan = IncomingDamage.PlanPlayer(100, 0, 0, 0, 0, 50, 500, membersInRange: 2, casterCp: 400, absorbCp: false);
        Assert.Equal(0, plan.CpOnCaster);
        Assert.Equal(25, plan.PartyShare);
        Assert.Equal(50, plan.Remaining);
    }

    [Fact]
    public void Absorbing_shield_skips_the_party_transfer()
    {
        var plan = IncomingDamage.PlanPlayer(100, 0, 0, 70, 200, transDamToPlayerPercent: 50, casterHp: 500, membersInRange: 1);
        Assert.True(plan.ShieldAbsorbed);
        Assert.Equal(0, plan.ToPartyCaster);
        Assert.Equal(0, plan.Remaining);
        Assert.Equal(30, plan.MpLoss);
    }

    [Fact]
    public void Transfer_pain_runs_before_arcane_shield()
    {
        // 100 incoming, 50% to servitor (50 left), then 70% shield pays 15 MP and absorbs the rest.
        var plan = IncomingDamage.PlanPlayer(100, transDamPercent: 50, servitorHp: 500, manaShieldPercent: 70, currentMp: 200, 0, 0, 0);
        Assert.Equal(50, plan.ToServitor);
        Assert.True(plan.ShieldAbsorbed);
        Assert.Equal(15, plan.MpLoss);
        Assert.Equal(0, plan.Remaining);
    }

    [Fact]
    public void Summon_without_a_party_can_transfer_the_whole_share_to_the_owner()
    {
        var plan = IncomingDamage.PlanSummon(80, transDamToPlayerPercent: 50, casterHp: 400, membersInRange: 0, partyPresent: false);
        Assert.Equal(40, plan.ToPartyCaster);
        Assert.Equal(40, plan.PartyShare);
        Assert.Equal(40, plan.Remaining);
    }

    [Fact]
    public void Summon_in_a_party_requires_members_in_range_for_the_hp_share()
    {
        var blocked = IncomingDamage.PlanSummon(80, 50, 400, membersInRange: 0, partyPresent: true, casterCp: 200);
        Assert.Equal(40, blocked.ToPartyCaster);
        Assert.Equal(40, blocked.CpOnCaster);
        Assert.Equal(0, blocked.PartyShare);
        Assert.Equal(80, blocked.Remaining);

        var ok = IncomingDamage.PlanSummon(80, 50, 400, membersInRange: 2, partyPresent: true, casterCp: 200);
        Assert.Equal(40, ok.ToPartyCaster);
        Assert.Equal(40, ok.CpOnCaster);
        Assert.Equal(20, ok.PartyShare);
        Assert.Equal(40, ok.Remaining);
    }

    [Fact]
    public void Summon_without_a_party_mutates_the_owner_share_when_cp_is_short()
    {
        var plan = IncomingDamage.PlanSummon(80, 50, 400, membersInRange: 0, partyPresent: false, casterCp: 10);
        Assert.Equal(40, plan.ToPartyCaster);
        Assert.Equal(10, plan.CpOnCaster);
        Assert.Equal(30, plan.PartyShare);
        Assert.Equal(50, plan.Remaining);
    }
}

public sealed class IncomingDamageWorldTests
{
    const int SummonKat = 1111;

    static async Task<(NetTestKit Kit, ZoneEntity Attacker, ZoneEntity Target)> SetupAsync()
    {
        var kit = new NetTestKit();
        var (_, attacker) = await kit.EnterAsync(classId: 0, level: 40, x: NetTestKit.ElfX - 100);
        var (_, target) = await kit.EnterAsync(classId: 25, level: 40);
        target.MaxHp = 1_000;
        target.CurHp = 1_000;
        target.MaxCp = 200;
        target.CurCp = 200;
        target.MaxMp = 400;
        target.CurMp = 400;
        return (kit, attacker, target);
    }

    static void ApplyBuff(ZoneEntity target, int skillId, int skillLevel, int abnormalLevel = 1)
    {
        Assert.True(ZoneAbnormals.Apply(target.Abnormals, skillId, abnormalLevel, durationSeconds: 600, nowUnixMs: 0, skillLevel: skillLevel));
    }

    [Fact]
    public async Task Transfer_pain_diverts_half_the_hit_to_the_servitor()
    {
        var (kit, attacker, target) = await SetupAsync();
        var servitor = kit.World.SpawnServitorForTest(target, SummonKat, 1);
        Assert.NotNull(servitor);
        servitor!.MaxHp = servitor.CurHp = 500;
        ApplyBuff(target, skillId: 1262, skillLevel: 5);

        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker, 100);
        Assert.Equal(450, servitor.CurHp);
        Assert.Equal(new DamageSplit(50, 0), dealt);
        Assert.Equal(150, target.CurCp);
        Assert.Equal(1_000, target.CurHp);
    }

    [Fact]
    public async Task Arcane_shield_spends_mp_and_blocks_cp_and_hp()
    {
        var (kit, attacker, target) = await SetupAsync();
        target.TestCombatFuncs = [new SkillStatFunc(CombatStat.ManaShield, 'a', [70])];
        ApplyBuff(target, skillId: 1556, skillLevel: 1);
        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker, 100);

        Assert.Equal(DamageSplit.None, dealt);
        Assert.Equal(200, target.CurCp);
        Assert.Equal(1_000, target.CurHp);
        Assert.Equal(370, target.CurMp); // 400 - 30
        Assert.Contains(target.Abnormals, a => a.Id == 1556);
    }

    [Fact]
    public async Task Arcane_shield_expires_when_mp_runs_out_and_the_remainder_hits_cp()
    {
        var (kit, attacker, target) = await SetupAsync();
        target.CurMp = 10;
        target.TestCombatFuncs = [new SkillStatFunc(CombatStat.ManaShield, 'a', [70])];
        ApplyBuff(target, skillId: 1556, skillLevel: 1);
        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker, 100);

        Assert.Equal(0, target.CurMp);
        Assert.DoesNotContain(target.Abnormals, a => a.Id == 1556);
        Assert.Equal(new DamageSplit(20, 0), dealt);
        Assert.Equal(180, target.CurCp);
    }

    [Fact]
    public async Task Transfer_to_a_party_caster_spends_the_casters_cp_first()
    {
        var kit = new NetTestKit();
        var (leaderSession, caster) = await kit.EnterAsync(classId: 0, level: 40, x: NetTestKit.ElfX);
        var (memberSession, target) = await kit.EnterAsync(classId: 25, level: 40, x: NetTestKit.ElfX + 50);
        var (_, attacker) = await kit.EnterAsync(classId: 0, level: 40, x: NetTestKit.ElfX - 100);
        Assert.Null(await kit.World.TryPartyInviteAsync(leaderSession, target.ObjectId, 0, default));
        Assert.Null(await kit.World.TryPartyAnswerAsync(memberSession, leaderSession.ObjectId, 1, default));

        caster.MaxHp = caster.CurHp = 1_000;
        caster.MaxCp = caster.CurCp = 400;
        target.MaxHp = target.CurHp = 1_000;
        target.MaxCp = target.CurCp = 200;
        target.TransferDamageToObjectId = caster.ObjectId;
        target.TestCombatFuncs = [new SkillStatFunc(CombatStat.TransDamToPlayer, 'a', [50])];

        // 100 incoming, 50% = 50 to caster. membersInRange includes the target (1). Share 50.
        // Playable attacker: caster first spends 50 CP via reduceCp, then reduceCurrentHp(50) spends another 50 CP.
        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker, 100);
        Assert.Equal(new DamageSplit(50, 0), dealt);
        Assert.Equal(150, target.CurCp);
        Assert.Equal(1_000, target.CurHp);
        Assert.Equal(300, caster.CurCp);
        Assert.Equal(1_000, caster.CurHp);
    }

    [Fact]
    public async Task Transfer_to_a_party_caster_short_on_cp_mutates_the_share()
    {
        var kit = new NetTestKit();
        var (leaderSession, caster) = await kit.EnterAsync(classId: 0, level: 40, x: NetTestKit.ElfX);
        var (memberSession, target) = await kit.EnterAsync(classId: 25, level: 40, x: NetTestKit.ElfX + 50);
        var (_, attacker) = await kit.EnterAsync(classId: 0, level: 40, x: NetTestKit.ElfX - 100);
        Assert.Null(await kit.World.TryPartyInviteAsync(leaderSession, target.ObjectId, 0, default));
        Assert.Null(await kit.World.TryPartyAnswerAsync(memberSession, leaderSession.ObjectId, 1, default));

        caster.MaxHp = caster.CurHp = 1_000;
        caster.MaxCp = caster.CurCp = 20;
        target.MaxHp = target.CurHp = 1_000;
        target.MaxCp = target.CurCp = 200;
        target.TransferDamageToObjectId = caster.ObjectId;
        target.TestCombatFuncs = [new SkillStatFunc(CombatStat.TransDamToPlayer, 'a', [50])];

        // Raw 50, CP 20 → transferDmg 30. membersInRange=1 so share 30.
        // Target remaining 70 (all CP). Caster: reduceCp(20) then reduceCurrentHp(30) spends the last 0 CP + 30 HP.
        var dealt = kit.World.ApplyTargetDamageForTest(target, attacker, 100);
        Assert.Equal(new DamageSplit(70, 0), dealt);
        Assert.Equal(130, target.CurCp);
        Assert.Equal(1_000, target.CurHp);
        Assert.Equal(0, caster.CurCp);
        Assert.Equal(970, caster.CurHp);
    }

    [Fact]
    public async Task Summon_without_a_party_transfers_its_share_to_the_owner()
    {
        var (kit, attacker, owner) = await SetupAsync();
        var servitor = kit.World.SpawnServitorForTest(owner, SummonKat, 1);
        Assert.NotNull(servitor);
        servitor!.MaxHp = servitor.CurHp = 500;
        servitor.TransferDamageToObjectId = owner.ObjectId;
        servitor.TestCombatFuncs = [new SkillStatFunc(CombatStat.TransDamToPlayer, 'a', [50])];
        owner.MaxHp = owner.CurHp = 1_000;
        owner.MaxCp = owner.CurCp = 400;

        // 80 incoming, 50% = 40. reduceCp(40) then reduceCurrentHp(40) → owner CP 320. Servitor keeps 40.
        var dealt = kit.World.ApplyTargetDamageForTest(servitor, attacker, 80);
        Assert.Equal(new DamageSplit(0, 40), dealt);
        Assert.Equal(460, servitor.CurHp);
        Assert.Equal(320, owner.CurCp);
        Assert.Equal(1_000, owner.CurHp);
    }
}
