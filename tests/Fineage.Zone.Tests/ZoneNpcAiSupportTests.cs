using Fineage.Protocol;
using Fineage.Zone.Npcs;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius AISkillScope HEAL / BUFF / DEBUFF — second #259 slice.</summary>
public sealed class ZoneNpcAiSupportTests
{
    [Fact]
    public void Catalog_loads_beast_seer_heal()
    {
        var row = NpcAiSupportCatalog.LoadDefault().Get(21060);
        Assert.NotNull(row);
        Assert.NotNull(row!.Value.Heal);
        Assert.Contains(row.Value.Heal!, s => s.SkillId == 4065 && s.Level == 5 && s.Amount == 494);
    }

    [Fact]
    public void Catalog_loads_scavenger_bat_debuff()
    {
        var row = NpcAiSupportCatalog.LoadDefault().Get(20411);
        Assert.NotNull(row);
        Assert.Contains(row!.Value.Debuff!, s => s.SkillId == 4038);
    }

    [Fact]
    public void Catalog_loads_dismal_pole_buff()
    {
        var row = NpcAiSupportCatalog.LoadDefault().Get(21004);
        Assert.Contains(row!.Value.Buff!, s => s.SkillId == 4028 && s.Level == 3);
    }

    [Fact]
    public async Task Npc_heal_skill_restores_hp()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        mob.AiHealSkills =
        [
            new NpcAiSupportSkill(4065, 5, HitTimeMs: 0, ReuseDelayMs: 8000, Mp: 64, MpInitial: 16, Amount: 494)
        ];
        mob.CurHp = Math.Max(1, mob.MaxHp / 4);
        mob.CurMp = mob.MaxMp;
        int before = mob.CurHp;

        Assert.True(kit.World.CastNpcSupportForTest(mob, mob, mob.AiHealSkills![0], "heal"));
        Assert.True(mob.CurHp > before);
        Assert.True(mob.CurHp <= mob.MaxHp);
    }

    [Fact]
    public async Task Npc_self_buff_applies_abnormal()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var skill = new NpcAiSupportSkill(4028, 3, HitTimeMs: 0, ReuseDelayMs: 8000, Mp: 33, MpInitial: 0,
            AbnormalTimeSeconds: 300, AbnormalLevel: 3);
        mob.AiBuffSkills = [skill];
        mob.CurMp = mob.MaxMp;

        Assert.True(kit.World.CastNpcSupportForTest(mob, mob, skill, "buff"));
        Assert.Contains(mob.Abnormals, a => a.Id == 4028);
    }

    [Fact]
    public async Task Npc_debuff_applies_to_player()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (_, player) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        var skill = new NpcAiSupportSkill(4038, 2, HitTimeMs: 0, ReuseDelayMs: 8000, Mp: 20, MpInitial: 0,
            AbnormalTimeSeconds: 30, AbnormalLevel: 2);
        mob.AiDebuffSkills = [skill];
        mob.CurMp = mob.MaxMp;

        Assert.True(kit.World.CastNpcSupportForTest(mob, player, skill, "debuff"));
        Assert.Contains(player.Abnormals, a => a.Id == 4038);
    }

    [Fact]
    public void Catalog_loads_grave_lich_sleep()
    {
        var row = NpcAiSupportCatalog.LoadDefault().Get(21003);
        Assert.Contains(row!.Value.Immobilize!, s => s.SkillId == 4046 && s.Slot == "SLEEP");
    }

    [Fact]
    public async Task Npc_sleep_blocks_player_move_and_skills()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mob = kit.World.AttackableForTest()!;
        var (session, player) = await kit.EnterAsync(classId: 54, race: 4, x: mob.X + 10, y: mob.Y, z: mob.Z);
        player.KnownSkills[4032] = 1;
        var skill = new NpcAiSupportSkill(4046, 5, HitTimeMs: 0, ReuseDelayMs: 8000, Mp: 45, MpInitial: 0,
            AbnormalTimeSeconds: 30, AbnormalLevel: 1, Slot: "SLEEP");
        mob.CurMp = mob.MaxMp;

        Assert.True(kit.World.CastNpcSupportForTest(mob, player, skill, "immobilize"));
        Assert.True(ZoneWorld.IsImmobilized(player));
        Assert.Equal(ActionProtocol.Immobilized,
            await kit.World.TryMoveAsync(session, new MoveToMsg { DestX = player.X + 200, DestY = player.Y, DestZ = player.Z }, default));
        Assert.Equal(SkillProtocol.Muted,
            await kit.World.TrySkillUseAsync(session, new SkillUseRequestMsg { SkillId = 4032, TargetObjectId = mob.ObjectId }, default));
    }

    [Fact]
    public async Task Npc_heal_can_restore_another_npc()
    {
        var kit = new NetTestKit(Fineage.Zone.Spawns.SpawnCatalog.LoadDefault(), "elven_starting");
        var mobs = kit.World.AttackablesForTest(2);
        Assert.True(mobs.Count >= 2);
        var healer = mobs[0];
        var mate = mobs[1];
        mate.X = healer.X + 50;
        mate.Y = healer.Y;
        mate.Z = healer.Z;
        mate.CurHp = Math.Max(1, mate.MaxHp / 5);
        healer.CurMp = healer.MaxMp;
        var skill = new NpcAiSupportSkill(4065, 5, HitTimeMs: 0, ReuseDelayMs: 8000, Mp: 64, MpInitial: 16, Amount: 494);

        int before = mate.CurHp;
        Assert.True(kit.World.CastNpcSupportForTest(healer, mate, skill, "heal"));
        Assert.True(mate.CurHp > before);
    }
}
