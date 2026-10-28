using Fineage.Login;
using Fineage.Zone.Drops;
using Fineage.Zone.Interest;
using Fineage.Zone.World;

namespace Fineage.Zone.Tests;

static class MobiusDropFixtures
{
    public static ZoneWorld WorldForDropTests(IAoi? aoi = null, IAccountStore? accounts = null) =>
        aoi == null
            ? new ZoneWorld(
                spawnRandom: new Random(MobiusDropTestSeed.DefaultSeed),
                dropRandom: new Random(MobiusDropTestSeed.DefaultSeed),
                accounts: accounts)
            : new ZoneWorld(
                aoi,
                spawnRandom: new Random(MobiusDropTestSeed.DefaultSeed),
                dropRandom: new Random(MobiusDropTestSeed.DefaultSeed),
                accounts: accounts);
}
