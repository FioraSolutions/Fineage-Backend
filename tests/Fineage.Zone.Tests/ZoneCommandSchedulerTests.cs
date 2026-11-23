using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class ZoneCommandSchedulerTests
{
    [Fact]
    public async Task Deficit_round_robin_serves_quiet_players_while_one_player_floods()
    {
        var ingress = new ZoneCommandIngress(Limits(quantum: 4, costCap: 24));
        var flood = Session(1);
        var quietA = Session(2);
        var quietB = Session(3);
        for (int i = 0; i < 30; i++)
            _ = ingress.Submit(flood, new CancelTargetCommand(), CancellationToken.None);
        _ = ingress.Submit(quietA, new CancelTargetCommand(), CancellationToken.None);
        _ = ingress.Submit(quietB, new CancelTargetCommand(), CancellationToken.None);

        var order = await DrainAsync(ingress);

        Assert.Equal(12, order.Count);
        Assert.Equal([1, 1, 2, 3], order.Take(4));
        Assert.Equal(1, order.Count(id => id == 2));
        Assert.Equal(1, order.Count(id => id == 3));
        Assert.Equal(10, order.Count(id => id == 1));
        Assert.All(order.Skip(4), id => Assert.Equal(1, id));
    }

    [Fact]
    public async Task Consecutive_moves_collapse_to_the_latest_and_a_non_move_is_a_boundary()
    {
        var ingress = new ZoneCommandIngress(Limits());
        var session = Session(4);

        var first = ingress.Submit(session, new MoveCommand(1, 0, 0), CancellationToken.None);
        var second = ingress.Submit(session, new MoveCommand(2, 0, 0), CancellationToken.None);
        var third = ingress.Submit(session, new MoveCommand(3, 0, 0), CancellationToken.None);
        Assert.Equal(ZoneCommandIngress.CoalescedReason, await first);
        Assert.Equal(ZoneCommandIngress.CoalescedReason, await second);
        Assert.Equal(1, ingress.QueuedDepth);

        var only = await TakeOneAsync(ingress);
        var move = Assert.IsType<MoveCommand>(only.Command);
        Assert.Equal(3, move.DestX);
        Assert.False(third.IsCompleted);

        var bounded = new ZoneCommandIngress(Limits());
        var player = Session(5);
        _ = bounded.Submit(player, new MoveCommand(10, 0, 0), CancellationToken.None);
        _ = bounded.Submit(player, new StopMoveCommand(), CancellationToken.None);
        _ = bounded.Submit(player, new MoveCommand(11, 0, 0), CancellationToken.None);
        _ = bounded.Submit(player, new AttackCommand(9, false), CancellationToken.None);
        _ = bounded.Submit(player, new MoveCommand(12, 0, 0), CancellationToken.None);
        Assert.Equal(5, bounded.QueuedDepth);

        var taken = await DrainAsync(bounded, player.ObjectId);
        Assert.Equal(5, taken.Count);
        Assert.Equal(10, Assert.IsType<MoveCommand>(taken[0].Command).DestX);
        Assert.IsType<StopMoveCommand>(taken[1].Command);
        Assert.Equal(11, Assert.IsType<MoveCommand>(taken[2].Command).DestX);
        Assert.IsType<AttackCommand>(taken[3].Command);
        Assert.Equal(12, Assert.IsType<MoveCommand>(taken[4].Command).DestX);
    }

    [Fact]
    public async Task Wrapped_moves_coalesce_and_do_not_cross_another_request()
    {
        var ingress = new ZoneCommandIngress(Limits());
        var session = Session(6);
        var firstMove = new MoveCommand(1, 2, 3);
        var latestMove = new MoveCommand(4, 5, 6);
        var first = ingress.Submit(session, new ZoneRequest<int>("move", _ => 1, firstMove), CancellationToken.None);
        var latest = ingress.Submit(session, new ZoneRequest<int>("move", _ => 2, latestMove), CancellationToken.None);
        Assert.Equal(ZoneCommandIngress.CoalescedReason, await first);
        Assert.Equal(1, ingress.QueuedDepth);

        var kept = await TakeOneAsync(ingress);
        var request = Assert.IsType<ZoneRequest<int>>(kept.Command);
        Assert.Same(latestMove, request.Wrapped);
        Assert.False(latest.IsCompleted);

        var bounded = new ZoneCommandIngress(Limits());
        _ = bounded.Submit(session, new ZoneRequest<int>("move", _ => 1, new MoveCommand(1, 0, 0)), CancellationToken.None);
        _ = bounded.Submit(session, new ZoneRequest<int>("attack", _ => 0, new AttackCommand(1, false)), CancellationToken.None);
        _ = bounded.Submit(session, new ZoneRequest<int>("move", _ => 2, new MoveCommand(2, 0, 0)), CancellationToken.None);
        Assert.Equal(3, bounded.QueuedDepth);
    }

    [Fact]
    public async Task Admission_rejects_depth_and_age_with_a_named_reason()
    {
        var depth = new ZoneCommandIngress(Limits(queueDepth: 2));
        var session = Session(7);
        _ = depth.Submit(session, new CancelTargetCommand(), CancellationToken.None);
        _ = depth.Submit(session, new CancelTargetCommand(), CancellationToken.None);
        var refused = await Assert.ThrowsAsync<ZoneIngressRefusal>(() =>
            depth.Submit(session, new AttackCommand(1, false), CancellationToken.None));
        Assert.Contains("rejected: queue depth 2 is at the limit 2", refused.Reason);
        Assert.Equal(2, depth.QueuedDepth);

        var aged = new ZoneCommandIngress(Limits(maxAgeMs: 1000));
        long start = 5_000_000_000;
        aged.ClockForTest = start;
        var pendingMove = aged.Submit(session, new MoveCommand(8, 0, 0), CancellationToken.None);
        aged.ClockForTest = start + StopwatchTicks(5_000);
        var stale = await Assert.ThrowsAsync<ZoneIngressRefusal>(() =>
            aged.Submit(session, new AttackCommand(1, false), CancellationToken.None));
        Assert.Contains("rejected: queue age", stale.Reason);
        Assert.Contains("exceeds 1000ms", stale.Reason);
        Assert.Equal(1, aged.QueuedDepth);

        var replaced = aged.Submit(session, new MoveCommand(9, 0, 0), CancellationToken.None);
        Assert.Equal(ZoneCommandIngress.CoalescedReason, await pendingMove);
        Assert.False(replaced.IsCompleted);
        Assert.Equal(1, aged.QueuedDepth);
    }

    [Fact]
    public async Task Wall_clock_slice_stops_at_the_deadline_and_banks_unused_time_for_a_backlog()
    {
        var box = new CommandApplyTimebox(nominalMs: 70, stepMs: 200);
        Assert.Equal(70, box.GrantMilliseconds(backlog: false));
        box.Account(10);
        Assert.Equal(60, box.CreditMs, 3);
        box.Account(0);
        Assert.Equal(130, box.CreditMs, 3);
        Assert.Equal(200, box.GrantMilliseconds(backlog: true));
        box.Account(200);
        Assert.Equal(0, box.CreditMs, 3);
        Assert.Equal(70, box.GrantMilliseconds(backlog: true));

        var ingress = new ZoneCommandIngress(Limits(nominalMs: 10, stepMs: 200));
        long start = 9_000_000_000;
        ingress.ClockForTest = start;
        ingress.BeginApply(stepStartedTimestamp: 0);
        ingress.FinishApply();
        Assert.InRange(ingress.BankedApplyMsForTest, 9.5, 10.5);

        var session = Session(8);
        for (int i = 0; i < 6; i++)
            _ = ingress.Submit(session, new CancelTargetCommand(), CancellationToken.None);
        ingress.ClockForTest = start;
        var slice = ingress.BeginApply(stepStartedTimestamp: 0);
        double grantMs = (slice.DeadlineTimestamp - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Assert.InRange(grantMs, 19.5, 20.5);

        int cost = slice.CostRemaining;
        ingress.ClockForTest = start;
        Assert.True(ingress.TryTake(slice, ref cost, out _));
        ingress.ClockForTest = slice.DeadlineTimestamp;
        Assert.False(ingress.TryTake(slice, ref cost, out _));
        Assert.Equal(5, ingress.QueuedDepth);
    }

    static ZoneCommandIngress.IngressLimits Limits(
        int queueDepth = 64,
        int maxAgeMs = 60_000,
        int quantum = 8,
        int costCap = int.MaxValue,
        double nominalMs = 70,
        double stepMs = 200) =>
        new(queueDepth, maxAgeMs, quantum, costCap, nominalMs, stepMs);

    static ZoneSession Session(int objectId)
    {
        var session = new ZoneSession();
        session.Bind(objectId, "load", "load", classId: 0);
        return session;
    }

    static async Task<List<int>> DrainAsync(ZoneCommandIngress ingress)
    {
        var slice = ingress.BeginApply(0);
        int cost = slice.CostRemaining;
        var order = new List<int>();
        while (ingress.TryTake(slice, ref cost, out var submission))
            order.Add(submission.Session.ObjectId);
        await Task.CompletedTask;
        return order;
    }

    static async Task<List<ZoneCommandIngress.Submission>> DrainAsync(ZoneCommandIngress ingress, int _)
    {
        var slice = ingress.BeginApply(0);
        int cost = slice.CostRemaining;
        var taken = new List<ZoneCommandIngress.Submission>();
        while (ingress.TryTake(slice, ref cost, out var submission))
            taken.Add(submission);
        await Task.CompletedTask;
        return taken;
    }

    static async Task<ZoneCommandIngress.Submission> TakeOneAsync(ZoneCommandIngress ingress)
    {
        var slice = ingress.BeginApply(0);
        int cost = slice.CostRemaining;
        Assert.True(ingress.TryTake(slice, ref cost, out var submission));
        await Task.CompletedTask;
        return submission;
    }

    static long StopwatchTicks(double milliseconds) =>
        (long)(milliseconds * System.Diagnostics.Stopwatch.Frequency / 1000.0);
}
