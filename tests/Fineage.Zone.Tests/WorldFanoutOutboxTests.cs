using System.Reflection;
using Fineage.Protocol;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Step 4 outbox: per-step batches, coalescing per (kind, subject, audience), and step-ordered sends.</summary>
public sealed class WorldFanoutOutboxTests
{
    [Fact]
    public void Status_updates_for_different_audiences_are_all_delivered()
    {
        var (bridge, rec, fanout) = Outbox();

        fanout.Begin();
        bridge.FanoutStatusUpdate([1, 2], Status(7, (Hp, 50)));
        bridge.FanoutStatusUpdate([1], Status(7, (Exp, 900)));
        fanout.Seal();
        fanout.Drain();

        Assert.Equal(["[1,2] 7 9=50", "[1] 7 11=900"], rec.Sent);
    }

    [Fact]
    public void Status_updates_for_one_audience_merge_attributes_with_the_latest_values_at_the_last_write()
    {
        var (bridge, rec, fanout) = Outbox();
        var first = Status(7, (Hp, 50), (Mp, 10));

        fanout.Begin();
        bridge.FanoutStatusUpdate([1], first);
        bridge.FanoutAttackStance([1], 7, true);
        bridge.FanoutStatusUpdate([1], Status(7, (Hp, 40)));
        fanout.Seal();
        fanout.Drain();

        Assert.Equal(["stance 7", "[1] 7 10=10,9=40"], rec.Sent);
        Assert.Equal([Hp, Mp], first.AttributeIds);
        Assert.Equal([50, 10], first.Values);
    }

    [Fact]
    public void A_single_move_keeps_its_place_before_later_ordered_events()
    {
        var (bridge, rec, fanout) = Outbox();

        fanout.Begin();
        bridge.FanoutMoveState([1], 7, 0, 0, 0, 100, 0, 0, true);
        bridge.FanoutAttackStance([1], 7, true);
        bridge.FanoutMoveState([1], 8, 0, 0, 0, 5, 0, 0, true);
        bridge.FanoutMoveState([1], 8, 0, 0, 0, 6, 0, 0, true);
        fanout.Seal();
        fanout.Drain();

        Assert.Equal(["move 7 to 100", "stance 7", "move 8 to 6"], rec.Sent);
    }

    [Fact]
    public void World_reading_fanout_is_prepared_when_recorded_not_when_sent()
    {
        var (bridge, rec, fanout) = Outbox();

        fanout.Begin();
        rec.WorldState = "before";
        bridge.FanoutKnownlistDiff(1, [7], []);
        rec.WorldState = "after";
        fanout.Seal();
        fanout.Drain();

        Assert.Equal(["diff 1 seen before"], rec.Sent);
    }

    [Fact]
    public void Watcher_lists_are_copied_when_recorded()
    {
        var (bridge, rec, fanout) = Outbox();
        var watchers = new List<int> { 1 };

        fanout.Begin();
        bridge.FanoutAttackStance(watchers, 7, true);
        watchers.Add(2);
        fanout.Seal();
        fanout.Drain();

        Assert.Equal(["stance 7"], rec.Sent);
        Assert.Equal([1], rec.LastAudience);
    }

    [Fact]
    public void A_failed_send_is_logged_and_the_rest_of_the_batch_still_goes_out()
    {
        var (bridge, rec, fanout) = Outbox();

        fanout.Begin();
        bridge.FanoutAttackStance([1], 7, true);
        bridge.FanoutAttackStance([1], FakePublisher.Throws, true);
        bridge.FanoutAttackStance([1], 8, true);
        fanout.Seal();
        fanout.Drain();

        Assert.Equal(["stance 7", "stance 8"], rec.Sent);
    }

    [Fact]
    public void A_send_outside_a_step_waits_for_sealed_batches_and_surfaces_its_own_failure()
    {
        var (bridge, rec, fanout) = Outbox();

        fanout.Begin();
        bridge.FanoutAttackStance([1], 7, true);
        fanout.Seal();
        bridge.FanoutAttackStance([1], 8, true);

        Assert.Equal(["stance 7", "stance 8"], rec.Sent);
        Assert.Throws<InvalidOperationException>(() => bridge.FanoutAttackStance([1], FakePublisher.Throws, true));
    }

    [Fact]
    public void Seal_without_begin_is_rejected()
    {
        var fanout = new WorldFanoutSequencer();
        Assert.Throws<InvalidOperationException>(fanout.Seal);
    }

    [Fact]
    public void Sends_from_steps_and_handlers_on_several_threads_arrive_in_step_order()
    {
        var (bridge, rec, fanout) = Outbox();
        var gate = new object();
        int next = 0;
        const int perThread = 400;

        void Step()
        {
            for (int i = 0; i < perThread; i++)
            {
                lock (gate)
                {
                    fanout.Begin();
                    bridge.FanoutAttackStance([1], ++next, true);
                    bridge.FanoutAttackStance([1], ++next, true);
                    fanout.Seal();
                }

                fanout.Drain();
            }
        }

        void Handler()
        {
            for (int i = 0; i < perThread; i++)
            {
                lock (gate)
                    bridge.FanoutAttackStance([1], ++next, true);
            }
        }

        Task.WaitAll(Task.Run(Step), Task.Run(Step), Task.Run(Handler));
        fanout.Drain();

        Assert.Equal(Enumerable.Range(1, next).Select(n => $"stance {n}"), rec.Sent);
    }

    const int Hp = 9;
    const int Mp = 10;
    const int Exp = 11;

    static StatusUpdateMsg Status(int objectId, params (int Id, int Value)[] attributes) => new()
    {
        ObjectId = objectId,
        AttributeIds = [.. attributes.Select(a => a.Id)],
        Values = [.. attributes.Select(a => a.Value)]
    };

    static (IWorldEventPublisher Bridge, FakePublisher Rec, WorldFanoutSequencer Fanout) Outbox()
    {
        var inner = DispatchProxy.Create<IWorldEventPublisher, FakePublisher>();
        var fanout = new WorldFanoutSequencer();
        return (new OutboxingWorldEventPublisher(inner, fanout), (FakePublisher)(object)inner, fanout);
    }

    public class FakePublisher : DispatchProxy
    {
        public const int Throws = -1;

        public List<string> Sent { get; } = [];

        public string WorldState { get; set; } = "";

        public int[] LastAudience { get; private set; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            args ??= [];
            switch (method?.Name)
            {
                case nameof(IWorldEventPublisher.PrepareKnownlistDiff):
                    string seen = WorldState;
                    return (Action)(() => Add($"diff {args[0]} seen {seen}"));
                case nameof(IWorldEventPublisher.FanoutAttackStance):
                    if ((int)args[1]! == Throws)
                        throw new InvalidOperationException("send failed");
                    LastAudience = [.. (IReadOnlyList<int>)args[0]!];
                    Add($"stance {args[1]}");
                    return null;
                case nameof(IWorldEventPublisher.FanoutMoveState):
                    Add($"move {args[1]} to {args[5]}");
                    return null;
                case nameof(IWorldEventPublisher.FanoutStatusUpdate):
                    var status = (StatusUpdateMsg)args[1]!;
                    string attributes = string.Join(",", status.AttributeIds.Zip(status.Values, (id, v) => $"{id}={v}"));
                    Add($"[{string.Join(",", (IReadOnlyList<int>)args[0]!)}] {status.ObjectId} {attributes}");
                    return null;
                default:
                    throw new InvalidOperationException($"unexpected call {method?.Name}");
            }
        }

        void Add(string line)
        {
            lock (Sent)
                Sent.Add(line);
        }
    }
}
