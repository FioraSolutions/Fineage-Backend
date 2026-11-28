using Fineage.Zone.Npcs;
using Fineage.Zone.Net;
using Fineage.Net.Contract;
using Fineage.Zone.World;
using Xunit;

namespace Fineage.Zone.Tests;

/// <summary>
/// Mobius NPC equipment / attack type / range: the catalog carries rhand, lhand and
/// <c>stats/attack type</c> for every template, and NpcInfo (EntitySpawn) fans the hands out
/// so the client can pick the weapon attack clip — including bow guards at any distance.
/// </summary>
public sealed class NpcWeaponCatalogTests
{
    static readonly NpcCatalog Catalog = NpcCatalog.LoadDefault();

    [Fact]
    public void Elf_bow_sentinel_carries_elven_bow_and_bow_type()
    {
        var berros = Catalog.Get(30217); // Berros, Elven Village Sentinel
        Assert.Equal("BOW", berros.AttackType);
        Assert.Equal(276, berros.Rhand); // Elven Bow
        Assert.Equal(0, berros.Lhand);
        Assert.Equal(1100, berros.AttackRange);

        var kendell = Catalog.Get(30218);
        Assert.Equal("BOW", kendell.AttackType);
        Assert.Equal(276, kendell.Rhand);
        Assert.Equal(1100, kendell.AttackRange);
    }

    [Fact]
    public void Elf_melee_sentinel_carries_a_sword()
    {
        var wheeler = Catalog.Get(30216); // Wheeler — Elven Sword
        Assert.Equal("SWORD", wheeler.AttackType);
        Assert.Equal(130, wheeler.Rhand);
        Assert.Equal(40, wheeler.AttackRange);
    }

    [Fact]
    public void Talking_island_guards_split_bow_and_sword()
    {
        var leon = Catalog.Get(30040); // Long Bow
        Assert.Equal("BOW", leon.AttackType);
        Assert.Equal(275, leon.Rhand);
        Assert.Equal(1100, leon.AttackRange);

        var abellos = Catalog.Get(30042);
        Assert.Equal("SWORD", abellos.AttackType);
        Assert.Equal(2, abellos.Rhand); // Long Sword
        Assert.Equal(20, abellos.Lhand); // Buckler
        Assert.Equal(40, abellos.AttackRange);
    }

    [Fact]
    public void Giran_castle_bow_guard_uses_interlude_bow_range()
    {
        var guard = Catalog.Get(35156);
        Assert.Equal("BOW", guard.AttackType);
        Assert.Equal(276, guard.Rhand);
        Assert.Equal(500, guard.AttackRange);
    }

    [Fact]
    public void Catalog_has_a_bow_row_for_every_mobius_bow_template()
    {
        int bows = 0;
        foreach (var npc in Catalog.ById.Values)
        {
            if (string.Equals(npc.AttackType, "BOW", StringComparison.OrdinalIgnoreCase))
                bows++;
        }
        // Mobius Interlude stats/npcs: 299 attack type=BOW templates (top-level <npc> only).
        Assert.Equal(299, bows);
    }

    [Fact]
    public void EntitySpawn_carries_npc_rhand_and_old_payloads_still_decode()
    {
        var msg = new EntitySpawnMsg
        {
            EntityValue = 9,
            EntityGeneration = 1,
            Kind = EntityKindNet.Npc,
            Name = "Berros",
            TemplateId = 30217,
            Rhand = 276,
            Lhand = 0
        };
        var decoded = NetCodec.DecodeEntitySpawn(NetCodec.EncodeEntitySpawn(msg));
        Assert.Equal(276, decoded.Rhand);
        Assert.Equal(0, decoded.Lhand);

        // A peer from before this tail still decodes (Rhand stays 0).
        byte[] full = NetCodec.EncodeEntitySpawn(msg);
        byte[] older = full.AsSpan(0, full.Length - 8).ToArray();
        var legacy = NetCodec.DecodeEntitySpawn(older);
        Assert.Equal("Berros", legacy.Name);
        Assert.Equal(0, legacy.Rhand);
        Assert.Equal(0, legacy.Lhand);
    }

    [Fact]
    public void Npc_presence_spawn_includes_template_weapon()
    {
        var entity = new ZoneEntity
        {
            ObjectId = 42,
            Kind = ZoneEntityKind.Npc,
            Name = "Berros",
            NpcId = 30217,
            Rhand = 276,
            Lhand = 0,
            AttackType = "BOW",
            AttackRange = 1100,
            MaxHp = 100,
            CurHp = 100
        };
        var spawn = NetEnterWorldProjector.BuildNpcSpawn(entity, new EntityId(42, 1));
        Assert.Equal(276, spawn.Rhand);
        Assert.Equal(0, spawn.Lhand);
        Assert.Equal(30217, spawn.TemplateId);
    }
}
