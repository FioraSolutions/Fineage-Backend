using System.Collections.Concurrent;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

public sealed class FanoutDispatcherBackpressureTests
{
    [Fact]
    public void Enqueue_blocks_when_the_queue_is_full_instead_of_dropping_batches()
    {
        var holdWorker = new ManualResetEventSlim(false);
        using var dispatcher = new FanoutDispatcher(batchQueueCapacity: 1)
        {
            HoldWorkerBeforeReadForTest = holdWorker,
        };
        var delivered = new ConcurrentQueue<ulong>();

        var first = Batch(1, () => delivered.Enqueue(1));
        var second = Batch(2, () => delivered.Enqueue(2));

        dispatcher.Enqueue(first);
        var blocked = Task.Run(() => dispatcher.Enqueue(second));
        Assert.False(blocked.Wait(TimeSpan.FromMilliseconds(300)));

        holdWorker.Set();
        blocked.GetAwaiter().GetResult();
        dispatcher.WaitUntilIdle();

        Assert.Equal(new ulong[] { 1, 2 }, delivered.ToArray());
    }

    [Fact]
    public void Enqueue_after_close_logs_sequence_and_throws_without_delivering_the_batch()
    {
        using var dispatcher = new FanoutDispatcher(batchQueueCapacity: 4);
        var delivered = new ConcurrentQueue<ulong>();

        dispatcher.Enqueue(Batch(1, () => delivered.Enqueue(1)));
        dispatcher.Dispose();

        var rejected = Batch(99, () => delivered.Enqueue(99));
        var ex = Assert.Throws<InvalidOperationException>(() => dispatcher.Enqueue(rejected));
        Assert.Contains("tick=42", ex.Message);
        Assert.Contains("phase=3", ex.Message);
        Assert.Contains("seq=99", ex.Message);
        Assert.DoesNotContain(delivered, n => n == 99);
    }

    static SealedWorldFanoutBatch Batch(ulong sequence, Action send)
    {
        var batch = new WorldFanoutBatch();
        batch.AddOrdered("test", send);
        return new SealedWorldFanoutBatch(batch, new WorldEventSequence(42, 3, sequence), 0);
    }
}