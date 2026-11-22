using Fineage.Game.Economy;
using Fineage.Protocol;
using Fineage.Zone.Parity;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

[Trait("Category", "Integration")]
public sealed class NetSkillEnchantTests
{
    sealed class EnvScope : IDisposable
    {
        readonly List<(string Key, string? Prior)> _priors = [];

        public static EnvScope Set(string key, string value)
        {
            var scope = new EnvScope();
            scope._priors.Add((key, Environment.GetEnvironmentVariable(key)));
            Environment.SetEnvironmentVariable(key, value);
            return scope;
        }

        public void Dispose()
        {
            foreach (var (key, prior) in _priors)
                Environment.SetEnvironmentVariable(key, prior);
        }
    }

    static async Task<(NetTestKit Kit, ZoneSession Session, ZoneEntity Player, NetBridgeRecorder Rec, EnvScope Env)> SetupAsync()
    {
        var env = EnvScope.Set(ClientHeavyFeatures.SkillEnchantVariable, "1");
        var kit = new NetTestKit(combatRandom: new Random(0));
        var (session, player) = await kit.EnterAsync(classId: 88, level: 76);
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        player.KnownSkills[1] = 37;
        player.LearnedSkills[1] = 37;
        player.Sp = 10_000_000;
        player.Exp = 2_000_000_000;
        return (kit, session, player, rec, env);
    }

    [Fact]
    public async Task Enchant_info_fanout_matches_route_costs()
    {
        var (kit, session, _, rec, env) = await SetupAsync();
        using (env)
        {
            Assert.Null(kit.World.EnchantSkillForTest(session, skillId: 1, skillLevel: 101, informing: true));
            var info = Assert.IsType<EnchantSkillInfoMsg>(
                Assert.Single(rec.Of(nameof(IWorldEventPublisher.FanoutEnchantSkillInfo)))[1]);
            Assert.Equal(1, info.SkillId);
            Assert.Equal(101, info.Level);
            Assert.Equal(306_000, info.SpCost);
            Assert.True(info.Rate > 0);
        }
    }

    [Fact]
    public async Task Enchant_spends_sp_on_ledger_and_bumps_known_level()
    {
        var (kit, session, player, rec, env) = await SetupAsync();
        using (env)
        {
            player.KnownSkills[1] = 101;
            player.LearnedSkills[1] = 101;
            long spBefore = player.Sp;
            var route = kit.Game.SkillEnchants.Find(1, 102)!;

            Assert.Null(kit.World.EnchantSkillForTest(session, skillId: 1, skillLevel: 102, informing: false));

            Assert.Equal(spBefore - route.Sp, player.Sp);
            Assert.Equal(102, player.KnownSkills[1]);
            Assert.NotEmpty(rec.Of(nameof(IWorldEventPublisher.FanoutSkillList)));
            var progress = LedgerAccount.Progress(player.ObjectId);
            Assert.Equal(player.Sp, kit.World.LedgerForTests.HoldingsOf(progress).CountOf(LedgerResourceIds.Sp));
            Assert.Contains(kit.Ledger.Entries, e => e.Transaction.Kind == LedgerTxKind.TrainerSkillEnchant);
        }
    }

    [Fact]
    public async Task Missing_route_logs_and_refuses()
    {
        var (kit, session, _, _, env) = await SetupAsync();
        using (env)
        {
            Assert.Equal("bad_request", kit.World.EnchantSkillForTest(session, skillId: 99_999, skillLevel: 101, informing: false));
        }
    }

    [Fact]
    public void Feature_gate_default_off()
    {
        var prior = Environment.GetEnvironmentVariable(ClientHeavyFeatures.SkillEnchantVariable);
        try
        {
            Environment.SetEnvironmentVariable(ClientHeavyFeatures.SkillEnchantVariable, null);
            Assert.False(ClientHeavyFeatures.SkillEnchantEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ClientHeavyFeatures.SkillEnchantVariable, prior);
        }
    }
}