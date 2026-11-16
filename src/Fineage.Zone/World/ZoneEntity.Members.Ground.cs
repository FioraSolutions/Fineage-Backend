using Fineage.Zone.Geo;
using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Chat;
using Fineage.Zone.Combat;
using Fineage.Zone.Interest;
using Fineage.Zone.Spawns;
using Fineage.Zone.Drops;
using Fineage.Zone.Bosses;
using Fineage.Game.Shops;
using Fineage.Zone.NpcHtml;
using Fineage.Zone.Shops;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Dungeons;
using Fineage.Zone.Siege;
using Fineage.Zone.Olympiad;
using Fineage.Game.Services;

namespace Fineage.Zone.World;

sealed partial class ZoneEntity
{
    public ZoneGroundItemPayload? Ground { get; set; }

    public int GroundItemId
    {
        get => Ground?.ItemId ?? 0;
        set => EnsureGround().ItemId = value;
    }

    public int GroundCount
    {
        get => Ground?.Count ?? 1;
        set => EnsureGround().Count = value;
    }

    public bool GroundStackable
    {
        get => Ground?.Stackable ?? false;
        set => EnsureGround().Stackable = value;
    }

    ZoneGroundItemPayload EnsureGround() => Ground ??= new ZoneGroundItemPayload();
    /// <summary>Mobius DropProtection owner (killer); 0 = anyone may pick up.</summary>
    public int GroundOwnerId
    {
        get => Ground?.OwnerId ?? 0;
        set => EnsureGround().OwnerId = value;
    }

    /// <summary>Unix ms until only <see cref="GroundOwnerId"/> (or their party) may pick up.</summary>
    public long GroundProtectUntilMs
    {
        get => Ground?.ProtectUntilMs ?? 0;
        set => EnsureGround().ProtectUntilMs = value;
    }

    /// <summary>Mobius ItemsAutoDestroy: unix ms when the ground item is removed; 0 = never.</summary>
    public long GroundExpireAtMs
    {
        get => Ground?.ExpireAtMs ?? 0;
        set => EnsureGround().ExpireAtMs = value;
    }
    /// <summary>NPC spoil/sweep payload; null until first spoil access (#353 composition).</summary>
    public ZoneNpcSpoilPayload? Spoil { get; set; }

    /// <summary>Mobius spoil: who activated spoil on this attackable (0 = none).</summary>
    public int SpoilerObjectId
    {
        get => Spoil?.SpoilerObjectId ?? 0;
        set => EnsureSpoil().SpoilerObjectId = value;
    }

    /// <summary>Pre-rolled corpse drops after death when spoiled; cleared by sweeper.</summary>
    public List<Drops.DropStack> SweepItems => EnsureSpoil().SweepItems;

    /// <summary>Dead NPC kept in world until sweeper takes <see cref="SweepItems"/>.</summary>
    public bool CorpseAwaitingSweep
    {
        get => Spoil?.CorpseAwaitingSweep ?? false;
        set => EnsureSpoil().CorpseAwaitingSweep = value;
    }

    /// <summary>Manor seed state (Mobius Attackable seed fields); 0 / false when nothing was sown.</summary>
    public int SeedId { get => Spoil?.SeedId ?? 0; set => EnsureSpoil().SeedId = value; }
    public int SeederObjectId { get => Spoil?.SeederObjectId ?? 0; set => EnsureSpoil().SeederObjectId = value; }
    public bool Seeded { get => Spoil?.Seeded ?? false; set => EnsureSpoil().Seeded = value; }
    public int HarvestItemId { get => Spoil?.HarvestItemId ?? 0; set => EnsureSpoil().HarvestItemId = value; }
    public int HarvestCount { get => Spoil?.HarvestCount ?? 0; set => EnsureSpoil().HarvestCount = value; }

    /// <summary>Mobius <c>Attackable.onSpawn</c> / decay: forget the seed and the crop.</summary>
    public void ClearSeed()
    {
        if (Spoil == null)
            return;
        Spoil.SeedId = 0;
        Spoil.SeederObjectId = 0;
        Spoil.Seeded = false;
        Spoil.HarvestItemId = 0;
        Spoil.HarvestCount = 0;
    }

    ZoneNpcSpoilPayload EnsureSpoil() => Spoil ??= new ZoneNpcSpoilPayload();
}
