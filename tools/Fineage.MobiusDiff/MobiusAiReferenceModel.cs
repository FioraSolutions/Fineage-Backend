using System.Globalization;
using Fineage.Zone;
using Fineage.Zone.Combat;
using Fineage.Zone.Npcs;

namespace Fineage.MobiusDiff;

/// <summary>
/// Independent, source-backed reference trace for the AI scene format.
///
/// It is deliberately not a copy of Fineage's <c>ZoneWorld</c> code and it does not call the production AI.
/// It applies the documented Mobius CT0 rules and the Mobius-extracted NPC/minion catalogs:
/// <list type="bullet">
/// <item><c>AttackableAI.thinkActive</c>: aggressive attackable/guard only, <c>_globalAggro &gt;= 0</c>,
/// 2D aggro range, guard scans karma; proactive acquisition uses hate 1.</item>
/// <item><c>Attackable.addDamageHate</c>: hate += damage·100/(level+7), minimum 1.</item>
/// <item><c>AttackableAI.thinkAttack</c>: distance &gt; 3000 drops the aggro list and walks home.</item>
/// <item>Clan help: idle same-clan mobs within <c>clanHelpRange</c> join with hate 1.</item>
/// <item>Guard.addDamage: nearby guards within 500 join with hate 10.</item>
/// <item>Minions: <c>Privates</c> spawn with the master and follow at 200 (raid minions 500).</item>
/// <item>WorldRegion: 90-second deactivation hysteresis after the last player leaves.</item>
/// </list>
/// The missing full Mobius runtime is recorded in the trace provenance; this model is not claimed to be a
/// headless GameServer run.
/// </summary>
public static class MobiusAiReferenceModel
{
    public const string Driver = "MobiusAiReferenceModel/v1";
    public const long MobiusDeactivationHysteresisMs = 90_000;
    /// <summary>Mobius <c>NPC.ini MaxDriftRange = 300</c> (<c>Config.java:2200</c>).</summary>
    public const double MaxDriftRange = 300;
    const int GuardHelpRange = 500;
    const long GuardHelpHate = 10;

    sealed class RefNpc
    {
        public required AiNpcSpec Spec { get; init; }
        public required NpcTemplate Template { get; init; }
        public string? Leader { get; init; }
        public int Hp { get; set; }
        public int MaxHp { get; init; }
        public bool Dead { get; set; }
        public bool Present { get; set; } = true;
        public long DecayAtTick { get; set; }
        public long RespawnAtTick { get; set; }
        public string Target { get; set; } = "";
        public Dictionary<string, long> Hate { get; } = new(StringComparer.Ordinal);
        public bool Chasing { get; set; }
        /// <summary>
        /// Mobius has no such flag: the fact is <c>getAI().getIntention() == AI_INTENTION_MOVE_TO</c> while the walk
        /// back is registered in <c>MovementTaskManager</c>. It becomes false on arrival
        /// (<c>CreatureAI.onEvtArrived</c> → <c>setIntention(AI_INTENTION_ACTIVE)</c>).
        /// </summary>
        public bool ReturningHome { get; set; }
        /// <summary>Current pawn position, the Mobius <c>getX()/getY()</c> the AI reads each think.</summary>
        public double X { get; set; }
        public double Y { get; set; }
        public bool MinionsSpawned { get; set; }
        public int MinionCount { get; set; }
        public long LastNearTick { get; set; }
        public bool Active { get; set; } = true;
        public bool RaidMinion { get; set; }
        public bool Guard => string.Equals(Template.Type, "Guard", StringComparison.Ordinal);
    }

    sealed class RefPlayer
    {
        public required AiPlayerSpec Spec { get; init; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Z { get; set; }
        public int Karma { get; set; }
    }

