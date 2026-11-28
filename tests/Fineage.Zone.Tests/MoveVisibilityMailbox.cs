using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.World;
using Fineage.Game.Economy;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// In-memory move/visibility assertions via <see cref="NetBridgeRecorder"/> and
/// <see cref="ManualSimulationDriver"/> — no sockets or quiet-window drains.
/// </summary>
internal sealed class MoveVisibilityMailbox
{
    public const int Range = 2000;
    public const int X0 = NetTestKit.ElfX;
    public const int Y0 = NetTestKit.ElfY;
    public const int Z0 = NetTestKit.ElfZ;

    readonly MutableClock _clock = new() { Now = ManualSimulationDriver.FixedStartUtc };

    public MoveVisibilityMailbox(IAoi? aoi = null, GeoEngine? geo = null)
    {
        Bench = new WorldBench(
            aoi: aoi ?? new RangeAoi(Range),
            geo: geo,
            clock: () => _clock.Now,
            spawns: SpawnCatalog.From(new Dictionary<string, SpawnMapDefinition>
            {
                [SpawnCatalog.DefaultMapId] = new(SpawnCatalog.DefaultMapId, true, [])
            }),
            ledgerJournal: new MemoryLedgerJournal(),
            allowAmbientBoats: false);
        Driver = new ManualSimulationDriver(Bench.World, _clock);
    }

    public WorldBench Bench { get; }
    public ManualSimulationDriver Driver { get; }
    public NetBridgeRecorder Rec => Bench.Rec;

    public int CallCount => Rec.Calls.Count;

    public async Task<BenchPlayer> EnterAsync(int x, int y, int z = Z0)
    {
        var p = await Bench.EnterAsync(x: x, y: y, z: z, clearRecorder: true);
        return p;
    }

    public void Step(int n = 1) => Driver.Step(n);

    public void AdvanceMs(int totalMs) => Driver.Advance(TimeSpan.FromMilliseconds(totalMs));

    /// <summary>Moves the world clock without running a tick, to exercise a command's own in-step advance.</summary>
    public void AdvanceClockOnly(int totalMs) => _clock.Now = _clock.Now.AddMilliseconds(totalMs);

    public DateTimeOffset Now => _clock.Now;

    public static List<object?[]> MovesFor(NetBridgeRecorder rec, int watcherObjectId, int moverObjectId) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutMoveState))
            .Where(a => ((IReadOnlyList<int>)a[0]!).Contains(watcherObjectId) && (int)a[1]! == moverObjectId)
            .ToList();

    public static List<object?[]> KnownAdds(NetBridgeRecorder rec, int watcherObjectId, int subjectObjectId) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutKnownlistDiff))
            .Where(a => (int)a[0]! == watcherObjectId && ((IReadOnlyList<int>)a[1]!).Contains(subjectObjectId))
            .ToList();

    public static List<object?[]> KnownRemoves(NetBridgeRecorder rec, int watcherObjectId, int subjectObjectId) =>
        rec.Of(nameof(IWorldEventPublisher.FanoutKnownlistDiff))
            .Where(a => (int)a[0]! == watcherObjectId && ((IReadOnlyList<int>)a[2]!).Contains(subjectObjectId))
            .ToList();

    public static (int originX, int originY, int destX, int destY, bool running) DecodeMove(object?[] args) =>
        ((int)args[2]!, (int)args[3]!, (int)args[5]!, (int)args[6]!, (bool)args[8]!);
}