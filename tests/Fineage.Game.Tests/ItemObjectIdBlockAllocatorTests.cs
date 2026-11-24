using Xunit;

namespace Fineage.Game.Tests;

public sealed class ItemObjectIdBlockAllocatorTests
{
    sealed class SharedCounter
    {
        int _next = PlayerInventory.FirstObjectId;
        public int Calls;

        public int Reserve(int count)
        {
            Interlocked.Increment(ref Calls);
            return Interlocked.Add(ref _next, count) - count;
        }
    }

    [Fact]
    public void Two_processes_on_one_shared_counter_never_hand_out_the_same_id()
    {
        var shared = new SharedCounter();
        var zone = new ItemObjectIdAllocator(shared.Reserve, blockSize: 8);
        var login = new ItemObjectIdAllocator(shared.Reserve, blockSize: 8);
        var seen = new HashSet<int>();
        for (int i = 0; i < 200; i++)
        {
            Assert.True(seen.Add(zone.Next()));
            Assert.True(seen.Add(login.Next()));
        }
    }

    [Fact]
    public void Next_block_is_prefetched_at_half_use()
    {
        var shared = new SharedCounter();
        var ids = new ItemObjectIdAllocator(shared.Reserve, blockSize: 16);
        for (int i = 0; i < 9; i++)
            ids.Next();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref shared.Calls) == 2, TimeSpan.FromSeconds(5)));
        Thread.Sleep(50); // let the prefetch task publish its result
        int firstOfSecond = 0;
        for (int i = 0; i < 8; i++)
            firstOfSecond = ids.Next();
        Assert.Equal(PlayerInventory.FirstObjectId + 16, firstOfSecond);
        Assert.Equal(0, ids.BlockingRefills);
    }

    [Fact]
    public void Floor_is_respected_and_foreign_ids_do_not_move_the_cursor()
    {
        var shared = new SharedCounter();
        var ids = new ItemObjectIdAllocator(shared.Reserve, blockSize: 4096, floor: PlayerInventory.FirstObjectId + 10);
        Assert.Equal(PlayerInventory.FirstObjectId + 10, ids.PeekNext());
        ids.Observe(PlayerInventory.FirstObjectId + 1_000_000);
        Assert.Equal(PlayerInventory.FirstObjectId + 10, ids.PeekNext());
        ids.Observe(PlayerInventory.FirstObjectId + 20);
        Assert.Equal(PlayerInventory.FirstObjectId + 21, ids.PeekNext());
    }

    [Fact]
    public void Reservation_failure_is_not_swallowed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ItemObjectIdAllocator(_ => throw new InvalidOperationException("store down"), blockSize: 4));
    }
}