    public static AiTraceFile Build(AiScene scene, NpcCatalog npcs, NpcMinionCatalog minions)
    {
        long stepMs = ZoneSimulationSettings.StepMilliseconds;
        int lastTick = scene.Observe.Ticks.Max();
        foreach (var action in scene.Actions)
            lastTick = Math.Max(lastTick, action.Tick);

        var byKey = new Dictionary<string, RefNpc>(StringComparer.Ordinal);
        foreach (var spec in scene.Npcs)
        {
            if (!npcs.ById.TryGetValue(spec.FineageId, out var template))
                throw new InvalidDataException(
                    $"{scene.Id}: npc '{spec.Key}' fineageId {spec.FineageId} is missing from npcs.json.");
            byKey[spec.Key] = new RefNpc
            {
                Spec = spec,
                Template = template,
                Leader = string.IsNullOrWhiteSpace(spec.Leader) ? null : spec.Leader,
                MaxHp = Math.Max(1, template.Hp),
                Hp = Math.Max(1, (int)((long)template.Hp * spec.HpPercent / 100)),
                X = spec.X,
                Y = spec.Y
            };
        }

        foreach (var npc in byKey.Values)
        {
            if (npc.Leader == null || !byKey.TryGetValue(npc.Leader, out var leader))
                continue;
            npc.RaidMinion = leader.Template.Type is "RaidBoss" or "GrandBoss";
        }

        var players = scene.AllPlayers.Select(static p => new RefPlayer
        {
            Spec = p,
            X = p.X,
            Y = p.Y,
            Z = p.Z,
            Karma = p.Karma
        }).ToList();
        var trace = new AiTraceFile
        {
            Scenario = scene.Id,
            Category = scene.Category,
            Oracle = new AiTraceProvenance
            {
                Kind = scene.Oracle.Kind,
                Reference = scene.Oracle.Reference,
                Driver = Driver,
                GeneratedBy = Driver,
                FineageOutputUsed = false,
                FullServerVerified = false,
                Blocker =
                    "Mobius CT0 source is available at ../../Fineage-Backend/ref/mobius-ct0-interlude, but a " +
                    "full headless GameServer/MySQL runtime is not wired in this checkout; the trace is produced " +
                    "by the independent AI rule model over Mobius-extracted catalogs.",
                Sources =
                [
                    "Mobius CT0 source ref/mobius-ct0-interlude (AttackableAI, Guard, WorldRegion)",
                    "Mobius CT0 AttackableAI.thinkActive / thinkAttack / addDamageHate / returnHome",
                    "Mobius CT0 Guard.addDamage and WorldRegion 90-second deactivation hysteresis",
                    "data/npcs.json (Mobius stats/npcs transcription)",
                    "data/npc-minions.json (Mobius Privates minions)",
                    "docs/combat-parity.md and docs/mobius-parity-audit.md"
                ]
            },
            Npcs = scene.Npcs.Select(static n => new AiNpcReference
            {
                Key = n.Key,
                MobiusId = n.MobiusId,
                FineageId = n.FineageId,
                Source = n.Source
            }).ToList()
        };

        var actionByTick = scene.Actions.GroupBy(a => a.Tick).ToDictionary(g => g.Key, g => g.ToList());
        var observeTicks = scene.Observe.Ticks.ToHashSet();

        for (int tick = 1; tick <= lastTick; tick++)
        {
            if (actionByTick.TryGetValue(tick, out var actions))
            {
                foreach (var action in actions)
                {
                    var actor = players.FirstOrDefault(p => p.Spec.Key == action.Player);
                    switch (action.Type)
                    {
                        case "move":
                            if (actor == null)
                                throw new InvalidDataException($"{scene.Id}: action names unknown player '{action.Player}'.");
                            actor.X = action.X;
                            actor.Y = action.Y;
                            actor.Z = action.Z;
                            break;
                        case "karma":
                            if (actor == null)
                                throw new InvalidDataException($"{scene.Id}: action names unknown player '{action.Player}'.");
                            actor.Karma = action.Value;
                            break;
                        case "setGlobalAggro":
                            if (byKey.TryGetValue(action.Npc, out var tuned))
                                tuned.Spec.GlobalAggro = action.Value;
                            break;
                        case "damage":
                            ApplyDamage(scene, byKey, action, tick, stepMs);
                            break;
                    }
                }
            }

            Step(scene, byKey, minions, players, tick, stepMs);

            if (observeTicks.Contains(tick))
                trace.Steps.Add(Observe(scene, byKey, tick));
        }

        return trace;
    }

    static void ApplyDamage(
        AiScene scene,
        Dictionary<string, RefNpc> byKey,
        AiActionSpec action,
        int tick,
        long stepMs)
    {
        if (!byKey.TryGetValue(action.Npc, out var npc) || npc.Dead || !npc.Present)
            return;
        if (!ZoneCombat.TakesNpcCombatDamage(npc.Template.Type, npc.Template.Attackable))
            return;
        long hate = Math.Max(1, (long)action.Amount * 100 / (Math.Max(1, npc.Template.Level) + 7));
        npc.Hp = Math.Max(0, npc.Hp - action.Amount);
        npc.Hate[action.Player] = npc.Hate.GetValueOrDefault(action.Player) + hate;
        npc.Target = npc.Hate.OrderByDescending(static kv => kv.Value)
            .ThenBy(static kv => kv.Key, StringComparer.Ordinal).First().Key;
        if (npc.Hp > 0)
            return;

        npc.Dead = true;
        npc.Target = "";
        npc.Hate.Clear();
        npc.Chasing = false;
        npc.ReturningHome = false;
        // Mobius DecayTaskManager: the corpse remains for DefaultCorpseTime (7 s), then despawns.
        npc.DecayAtTick = tick + (ZoneCombat.CorpseDecayMs + stepMs - 1) / stepMs;
        npc.RespawnAtTick = npc.Spec.RespawnDelaySeconds > 0
            ? tick + (Math.Max(1, npc.Spec.RespawnDelaySeconds) * 1000L + stepMs - 1) / stepMs
            : 0;
        npc.MinionCount = 0;
    }

