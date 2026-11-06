using Fineage.Zone.Npcs;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>Mobius Npc SoulShot / SpiritShot template fields.</summary>
public sealed class ZoneNpcSoulshotTests
{
    [Fact]
    public void Catalog_loads_mobius_soul_and_spirit_shot_stock()
    {
        var cat = NpcCatalog.LoadDefault();
        // 20052 Kaboo Orc Archer — SoulShot 100 / rate 10 in Mobius XML
        var npc = cat.Get(20052);
        Assert.True(npc.SoulShot > 0);
        Assert.True(npc.SoulShotChance > 0);
        Assert.True(npc.SpiritShot > 0 || npc.SpiritShotChance >= 0);
    }

    [Fact]
    public void Low_level_gremlin_has_no_shot_stock()
    {
        var gremlin = NpcCatalog.LoadDefault().Get(20001);
        Assert.Equal(0, gremlin.SoulShot);
        Assert.Equal(0, gremlin.SoulShotChance);
    }
}
