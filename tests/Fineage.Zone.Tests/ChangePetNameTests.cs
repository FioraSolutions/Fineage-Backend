using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Geo;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>#936: Mobius RequestChangePetName — name the pet once while it is out.</summary>
public sealed class ChangePetNameTests
{
    const int WolfCollar = 2375;
    const int X = 46045, Y = 41251, Z = -3440;
    static readonly CancellationToken Ct = CancellationToken.None;

    static (WorldBench Bench, MutableClock Clock) Open()
    {
        var clock = new MutableClock { Now = DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000) };
        return (new WorldBench(clock: () => clock.Now, geo: GeoEngine.Empty), clock);
    }

    static List<SystemMessageMsg> Lines(WorldBench bench) =>
        bench.Rec.Of("FanoutSystemMessage").Select(args => (SystemMessageMsg)args[1]!).ToList();

    static async Task<BenchPlayer> OwnerAsync(WorldBench bench, string name) =>
        await bench.EnterAsync(name.ToLowerInvariant(), name, [new StoredItem(57, 1000, -1), new StoredItem(WolfCollar, 1, -1)],
            level: 20, x: X, y: Y, z: Z);

    static int CollarOf(BenchPlayer owner) =>
        owner.Inventory.Items.Single(i => i.ItemId == WolfCollar).ObjectId;

    static async Task<ZoneEntity> SummonAsync(WorldBench bench, BenchPlayer owner)
    {
        owner.Player.CastingUntilMs = 0;
        owner.Player.CombatUntilUnixMs = 0;
        Assert.Null(await bench.World.TryUseItemAsync(owner.Session, CollarOf(owner), Ct));
        bench.World.ResolvePendingHitForTest(owner.Player);
        var pet = bench.World.SummonOfForTest(owner.Player);
        Assert.NotNull(pet);
        return pet!;
    }

    [Fact]
    public async Task Rename_sets_name_persists_and_refreshes_summon_info()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Renamer");
        var pet = await SummonAsync(bench, owner);
        Assert.Equal("", pet.Summon!.Pet!.Name);

        Assert.Null(await bench.World.TryChangePetNameAsync(
            owner.Session, new ChangePetNameMsg { Name = "Fluffy" }, Ct));

        Assert.Equal("Fluffy", pet.Summon!.Pet!.Name);
        Assert.Equal("Fluffy", pet.Name);
        var stored = bench.World.LoadPetForTest(CollarOf(owner));
        Assert.NotNull(stored);
        Assert.Equal("Fluffy", stored!.Name);

        var info = (SummonInfoNetMsg)bench.Rec.Of(nameof(IWorldEventPublisher.FanoutSummonInfo)).Last()[1]!;
        Assert.Equal("Fluffy", info.Name);
    }

    [Fact]
    public async Task Rename_twice_is_rejected()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Twice");
        await SummonAsync(bench, owner);

        Assert.Null(await bench.World.TryChangePetNameAsync(
            owner.Session, new ChangePetNameMsg { Name = "First" }, Ct));
        Assert.Null(await bench.World.TryChangePetNameAsync(
            owner.Session, new ChangePetNameMsg { Name = "Second" }, Ct));

        Assert.Equal("First", bench.World.SummonOfForTest(owner.Player)!.Summon!.Pet!.Name);
        Assert.Contains(Lines(bench), m => m.Id == ZoneWorld.SysMsgPetNameAlreadySet);
    }

    [Fact]
    public async Task Rename_invalid_name_is_rejected_and_logged_path()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "BadName");
        await SummonAsync(bench, owner);

        Assert.Null(await bench.World.TryChangePetNameAsync(
            owner.Session, new ChangePetNameMsg { Name = "Bad Name!" }, Ct));

        Assert.Equal("", bench.World.SummonOfForTest(owner.Player)!.Summon!.Pet!.Name);
        Assert.Contains(Lines(bench), m => m.Id == ZoneWorld.SysMsgPetNameInvalid);
    }

    [Fact]
    public async Task Rename_too_short_is_rejected()
    {
        var (bench, _) = Open();
        var owner = await OwnerAsync(bench, "Short");
        await SummonAsync(bench, owner);

        Assert.Null(await bench.World.TryChangePetNameAsync(
            owner.Session, new ChangePetNameMsg { Name = "Ab" }, Ct));

        Assert.Equal("", bench.World.SummonOfForTest(owner.Player)!.Summon!.Pet!.Name);
        Assert.Contains(Lines(bench), m => m.Text.Contains("16 characters", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rename_duplicate_name_same_npc_is_rejected()
    {
        var (bench, _) = Open();
        var first = await OwnerAsync(bench, "OwnerA");
        var second = await OwnerAsync(bench, "OwnerB");
        await SummonAsync(bench, first);
        await SummonAsync(bench, second);

        Assert.Null(await bench.World.TryChangePetNameAsync(
            first.Session, new ChangePetNameMsg { Name = "Shared" }, Ct));
        Assert.Null(await bench.World.TryChangePetNameAsync(
            second.Session, new ChangePetNameMsg { Name = "Shared" }, Ct));

        Assert.Equal("Shared", bench.World.SummonOfForTest(first.Player)!.Summon!.Pet!.Name);
        Assert.Equal("", bench.World.SummonOfForTest(second.Player)!.Summon!.Pet!.Name);
        Assert.Contains(Lines(bench), m => m.Id == ZoneWorld.SysMsgPetNameInUse);
    }

    [Fact]
    public void Codec_roundtrips_change_pet_name()
    {
        var bytes = NetCodec.EncodeChangePetName(new ChangePetNameNetMsg { Name = "Wolfie" });
        var decoded = NetCodec.DecodeChangePetName(bytes);
        Assert.Equal("Wolfie", decoded.Name);
    }
}