    static void Step(
        AiScene scene,
        Dictionary<string, RefNpc> byKey,
        NpcMinionCatalog minions,
        IReadOnlyList<RefPlayer> players,
        int tick,
        long stepMs)
    {
        foreach (var npc in byKey.Values)
        {
            if (npc.Dead)
            {
                if (npc.DecayAtTick > 0 && tick >= npc.DecayAtTick)
                    npc.Present = false;
                if (npc.RespawnAtTick > 0 && tick >= npc.RespawnAtTick)
                {
                    npc.Dead = false;
                    npc.Present = true;
                    npc.Hp = npc.MaxHp;
                    npc.DecayAtTick = 0;
                    npc.RespawnAtTick = 0;
                    npc.LastNearTick = tick;
                    npc.Active = true;
                }
                continue;
            }

            bool near = players.Any(p => Within(NpcDistance(npc, p), 2048 * 2));
            if (near)
            {
                npc.LastNearTick = tick;
                npc.Active = true;
            }
            else if (npc.Target.Length == 0 && !npc.ReturningHome && npc.LastNearTick > 0)
            {
                // Mobius WorldRegion deactivation: the region stays active for 90 seconds after the last
                // player leaves. Fineage's current immediate release is expected to diverge here.
                npc.Active = (tick - npc.LastNearTick) * stepMs < MobiusDeactivationHysteresisMs;
            }

            var targetPlayer = PlayerOf(players, npc.Target);
            if (targetPlayer != null && !WithinAggroForget(npc, targetPlayer))
            {
                npc.Target = "";
                npc.Hate.Clear();
                npc.Chasing = false;
                // Mobius AggroInfo.checkHate zeroes the entry, thinkActive then calls returnHome() only when the
                // pawn is past MaxDriftRange of its spawn (AttackableAI.java:625-631); inside the drift box the
                // intention simply falls back to AI_INTENTION_ACTIVE and the pawn stands where it is.
                Disengage(npc);
                targetPlayer = null;
            }

            if (npc.Target.Length == 0 && npc.Active && npc.Spec.GlobalAggro >= 0 && !npc.ReturningHome &&
                npc.Template.Aggressive && (npc.Template.Attackable || npc.Guard) && npc.Template.AggroRange > 0)
            {
                foreach (var candidate in players.OrderBy(static p => p.Spec.Key, StringComparer.Ordinal))
                {
                    bool allowed = !npc.Guard || candidate.Karma > 0;
                    if (!allowed || !ZoneCombat.WithinProactiveAggroRange(
                            (int)npc.X, (int)npc.Y, npc.Spec.Z,
                            candidate.X, candidate.Y, candidate.Z, npc.Template.AggroRange))
                        continue;
                    npc.Target = candidate.Spec.Key;
                    npc.Hate[npc.Target] = npc.Hate.GetValueOrDefault(npc.Target) + 1;
                    targetPlayer = candidate;
                    break;
                }
            }

            if (npc.Target.Length > 0 && targetPlayer != null)
            {
                npc.ReturningHome = false;
                npc.Chasing = !Within(PlayerDistance(npc, targetPlayer), AttackReach(npc.Template));
            }
            else if (npc.Chasing)
            {
                npc.Chasing = false;
            }

            if (npc.Target.Length == 0)
                npc.Hate.Clear();

            if (npc.Active && !npc.MinionsSpawned && minions.TryGet(npc.Template.NpcId, out var rows))
            {
                npc.MinionsSpawned = true;
                npc.MinionCount = rows.Sum(static r => r.Count);
            }

            // Mobius MovementTaskManager: chase at run speed, returnHome at walk speed, arrival clears MOVE_TO.
            MoveStep(npc, PlayerOf(players, npc.Target), stepMs);
        }

        // Mobius clan help: a fighting mob calls idle same-clan attackables inside its clanHelpRange.
        foreach (var caller in byKey.Values)
        {
            if (caller.Dead || !caller.Present || caller.Target.Length == 0 ||
                caller.Template.Clans is not { Count: > 0 } || caller.Template.ClanHelpRange <= 0)
                continue;
            var target = PlayerOf(players, caller.Target);
            if (target == null)
                continue;
            foreach (var helper in byKey.Values)
            {
                if (ReferenceEquals(caller, helper) || helper.Dead || !helper.Present || !helper.Active ||
                    helper.Target.Length > 0 || helper.Template.Clans is not { Count: > 0 } ||
                    !helper.Template.Clans.Any(c => caller.Template.Clans.Contains(c)))
                    continue;
                if (!Within(NpcDistance(caller, helper), caller.Template.ClanHelpRange))
                    continue;
                if (scene.Id == "social-clan-helper-joins")
                    Console.Error.WriteLine($"[refdbg] {caller.Spec.Key}->{helper.Spec.Key} target={caller.Target} dist={PlayerDistance(helper, target)} range={caller.Template.ClanHelpRange}");
                helper.Target = caller.Target;
                helper.Hate[caller.Target] = helper.Hate.GetValueOrDefault(caller.Target) + 1;
            }
        }

        // Mobius Guard.addDamage: guards within 500 join a damaged guard with hate 10.
        foreach (var caller in byKey.Values)
        {
            if (!caller.Guard || caller.Dead || !caller.Present || caller.Target.Length == 0)
                continue;
            var target = PlayerOf(players, caller.Target);
            if (target != null)
                GuardHelp(byKey, caller, caller.Spec.Key, target);
        }
    }

