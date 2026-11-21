using System.Text.Json;
using Fineage.Game;
using Fineage.Game.Skills;
using Xunit;

namespace Fineage.Game.Tests;

public sealed class SkillCastConditionTests
{
    sealed class TestCtx : SkillCastConditionContext
    {
        public string[]? Kinds;
        public int Used;
        public int Limit = 80;
        public int LoadPct = 50;
        public string TargetRaceName = "";
        public override void LogUnsupported(string message) { }
        public override bool UsingKinds(string[] kinds) => Kinds != null && kinds.Any(k => Kinds.Contains(k));
        public override bool UsingSlot(string[] slots) => false;
        public override int InventoryUsedSlots => Used;
        public override int InventoryLimit => Limit;
        public override int LoadPercent => LoadPct;
        public override int HpPercent => 100;
        public override int MpPercent => 100;
        public override int SoulCharges => 0;
        public override int Level => 20;
        public override int Sex => 0;
        public override string RaceName => "HUMAN";
        public override int ElementSeedTotal => 0;
        public override int ElementSeedCount(string element) => 0;
        public override bool HasActiveEffect(int skillId, int level) => false;
        public override bool HasActiveSkill(int skillId, int level) => false;
        public override bool InsideZone(int zoneId) => false;
        public override bool CanSweep() => false;
        public override bool CanSummon() => false;
        public override bool HasSummon => false;
        public override bool CanSummonSiegeGolem() => false;
        public override bool CanCreateSiegeBase() => false;
        public override bool CanTakeCastle() => false;
        public override bool CanEscape() => false;
        public override bool CanResurrect() => false;
        public override bool CanCallPc() => false;
        public override bool InSiegeZone() => false;
        public override bool RidingStrider => false;
        public override bool Flying => false;
        public override bool Resting => false;
        public override bool Moving => false;
        public override bool Running => false;
        public override bool Standing => true;
        public override bool Chaotic => false;
        public override int ClanHallId => 0;
        public override bool BehindTarget => false;
        public override bool FrontOfTarget => false;
        public override string TargetRace => TargetRaceName;
        public override int TargetNpcId => 0;
        public override string TargetNpcType => "";
        public override bool TargetIsPartyMateExceptSelf => false;
        public override bool TargetHasActiveSkill(int skillId) => false;
        public override bool IsNight => false;
    }

    [Fact]
    public void And_requires_both_using_and_invSize()
    {
        var json = """
        {"op":"and","items":[
          {"op":"using","kinds":["DAGGER"]},
          {"op":"player","invsize":10}
        ]}
        """;
        var cond = SkillCastCondition.Parse(JsonDocument.Parse(json).RootElement);
        var ctx = new TestCtx { Kinds = ["DAGGER"], Used = 65, Limit = 80 };
        Assert.True(SkillCastConditionEvaluator.Met(cond, ctx));
        ctx.Used = 71;
        Assert.False(SkillCastConditionEvaluator.Met(cond, ctx));
    }

    [Fact]
    public void Target_race_undead_gate()
    {
        var json = """{"op":"target","race":"UNDEAD"}""";
        var cond = SkillCastCondition.Parse(JsonDocument.Parse(json).RootElement);
        var ctx = new TestCtx { TargetRaceName = "UNDEAD" };
        Assert.True(SkillCastConditionEvaluator.Met(cond, ctx));
        ctx.TargetRaceName = "HUMANOID";
        Assert.False(SkillCastConditionEvaluator.Met(cond, ctx));
    }

    [Fact]
    public void Catalog_loads_castCondition_from_skills_json()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data", "skills.json"));
        Assert.True(File.Exists(path), path);
        var cat = SkillCatalog.Load(path);
        var strike = cat.Find(49);
        Assert.NotNull(strike);
        Assert.NotNull(strike!.CastCondition);
    }
}