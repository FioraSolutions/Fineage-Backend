using Fineage.Login;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneSpawnRespawnTests
{
    [Fact]
    public async Task Gremlin_reappears_after_datapack_respawn_delay()
    {
        var gremlin = TalkingIsland.Gremlins[0];
        var row = new SpawnNpcDefinition(
            gremlin.ObjectId,
            gremlin.NpcId,
            gremlin.Name,
            gremlin.X,
            gremlin.Y,
            gremlin.Z,
            gremlin.Heading,
            gremlin.Attackable,
            TalkingIsland.GremlinMaxHp,
            gremlin.RunSpeed,
            gremlin.WalkSpeed,
            RespawnDelaySeconds: 2,
            RespawnRandomSeconds: 0);

        var catalog = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            [SpawnCatalog.DefaultMapId] = new(SpawnCatalog.DefaultMapId, Enabled: true, [row])
        });

        var clock = new SteppedClock(DateTimeOffset.UtcNow);
        var world = new ZoneWorld(
            spawnCatalog: catalog,
            clock: () => clock.Now,
            spawnRandom: new Random(0));

        var login = new LoginService(JsonAccountStore.InMemory(), new HandoffTokens(HandoffTokens.DefaultSecret));
        var ticket = login.Play("spawn", "secret1", "Hero", HumanClasses.Fighter).Value!;
        await using var stream = new MemoryStream();
        var session = new ZoneSession(stream);
        session.Bind(ticket.CharacterId, ticket.Account, ticket.CharacterName, ticket.ClassId);
        Assert.True(await world.TryEnterAsync(session, CancellationToken.None));

        Assert.Null(await world.TryMoveAsync(session, new MoveToMsg
        {
            DestX = gremlin.X,
            DestY = gremlin.Y,
            DestZ = gremlin.Z
        }, CancellationToken.None));
        Assert.Null(await world.TrySelectTargetAsync(session, new SelectTargetMsg { TargetObjectId = gremlin.ObjectId },
            CancellationToken.None));

        int hits = TalkingIsland.GremlinMaxHp / ZoneCombat.SampleMeleeDamage;
        for (int hit = 0; hit < hits; hit++)
        {
            Assert.Null(await world.TryAttackAsync(session, new AttackRequestMsg { TargetObjectId = gremlin.ObjectId },
                CancellationToken.None));
            // Re-click on the target being hit keeps the running attack (no double swing);
            // the auto-attack tick swings every Pspd interval and damage lands at hitTime.
            clock.Advance(TimeSpan.FromMilliseconds(ZoneCombat.PlayerMeleeHitDelayMs));
            await world.RunCombatTickAsync(CancellationToken.None);
            clock.Advance(TimeSpan.FromMilliseconds(ZoneCombat.AutoAttackIntervalMs - ZoneCombat.PlayerMeleeHitDelayMs));
            await world.RunCombatTickAsync(CancellationToken.None);
        }

        // Corpse stays for Mobius DEFAULT_CORPSE_TIME, then decays and the respawn timer starts.
        clock.Advance(TimeSpan.FromMilliseconds(ZoneCombat.CorpseDecayMs));
        world.AdvanceSpawnsForTests();
        Assert.DoesNotContain(gremlin.ObjectId, world.NpcObjectIds);

        clock.Advance(TimeSpan.FromSeconds(2));
        world.AdvanceSpawnsForTests();

        Assert.Contains(gremlin.ObjectId, world.NpcObjectIds);
    }

    sealed class SteppedClock(DateTimeOffset start)
    {
        public DateTimeOffset Now { get; private set; } = start;

        public void Advance(TimeSpan delta) => Now += delta;
    }
}
