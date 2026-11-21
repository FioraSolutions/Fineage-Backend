using Fineage.Game;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Net;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius compound <c>&lt;and&gt;</c> cast trees on the shared <see cref="SkillCastConditionEvaluator"/> path.</summary>
public sealed class NetSkillCompoundConditionTests
{
    const int CrushOfDoom = 315;
    const int MobObjectId = 9_300_315;

    static SpawnCatalog Map() =>
        SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            ["cd"] = new("cd", true,
            [
                new SpawnNpcDefinition(MobObjectId, 90_001, "Gremlin", NetTestKit.ElfX + 30, NetTestKit.ElfY, NetTestKit.ElfZ,
                    0, true, 50_000, 1f, 1f, 60, 0, "", "Monster", Level: 1, Aggressive: false)
            ])
        });

    [Fact]
    public async Task Crush_of_doom_and_gate_requires_two_handed_sword_or_blunt()
    {
        var kit = new NetTestKit(Map(), "cd", new GeoEngine());
        var (session, player) = await kit.EnterAsync(classId: 3, race: 0, level: 60);
        player.KnownSkills[CrushOfDoom] = 1;
        player.CurMp = player.MaxMp = 2000;
        player.Inventory!.Equip(player.Inventory.Add(10)); // Dagger — fails AND (kinds + lrhand)
        var (bridge, rec) = NetBridgeRecorder.Create();
        kit.World.NetBridge = bridge;
        Assert.Null(await kit.World.TrySelectTargetAsync(session, MobObjectId, CancellationToken.None));

        int mp = player.CurMp;
        Assert.Null(await kit.World.TrySkillUseAsync(session, CrushOfDoom, MobObjectId, CancellationToken.None));
        Assert.True(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage))
            .Any(m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgUnsuitableTerms));
        Assert.Equal(mp, player.CurMp);

        if (player.Inventory.GetEquipped(PaperdollSlot.RHand) is { } worn)
            player.Inventory.Unequip(worn.ObjectId);
        player.Inventory.Equip(player.Inventory.Add(70)); // Claymore — SWORD + lrhand
        rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)).Clear();
        Assert.Null(await kit.World.TrySkillUseAsync(session, CrushOfDoom, MobObjectId, CancellationToken.None));
        Assert.NotNull(player.PendingHit);
        Assert.DoesNotContain(rec.Of(nameof(IWorldEventPublisher.FanoutSystemMessage)),
            m => ((SystemMessageMsg)m[1]!).Id == ZoneWorld.SysMsgUnsuitableTerms);
    }
}