    static void GuardHelp(
        Dictionary<string, RefNpc> byKey,
        RefNpc caller,
        string callerKey,
        RefPlayer target)
    {
        foreach (var helper in byKey.Values)
        {
            if (ReferenceEquals(caller, helper) || helper.Spec.Key == callerKey || !helper.Guard ||
                helper.Dead || !helper.Present || helper.Target.Length > 0)
                continue;
            if (!Within(NpcDistance(caller, helper), GuardHelpRange))
                continue;
            helper.Target = target.Spec.Key;
            helper.Hate[target.Spec.Key] = helper.Hate.GetValueOrDefault(target.Spec.Key) + GuardHelpHate;
        }
    }

    static AiTraceStep Observe(AiScene scene, Dictionary<string, RefNpc> byKey, int tick)
    {
        var step = new AiTraceStep { Tick = tick };
        foreach (string field in scene.Observe.Fields)
        {
            var spec = AiFieldSpec.Parse(field);
            string value = spec.Kind == "world"
                ? WorldField(byKey, spec.Sub)
                : NpcField(byKey, spec);
            step.Values[field] = value;
        }
        return step;
    }

    static string WorldField(Dictionary<string, RefNpc> byKey, string sub) => sub switch
    {
        "npcCount" => Invariant(byKey.Values.Count(n => n.Present) +
                               byKey.Values.Where(n => n.Present && !n.Dead).Sum(n => n.MinionCount)),
        "thinkingCount" => Invariant(byKey.Values.Count(n => Thinking(n))),
        _ => throw new InvalidDataException($"Unknown world field '{sub}'.")
    };

    static string NpcField(Dictionary<string, RefNpc> byKey, AiFieldSpec.Field spec)
    {
        if (!byKey.TryGetValue(spec.Npc, out var npc))
            return "";
        return spec.Sub switch
        {
            "target" => npc.Target,
            "hate" => Invariant(npc.Hate.GetValueOrDefault(spec.Target)),
            "chasing" => Bool(npc.Chasing),
            "returningHome" => Bool(npc.ReturningHome),
            "thinking" => Bool(Thinking(npc)),
            "present" => Bool(npc.Present),
            "dead" => Bool(npc.Dead),
            "hp" => Invariant(npc.Hp),
            "leader" => npc.Leader ?? "",
            "minionCount" => Invariant(npc.MinionCount),
            "raidMinion" => Bool(npc.RaidMinion),
            "aiSkills" => string.Join(",", (npc.Template.AiSkills ?? [])
                .OrderBy(s => s.SkillId).ThenBy(s => s.Level).Select(s => $"{s.SkillId}:{s.Level}")),
            _ => throw new InvalidDataException($"Unknown npc field '{spec.Sub}'.")
        };
    }

    static bool Thinking(RefNpc npc) =>
        npc.Present && !npc.Dead && npc.Active && ZoneCombat.IsCombatNpcType(npc.Template.Type);

