using Fineage.Zone.Simulation;

namespace Fineage.Zone.Bosses;

/// <summary>A boss died at <paramref name="AtUnixMs"/> (combat death or the admin mark-dead).</summary>
public readonly record struct BossKill(int NpcId, long AtUnixMs);

/// <summary>
/// The raid and grand boss schedule as a global actor: a kill rolls its respawn window on the actor's own stream when
/// it is reported, starting at the kill's time. Kills in region bodies are reported at the stage barrier, so the rolls
/// happen in the same order in every executor. The step returns the bosses that are due for the world to materialize.
/// </summary>
public sealed class BossActor : GlobalActor<BossSpawnOrder>
{
    public const string StageName = "bosses";

    readonly BossSchedule _schedule;
    readonly IBossRandom _windows;

    public BossActor(BossSchedule schedule, Random random)
        : base(StageName, ActorTime.Simulation)
    {
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _windows = new InclusiveBossRandom(random ?? throw new ArgumentNullException(nameof(random)));
    }

    /// <summary>Static datapack rows; safe to read anywhere.</summary>
    public BossCatalog Catalog => _schedule.Catalog;

    public BossSchedule Schedule
    {
        get
        {
            RequireActorThread();
            return _schedule;
        }
    }

    public void Kill(BossKill kill)
    {
        RequireActorThread();
        _schedule.OnBossKilled(kill.NpcId, kill.AtUnixMs, _windows);
    }

    protected override void OnStep(long nowUnixMs, List<BossSpawnOrder> effects)
    {
        effects.AddRange(_schedule.CollectDue(nowUnixMs));
    }
}
