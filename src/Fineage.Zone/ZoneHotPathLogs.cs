using Fineage.Accounts;
using Microsoft.Extensions.Logging;

namespace Fineage.Zone;

/// <summary>Source-generated logging for simulation / movement / combat / AI hot paths (#867 P2).</summary>
internal static partial class ZoneHotPathLogs
{
    static readonly ILogger Log = FineageLogging.GetLogger("Fineage.Zone");

    public static bool SimulationWarningEnabled => Log.IsEnabled(LogLevel.Warning);
    public static bool SimulationInformationEnabled => Log.IsEnabled(LogLevel.Information);

    // --- Simulation ---
    [LoggerMessage(EventId = 3000, Level = LogLevel.Warning,
        Message = "simulation stage failed: stage={Stage} scope={Scope} tick={Tick} error={ErrorType}: {ErrorMessage}")]
    static partial void SimulationStageFailed(
        ILogger logger, string stage, string scope, long tick, string errorType, string errorMessage);

    public static void SimulationStageFailed(string stage, string scope, long tick, Exception error) =>
        SimulationStageFailed(Log, stage, scope, tick, error.GetType().Name, error.Message);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "simulation skipped {Skipped} tick(s) before tick {Tick}: step ran {LateMs} ms into its interval; their stages run once now")]
    static partial void SimulationTicksSkipped(ILogger logger, long skipped, long tick, long lateMs);

    public static void SimulationTicksSkipped(long skipped, long tick, long lateMs) =>
        SimulationTicksSkipped(Log, skipped, tick, lateMs);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Warning,
        Message = "simulation tick {Tick} took {ElapsedMs:F1} ms, over its {BudgetMs:F0} ms step; slowest: {Slowest}{NpcCounts}")]
    static partial void SimulationTickOverrun(
        ILogger logger, long tick, double elapsedMs, double budgetMs, string slowest, string npcCounts);

    public static void SimulationTickOverrun(long tick, double elapsedMs, double budgetMs, string slowest, string npcCounts) =>
        SimulationTickOverrun(Log, tick, elapsedMs, budgetMs, slowest, npcCounts);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Warning,
        Message = "wall clock is {BehindMs} ms behind simulation tick {Tick}; the simulation keeps its fixed step until the wall clock catches up")]
    static partial void SimulationWallBehind(ILogger logger, long behindMs, long tick);

    [LoggerMessage(EventId = 3004, Level = LogLevel.Warning,
        Message = "wall clock caught up with simulation tick {Tick}")]
    static partial void SimulationWallCaughtUp(ILogger logger, long tick);

    public static void SimulationWallBehind(long behindMs, long tick) =>
        SimulationWallBehind(Log, behindMs, tick);

    public static void SimulationWallCaughtUp(long tick) =>
        SimulationWallCaughtUp(Log, tick);

    [LoggerMessage(EventId = 3005, Level = LogLevel.Warning,
        Message = "simulation step tick={Tick} failed outside stage isolation")]
    static partial void SimulationStepFailedOutsideStages(ILogger logger, long tick, Exception exception);

    public static void SimulationStepFailedOutsideStages(long tick, Exception exception) =>
        SimulationStepFailedOutsideStages(Log, tick, exception);

    [LoggerMessage(EventId = 3006, Level = LogLevel.Error, Message = "{Report}")]
    static partial void SimulationWatchdog(ILogger logger, string report);

    public static void SimulationWatchdog(string report) => SimulationWatchdog(Log, report);

    // --- Movement ---
    [LoggerMessage(EventId = 3100, Level = LogLevel.Information,
        Message = "[geo] path obj={ObjectId} from={FromX},{FromY},{FromZ} want={WantX},{WantY} nodes={NodeCount}")]
    static partial void GeoPath(
        ILogger logger, int objectId, int fromX, int fromY, int fromZ, int wantX, int wantY, int nodeCount);

    public static void GeoPath(int objectId, int fromX, int fromY, int fromZ, int wantX, int wantY, int nodeCount) =>
        GeoPath(Log, objectId, fromX, fromY, fromZ, wantX, wantY, nodeCount);

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information,
        Message = "[geo] move blocked obj={ObjectId} from={FromX},{FromY},{FromZ} want={WantX},{WantY},{WantZ} stop={StopX},{StopY},{StopZ}")]
    static partial void GeoMoveBlocked(
        ILogger logger, int objectId, int fromX, int fromY, int fromZ, int wantX, int wantY, int wantZ, int stopX, int stopY, int stopZ);

    public static void GeoMoveBlocked(
        int objectId, int fromX, int fromY, int fromZ, int wantX, int wantY, int wantZ, int stopX, int stopY, int stopZ) =>
        GeoMoveBlocked(Log, objectId, fromX, fromY, fromZ, wantX, wantY, wantZ, stopX, stopY, stopZ);

    [LoggerMessage(EventId = 3102, Level = LogLevel.Information,
        Message = "[speed] obj={ObjectId} run={RunSpeed} base={BaseRunSpeed}")]
    static partial void MoveSpeedRefreshed(ILogger logger, int objectId, float runSpeed, float baseRunSpeed);

    public static void MoveSpeedRefreshed(int objectId, float runSpeed, float baseRunSpeed) =>
        MoveSpeedRefreshed(Log, objectId, runSpeed, baseRunSpeed);

    [LoggerMessage(EventId = 3103, Level = LogLevel.Information,
        Message = "[water] obj={ObjectId} {State} at={X},{Y},{Z} speed={Speed}")]
    static partial void WaterState(ILogger logger, int objectId, string state, int x, int y, int z, float speed);

    public static void WaterState(int objectId, bool enter, int x, int y, int z, float speed) =>
        WaterState(Log, objectId, enter ? "enter" : "exit", x, y, z, speed);

    [LoggerMessage(EventId = 3104, Level = LogLevel.Information,
        Message = "[door] move blocked obj={ObjectId} door={DoorId}:{DoorName} from={FromX},{FromY},{FromZ} want={WantX},{WantY},{WantZ} stop={StopX},{StopY},{StopZ}")]
    static partial void DoorMoveBlocked(
        ILogger logger, int objectId, int doorId, string doorName, int fromX, int fromY, int fromZ,
        int wantX, int wantY, int wantZ, int stopX, int stopY, int stopZ);

    public static void DoorMoveBlocked(
        int objectId, int doorId, string doorName, int fromX, int fromY, int fromZ,
        int wantX, int wantY, int wantZ, int stopX, int stopY, int stopZ) =>
        DoorMoveBlocked(Log, objectId, doorId, doorName, fromX, fromY, fromZ, wantX, wantY, wantZ, stopX, stopY, stopZ);

    [LoggerMessage(EventId = 3105, Level = LogLevel.Information,
        Message = "[npc] return home obj={ObjectId} from={FromX},{FromY} home={HomeX},{HomeY}")]
    static partial void NpcReturnHome(ILogger logger, int objectId, int fromX, int fromY, int homeX, int homeY);

    public static void NpcReturnHome(int objectId, int fromX, int fromY, int homeX, int homeY) =>
        NpcReturnHome(Log, objectId, fromX, fromY, homeX, homeY);

    [LoggerMessage(EventId = 3106, Level = LogLevel.Information,
        Message = "[aggro] leash reset npc={ObjectId} from={FromX},{FromY} home={HomeX},{HomeY} restore={RestoreLife}")]
    static partial void AggroLeashReset(
        ILogger logger, int objectId, int fromX, int fromY, int homeX, int homeY, bool restoreLife);

    public static void AggroLeashReset(int objectId, int fromX, int fromY, int homeX, int homeY, bool restoreLife) =>
        AggroLeashReset(Log, objectId, fromX, fromY, homeX, homeY, restoreLife);

    // --- Combat / skills ---
    [LoggerMessage(EventId = 3200, Level = LogLevel.Information,
        Message = "[skill] cast {SkillId} lv{Level} kind={Kind} hit={HitMs}ms cool={CoolMs}ms reuse={ReuseMs}ms")]
    static partial void SkillCast(
        ILogger logger, int skillId, int level, string kind, int hitMs, int coolMs, int reuseMs);

    public static void SkillCast(int skillId, int level, string kind, int hitMs, int coolMs, int reuseMs) =>
        SkillCast(Log, skillId, level, kind, hitMs, coolMs, reuseMs);

    [LoggerMessage(EventId = 3201, Level = LogLevel.Information,
        Message = "[skill] blow {SkillId} failed front={Front} behind={Behind}")]
    static partial void SkillBlowFailed(ILogger logger, int skillId, bool front, bool behind);

    public static void SkillBlowFailed(int skillId, bool front, bool behind) =>
        SkillBlowFailed(Log, skillId, front, behind);

    [LoggerMessage(EventId = 3202, Level = LogLevel.Information,
        Message = "[cast] abort obj={ObjectId} skill={SkillId} reason={Reason}")]
    static partial void CastAbort(ILogger logger, int objectId, int skillId, string reason);

    public static void CastAbort(int objectId, int skillId, string reason) =>
        CastAbort(Log, objectId, skillId, reason);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Information,
        Message = "[skill] effect {SkillId} lv{Level} target={TargetId}")]
    static partial void SkillEffect(ILogger logger, int skillId, int level, int targetId);

    public static void SkillEffect(int skillId, int level, int targetId) =>
        SkillEffect(Log, skillId, level, targetId);

    [LoggerMessage(EventId = 3204, Level = LogLevel.Information,
        Message = "[cast] stop-move obj={ObjectId} at={X},{Y}")]
    static partial void CastStopMove(ILogger logger, int objectId, int x, int y);

    public static void CastStopMove(int objectId, int x, int y) =>
        CastStopMove(Log, objectId, x, y);

    [LoggerMessage(EventId = 3205, Level = LogLevel.Information,
        Message = "[cast] queued skill={SkillId} obj={ObjectId} at={CastingUntilMs}")]
    static partial void CastQueued(ILogger logger, int skillId, int objectId, long castingUntilMs);

    public static void CastQueued(int skillId, int objectId, long castingUntilMs) =>
        CastQueued(Log, skillId, objectId, castingUntilMs);

    [LoggerMessage(EventId = 3206, Level = LogLevel.Warning,
        Message = "skill-combat missing skillId={SkillId} — refuse cast")]
    static partial void SkillCombatMissing(ILogger logger, int skillId);

    [LoggerMessage(EventId = 3212, Level = LogLevel.Warning, Message = "skill={SkillId} lv={SkillLevel} {Detail}")]
    static partial void SkillCastUnsupported(ILogger logger, int skillId, int skillLevel, string detail);

    public static void SkillCastUnsupported(int skillId, int skillLevel, string detail) =>
        SkillCastUnsupported(Log, skillId, skillLevel, detail);

    public static void SkillCombatMissing(int skillId) => SkillCombatMissing(Log, skillId);

    [LoggerMessage(EventId = 3207, Level = LogLevel.Information,
        Message = "[toggle] on skill={SkillId} lv{Level} mp={CurMp}/{MaxMp} initial={Initial}")]
    static partial void ToggleOn(ILogger logger, int skillId, int level, int curMp, int maxMp, int initial);

    public static void ToggleOn(int skillId, int level, int curMp, int maxMp, int initial) =>
        ToggleOn(Log, skillId, level, curMp, maxMp, initial);

    [LoggerMessage(EventId = 3208, Level = LogLevel.Information,
        Message = "[toggle] off skill={SkillId} reason={Reason} obj={ObjectId}")]
    static partial void ToggleOff(ILogger logger, int skillId, string reason, int objectId);

    public static void ToggleOff(int skillId, string reason, int objectId) =>
        ToggleOff(Log, skillId, reason, objectId);

    [LoggerMessage(EventId = 3209, Level = LogLevel.Information,
        Message = "[fakedeath] wake obj={ObjectId}")]
    static partial void FakeDeathWake(ILogger logger, int objectId);

    public static void FakeDeathWake(int objectId) => FakeDeathWake(Log, objectId);

    [LoggerMessage(EventId = 3210, Level = LogLevel.Information,
        Message = "[toggle] tick skill={SkillId} mp={CurMp}/{MaxMp} cost={Cost:F2}")]
    static partial void ToggleTick(ILogger logger, int skillId, int curMp, int maxMp, double cost);

    public static void ToggleTick(int skillId, int curMp, int maxMp, double cost) =>
        ToggleTick(Log, skillId, curMp, maxMp, cost);

    [LoggerMessage(EventId = 3211, Level = LogLevel.Information,
        Message = "[debuff-land] skill={SkillId} lv={Level} ml={MagicLevel} rate={ActivateRate} trait={Trait} traitMod={TraitMod:F3} mAtkMod={MAtkMod:F3} chance={Chance:F1} landed={Landed} atk={AttackerId} tgt={TargetId}")]
    static partial void DebuffLand(
        ILogger logger, int skillId, int level, int magicLevel, int activateRate, string trait,
        double traitMod, double mAtkMod, double chance, bool landed, int attackerId, int targetId);

    public static void DebuffLand(
        int skillId, int level, int magicLevel, int activateRate, string trait,
        double traitMod, double mAtkMod, double chance, bool landed, int attackerId, int targetId) =>
        DebuffLand(Log, skillId, level, magicLevel, activateRate, trait, traitMod, mAtkMod, chance, landed, attackerId, targetId);

  // --- AI ---
    [LoggerMessage(EventId = 3300, Level = LogLevel.Information,
        Message = "[skill] hit {SkillId} lv{Level} target={TargetId} damage={Damage}")]
    static partial void MobSkillHit(ILogger logger, int skillId, int level, int targetId, int damage);

    public static void MobSkillHit(int skillId, int level, int targetId, int damage) =>
        MobSkillHit(Log, skillId, level, targetId, damage);

    [LoggerMessage(EventId = 3301, Level = LogLevel.Information,
        Message = "[aggro] npc={NpcId} ({NpcName}) aggro on player={PlayerId} range={Range}")]
    static partial void AggroOnPlayer(ILogger logger, int npcId, string npcName, int playerId, int range);

    public static void AggroOnPlayer(int npcId, string npcName, int playerId, int range) =>
        AggroOnPlayer(Log, npcId, npcName, playerId, range);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Information,
        Message = "[aggro] guard={NpcId} ({NpcName}) aggro on monster={MonsterId} range={Range}")]
    static partial void GuardAggroMonster(ILogger logger, int npcId, string npcName, int monsterId, int range);

    public static void GuardAggroMonster(int npcId, string npcName, int monsterId, int range) =>
        GuardAggroMonster(Log, npcId, npcName, monsterId, range);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Information,
        Message = "[npc-skill] cast {SkillId} lv{Level} npc={NpcTemplateId} → player={PlayerId} hit={HitMs}ms")]
    static partial void NpcSkillCast(
        ILogger logger, int skillId, int level, int npcTemplateId, int playerId, int hitMs);

    public static void NpcSkillCast(int skillId, int level, int npcTemplateId, int playerId, int hitMs) =>
        NpcSkillCast(Log, skillId, level, npcTemplateId, playerId, hitMs);

    [LoggerMessage(EventId = 3304, Level = LogLevel.Information,
        Message = "[npc-skill] hit {SkillId} lv{Level} damage={Damage}")]
    static partial void NpcSkillHit(ILogger logger, int skillId, int level, int damage);

    public static void NpcSkillHit(int skillId, int level, int damage) =>
        NpcSkillHit(Log, skillId, level, damage);

    [LoggerMessage(EventId = 3305, Level = LogLevel.Information,
        Message = "[npc-support] heal {SkillId} npc={NpcTemplateId} {Before}->{After} (+{Amount})")]
    static partial void NpcSupportHeal(
        ILogger logger, int skillId, int npcTemplateId, int before, int after, int amount);

    public static void NpcSupportHeal(int skillId, int npcTemplateId, int before, int after, int amount) =>
        NpcSupportHeal(Log, skillId, npcTemplateId, before, after, amount);

    [LoggerMessage(EventId = 3306, Level = LogLevel.Information,
        Message = "[npc-support] {Kind} {SkillId} lv{Level} slot={Slot} on={TargetId}")]
    static partial void NpcSupportBuff(
        ILogger logger, string kind, int skillId, int level, string slot, int targetId);

    public static void NpcSupportBuff(string kind, int skillId, int level, string slot, int targetId) =>
        NpcSupportBuff(Log, kind, skillId, level, slot, targetId);

    [LoggerMessage(EventId = 3307, Level = LogLevel.Information,
        Message = "[npc-suicide] cast {SkillId} lv{Level} npc={NpcTemplateId} power={Power} range={Range} hit={HitMs}ms")]
    static partial void NpcSuicideCast(
        ILogger logger, int skillId, int level, int npcTemplateId, int power, int range, int hitMs);

    public static void NpcSuicideCast(int skillId, int level, int npcTemplateId, int power, int range, int hitMs) =>
        NpcSuicideCast(Log, skillId, level, npcTemplateId, power, range, hitMs);

    [LoggerMessage(EventId = 3308, Level = LogLevel.Information,
        Message = "[npc-support] cast {Kind} {SkillId} lv{Level} npc={NpcTemplateId} → {TargetId} hit={HitMs}ms")]
    static partial void NpcSupportCast(
        ILogger logger, string kind, int skillId, int level, int npcTemplateId, int targetId, int hitMs);

    public static void NpcSupportCast(
        string kind, int skillId, int level, int npcTemplateId, int targetId, int hitMs) =>
        NpcSupportCast(Log, kind, skillId, level, npcTemplateId, targetId, hitMs);

    [LoggerMessage(EventId = 3309, Level = LogLevel.Warning,
        Message = "skill-stat-effects missing skillId={SkillId} — abnormal has no combat mods")]
    static partial void SkillStatEffectsMissing(ILogger logger, int skillId);

    public static void SkillStatEffectsMissing(int skillId) => SkillStatEffectsMissing(Log, skillId);

    [LoggerMessage(EventId = 3310, Level = LogLevel.Information,
        Message = "[signet] missing npc-ai-support row npcId={NpcId}")]
    static partial void SignetMissingAiSupport(ILogger logger, int npcId);

    public static void SignetMissingAiSupport(int npcId) => SignetMissingAiSupport(Log, npcId);

    [LoggerMessage(EventId = 3311, Level = LogLevel.Information,
        Message = "[signet] pulse {SkillId} lv{Level} npc={NpcId} range={Range} hit={HitCount}")]
    static partial void SignetPulse(
        ILogger logger, int skillId, int level, int npcId, int range, int hitCount);

    public static void SignetPulse(int skillId, int level, int npcId, int range, int hitCount) =>
        SignetPulse(Log, skillId, level, npcId, range, hitCount);

    [LoggerMessage(EventId = 3312, Level = LogLevel.Information,
        Message = "[signet] skills.json id={SkillId} missing levelData for level={Level} signetNpcId={NpcId} source=npc-ai-support")]
    static partial void SignetMissingSkillLevel(
        ILogger logger, int skillId, int level, int npcId);

    public static void SignetMissingSkillLevel(int skillId, int level, int npcId) =>
        SignetMissingSkillLevel(Log, skillId, level, npcId);

    [LoggerMessage(EventId = 3313, Level = LogLevel.Information,
        Message = "[signet] missing skill catalog id={SkillId} npc={NpcId}")]
    static partial void SignetMissingSkillCatalog(ILogger logger, int skillId, int npcId);

    public static void SignetMissingSkillCatalog(int skillId, int npcId) =>
        SignetMissingSkillCatalog(Log, skillId, npcId);

    [LoggerMessage(EventId = 3314, Level = LogLevel.Information,
        Message = "[signet] hostile pulse {SkillId} lv{Level} npc={NpcId} range={Range} landed={Landed}")]
    static partial void SignetHostilePulse(
        ILogger logger, int skillId, int level, int npcId, int range, int landed);

    public static void SignetHostilePulse(int skillId, int level, int npcId, int range, int landed) =>
        SignetHostilePulse(Log, skillId, level, npcId, range, landed);
}