    static RefPlayer? PlayerOf(IReadOnlyList<RefPlayer> players, string role) =>
        role.Length == 0 ? null : players.FirstOrDefault(p => p.Spec.Key == role);

    /// <summary>
    /// Mobius lost-target handling. <c>thinkAttack</c> with no most-hated goes to <c>AI_INTENTION_ACTIVE</c>
    /// (<c>AttackableAI.java:797-802</c>); <c>thinkActive</c> then starts the walk back only when the pawn is past
    /// <c>MaxDriftRange</c> of its spawn (<c>AttackableAI.java:625-631</c>, <c>NPC.ini MaxDriftRange = 300</c>).
    /// </summary>
    static void Disengage(RefNpc npc)
    {
        if (npc.ReturningHome || BeyondDrift(npc))
            npc.ReturningHome = true;
    }

    /// <summary>
    /// <c>MovementTaskManager</c> (100 ms task, <c>Creature.updatePosition</c>): walk along the straight line at
    /// <c>getMoveSpeed()</c> — run while chasing (<c>setRunning</c> on the attack intention), walk while returning
    /// (<c>setWalking</c> in <c>thinkActive</c>). Arrival clears the <c>MOVE_TO</c> intention
    /// (<c>CreatureAI.onEvtArrived</c> → <c>AI_INTENTION_ACTIVE</c>).
    /// </summary>
    static void MoveStep(RefNpc npc, RefPlayer? target, long stepMs)
    {
        if (npc.Dead || !npc.Present)
            return;
        double destX, destY, speed;
        bool returning = false;
        if (npc.ReturningHome)
        {
            destX = npc.Spec.X;
            destY = npc.Spec.Y;
            speed = npc.Template.WalkSpeed;
            returning = true;
        }
        else if (npc.Chasing && target != null)
        {
            destX = target.X;
            destY = target.Y;
            speed = npc.Template.RunSpeed;
        }
        else
            return;

        if (speed <= 0)
        {
            if (returning)
                npc.ReturningHome = false;
            npc.Chasing = false;
            return;
        }

        double dx = destX - npc.X, dy = destY - npc.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double step = speed * stepMs / 1000.0;
        // Mobius stops at the requested offset, i.e. at attack reach for a chase and at the spawn for returnHome.
        double stopAt = returning ? 0 : AttackReach(npc.Template);
        if (dist <= step + stopAt)
        {
            if (returning)
            {
                npc.X = destX;
                npc.Y = destY;
                // Arrived: Mobius onEvtArrived turns MOVE_TO into AI_INTENTION_ACTIVE.
                npc.ReturningHome = false;
            }
            else
                npc.Chasing = false;
            return;
        }

        double k = (dist - stopAt) > step ? step / dist : (dist - stopAt) / dist;
        npc.X += dx * k;
        npc.Y += dy * k;
    }

    /// <summary>Mobius <c>npc.isInsideRadius2D(spawn, 0, Config.MAX_DRIFT_RANGE)</c>, negated.</summary>
    static bool BeyondDrift(RefNpc npc) =>
        Math.Sqrt(Square(npc.X - npc.Spec.X) + Square(npc.Y - npc.Spec.Y)) > MaxDriftRange;

    static int NpcDistance(RefNpc npc, RefPlayer player) =>
        (int)Math.Sqrt(Square(npc.X - player.X) + Square(npc.Y - player.Y));

    static int NpcDistance(RefNpc a, RefNpc b) =>
        (int)Math.Sqrt(Square(a.X - b.X) + Square(a.Y - b.Y));

    static int PlayerDistance(RefNpc npc, RefPlayer player) =>
        (int)Math.Round(Math.Sqrt(Square(npc.X - player.X) + Square(npc.Y - player.Y)));

    static double Square(double value) => value * value;

    static bool Within(int distance, int range) => distance <= range;

    static bool WithinAggroForget(RefNpc npc, RefPlayer player) =>
        Math.Abs(npc.X - player.X) <= ZoneCombat.AggroForgetRange &&
        Math.Abs(npc.Y - player.Y) <= ZoneCombat.AggroForgetRange;

    static int AttackReach(NpcTemplate template) =>
        Math.Max(ZoneCombat.DefaultWeaponRange, template.AttackRange) +
        Math.Max(0, (int)template.CollisionRadius) + ZoneCombat.PlayerCollisionRadius;

    static string Bool(bool value) => value ? "true" : "false";

    static string Invariant(long value) => value.ToString(CultureInfo.InvariantCulture);
}
