using System.Globalization;
using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;
using Fineage.Zone;
using Fineage.Zone.Combat;
using Fineage.Zone.Geo;
using Fineage.Zone.Npcs;
using Fineage.Zone.Simulation;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;

namespace Fineage.MobiusDiff;

/// <summary>
/// Production-side AI scene driver. It builds a normal <see cref="ZoneWorld"/> from the scene's spawn rows,
/// enters one real character and advances the fixed-step simulation. The observations are neutral names
/// (target role, hate, chase/return, thinking, minions, …), so the same fields can be compared with
/// <see cref="MobiusAiReferenceModel"/>.
///
/// It drives the production AI; it does not alter AI rules. A divergence is reported by the caller.
/// </summary>
public static class AiSceneProbe
{
    public const string MapId = "ai_scene";
    public const long FixedStartUnixMs = 1_800_000_000_000;

    public static async Task<AiTraceFile> RunAsync(
        AiScene scene,
        GameData game,
        NpcCatalog npcs,
        CancellationToken cancellationToken = default)
    {
        var store = MemoryAccountStore.InMemory();
        var createdPlayers = new List<(AiPlayerSpec Spec, CharacterRecord Character)>();
        int playerNumber = 0;
        foreach (var playerSpec in scene.AllPlayers)
        {
            playerNumber++;
            string account = "ai_probe" + playerNumber.ToString(CultureInfo.InvariantCulture);
            if (store.TryAddAccount(account, "hash", "salt", 1, null) != AddAccountStatus.Created)
                throw new InvalidOperationException($"{scene.Id}: could not create scene account '{account}'.");
            var starter = game.StarterKits.Contains(playerSpec.ClassId)
                ? CharacterInventory.Rows(game.CreateStarterInventory(playerSpec.ClassId))
                : [];
            string name = "AiProbe" + playerNumber.ToString(CultureInfo.InvariantCulture);
            var createdStatus = store.TryAddCharacter(
                account, name, playerSpec.ClassId, race: 1, sex: 0, out var character,
                items: starter, hasPosition: true, x: playerSpec.X, y: playerSpec.Y, z: playerSpec.Z,
                level: Math.Max(1, playerSpec.Level), maxHp: Math.Max(1, playerSpec.MaxHp),
                hp: Math.Max(1, playerSpec.Hp), maxMp: 80, mp: 80);
            if (createdStatus != CreateCharacterStatus.Ok || character == null)
                throw new InvalidOperationException(
                    $"{scene.Id}: could not create scene player '{playerSpec.Key}' ({createdStatus}).");
            createdPlayers.Add((playerSpec, character));
        }

        var defs = scene.Npcs.Select(n =>
        {
            if (!npcs.ById.TryGetValue(n.FineageId, out var template))
                throw new InvalidDataException(
                    $"{scene.Id}: npc '{n.Key}' fineageId {n.FineageId} is missing from npcs.json.");
            return SpawnCatalog.FromTemplate(
                n.ObjectId, template, n.X, n.Y, n.Z, n.Heading,
                respawnDelaySeconds: n.RespawnDelaySeconds,
                respawnRandomSeconds: n.RespawnRandomSeconds);
        }).ToList();
        var spawnCatalog = SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
        {
            [MapId] = new(MapId, true, defs)
        });

        long wallMs = FixedStartUnixMs;
        DateTimeOffset now = DateTimeOffset.FromUnixTimeMilliseconds(wallMs);
        var world = new ZoneWorld(
            game: game,
            accounts: store,
            spawnCatalog: spawnCatalog,
            activeMapId: MapId,
            aoi: new Fineage.Zone.Interest.GridAoi(3000),
            geo: GeoEngine.Empty,
            clock: () => now,
            ledgerJournal: new MemoryLedgerJournal(),
            includeEnabledMaps: false,
            allowAmbientBoats: false,
            regionExecutor: SequentialRegionExecutor.Instance,
            simulationSeed: (ulong)Math.Max(0, scene.Seed),
            combatRandom: new Random(scene.Seed),
            spawnRandom: new Random(scene.Seed + 1),
            dropRandom: new Random(scene.Seed + 2));
        world.StartEconomyPersistForTests();
        world.SimulationTicks.Resume(wallMs);

        try
        {
            var playersByRole = new Dictionary<string, ZoneEntity>(StringComparer.Ordinal);
            var sessionsByRole = new Dictionary<string, ZoneSession>(StringComparer.Ordinal);
            var playerRoleByObjectId = new Dictionary<int, string>();
            foreach (var (playerSpec, character) in createdPlayers)
            {
                var session = new ZoneSession();
                session.Bind(character.Id, character.Account, character.Name, character.ClassId);
                if (!await world.TryEnterAsync(session, cancellationToken))
                    throw new InvalidOperationException(
                        $"{scene.Id}: player '{playerSpec.Key}' could not enter the world.");
                if (!world.TryGetLivePlayer(session, out var livePlayer))
                    throw new InvalidOperationException(
                        $"{scene.Id}: player '{playerSpec.Key}' is not live after enter.");
                livePlayer.Karma = playerSpec.Karma;
                playersByRole[playerSpec.Key] = livePlayer;
                sessionsByRole[playerSpec.Key] = session;
                playerRoleByObjectId[livePlayer.ObjectId] = playerSpec.Key;
            }

            var byObjectId = new Dictionary<int, ZoneEntity>();
            var roleByObjectId = new Dictionary<int, string>(playerRoleByObjectId);
            foreach (var spec in scene.Npcs)
            {
                if (!world.TryGetEntityById(spec.ObjectId, out var npc))
                    throw new InvalidOperationException(
                        $"{scene.Id}: spawned npc '{spec.Key}' (object {spec.ObjectId}) is missing.");
                byObjectId[spec.ObjectId] = npc;
                roleByObjectId[spec.ObjectId] = spec.Key;
                npc.GlobalAggro = spec.GlobalAggro;
                npc.CurHp = Math.Max(1, (int)((long)npc.MaxHp * spec.HpPercent / 100));
            }
            foreach (var spec in scene.Npcs.Where(static s => !string.IsNullOrWhiteSpace(s.Leader)))
            {
                var leaderSpec = scene.Npcs.FirstOrDefault(n => n.Key == spec.Leader);
                if (leaderSpec == null || !byObjectId.TryGetValue(spec.ObjectId, out var minion) ||
                    !byObjectId.TryGetValue(leaderSpec.ObjectId, out var leader))
                    continue;
                minion.LeaderObjectId = leader.ObjectId;
                minion.RaidMinion = leader.NpcType is "RaidBoss" or "GrandBoss";
            }

            long stepMs = ZoneSimulationSettings.StepMilliseconds;
            int lastTick = scene.Observe.Ticks.Max();
            foreach (var action in scene.Actions)
                lastTick = Math.Max(lastTick, action.Tick);
            var actionByTick = scene.Actions.GroupBy(a => a.Tick).ToDictionary(g => g.Key, g => g.ToList());
            var observeTicks = scene.Observe.Ticks.ToHashSet();
            var trace = new AiTraceFile
            {
                Scenario = scene.Id,
                Category = scene.Category,
                Oracle = new AiTraceProvenance
                {
                    Kind = "fineage-production-trace",
                    Reference = scene.Oracle.Reference,
                    Driver = "AiSceneProbe/ZoneWorld",
                    GeneratedBy = "AiSceneProbe/ZoneWorld",
                    FullServerVerified = true,
                    FineageOutputUsed = true,
                    Sources = ["src/Fineage.Zone/World (production ZoneWorld)"]
                },
                Npcs = scene.Npcs.Select(static n => new AiNpcReference
                {
                    Key = n.Key,
                    MobiusId = n.MobiusId,
                    FineageId = n.FineageId,
                    Source = n.Source
                }).ToList()
            };

            for (int tick = 1; tick <= lastTick; tick++)
            {
                wallMs = FixedStartUnixMs + tick * stepMs;
                now = DateTimeOffset.FromUnixTimeMilliseconds(wallMs);
                if (actionByTick.TryGetValue(tick, out var actions))
                {
                    foreach (var action in actions)
                        ApplyAction(scene, action, world, playersByRole, sessionsByRole, byObjectId);
                }

                world.StepSimulation();

                if (observeTicks.Contains(tick))
                {
                    var step = new AiTraceStep { Tick = tick };
                    foreach (string field in scene.Observe.Fields)
                        step.Values[field] = ReadField(world, scene, field, byObjectId, roleByObjectId);
                    trace.Steps.Add(step);
                }
            }

            return trace;
        }
        finally
        {
            world.StopEconomyPersistForTests();
        }
    }

    static void ApplyAction(
        AiScene scene,
        AiActionSpec action,
        ZoneWorld world,
        Dictionary<string, ZoneEntity> playersByRole,
        Dictionary<string, ZoneSession> sessionsByRole,
        Dictionary<int, ZoneEntity> byObjectId)
    {
        if (!playersByRole.TryGetValue(action.Player, out var player) ||
            !sessionsByRole.TryGetValue(action.Player, out var session))
            throw new InvalidDataException(
                $"{scene.Id}: action at tick {action.Tick} names unknown player '{action.Player}'.");
        switch (action.Type)
        {
            case "move":
                world.TeleportPlayerForTests(session, action.X, action.Y, action.Z);
                break;
            case "karma":
                player.Karma = action.Value;
                break;
            case "setGlobalAggro":
                if (byObjectId.TryGetValue(ObjectIdOf(scene, action.Npc), out var tuned))
                    tuned.GlobalAggro = action.Value;
                break;
            case "damage":
                if (!byObjectId.TryGetValue(ObjectIdOf(scene, action.Npc), out var target) ||
                    !world.TryGetEntityById(target.ObjectId, out var live))
                    break;
                world.DamageNpcByForTest(player, live, action.Amount);
                break;
            default:
                throw new InvalidDataException($"{scene.Id}: unknown action type '{action.Type}'.");
        }
    }

    static int ObjectIdOf(AiScene scene, string key) =>
        scene.Npcs.FirstOrDefault(n => n.Key == key)?.ObjectId
        ?? throw new InvalidDataException($"{scene.Id}: action names unknown npc '{key}'.");

    static string ReadField(
        ZoneWorld world,
        AiScene scene,
        string field,
        Dictionary<int, ZoneEntity> byObjectId,
        Dictionary<int, string> roleByObjectId)
    {
        var spec = AiFieldSpec.Parse(field);
        if (spec.Kind == "world")
            return spec.Sub switch
            {
                "npcCount" => Invariant(world.SpawnedNpcCount),
                "thinkingCount" => Invariant(world.ActiveSetCounts.Thinking),
                _ => throw new InvalidDataException($"{scene.Id}: unknown world field '{spec.Sub}'.")
            };

        if (!byObjectId.TryGetValue(ObjectIdOf(scene, spec.Npc), out var known) ||
            !world.TryGetEntityById(known.ObjectId, out var npc))
            return spec.Sub switch
            {
                "present" => "false",
                "target" or "leader" or "aiSkills" => "",
                _ => "0"
            };

        return spec.Sub switch
        {
            "target" => RoleOf(npc.AggroTargetId, roleByObjectId),
            "hate" => Invariant(HateOf(npc, spec.Target, byObjectId, roleByObjectId, scene)),
            "chasing" => Bool(npc.Chasing),
            "returningHome" => Bool(npc.ReturningHome),
            "thinking" => Bool((npc.ActiveSets & ActiveSets.Thinking) != 0),
            "present" => "true",
            "dead" => Bool(npc.Dead),
            "hp" => Invariant(npc.CurHp),
            "leader" => RoleOf(npc.LeaderObjectId, roleByObjectId),
            "minionCount" => Invariant(npc.MinionIds.Count(id => world.TryGetEntityById(id, out _))),
            "raidMinion" => Bool(npc.RaidMinion),
            "aiSkills" => string.Join(",", (npc.AiSkills ?? [])
                .OrderBy(s => s.SkillId).ThenBy(s => s.Level).Select(s => $"{s.SkillId}:{s.Level}")),
            _ => throw new InvalidDataException($"{scene.Id}: unknown npc field '{spec.Sub}'.")
        };
    }

    static long HateOf(
        ZoneEntity npc,
        string targetRole,
        Dictionary<int, ZoneEntity> byObjectId,
        Dictionary<int, string> roleByObjectId,
        AiScene scene)
    {
        int targetId = roleByObjectId.FirstOrDefault(kv => kv.Value == targetRole).Key;
        if (targetId == 0)
        {
            int objectId = ObjectIdOf(scene, targetRole);
            if (!byObjectId.ContainsKey(objectId))
                return 0;
            targetId = objectId;
        }
        return npc.HateList.GetValueOrDefault(targetId);
    }

    static string RoleOf(int objectId, Dictionary<int, string> roleByObjectId)
    {
        if (objectId == 0)
            return "";
        return roleByObjectId.TryGetValue(objectId, out var role) ? role : $"id:{objectId}";
    }

    static string Bool(bool value) => value ? "true" : "false";

    static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
