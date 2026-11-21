using Fineage.Accounts;
using Fineage.Game;
using Fineage.Protocol;

namespace Fineage.Zone.CursedWeapons;

/// <summary>Mobius <c>CursedWeaponsManager</c>: spawn, owner, drop on death, disappearance.</summary>
public sealed class CursedWeaponsManager
{
    readonly CursedWeaponsCatalog _catalog;
    readonly Dictionary<int, CursedWeaponRuntime> _byItemId;
    readonly object _gate = new();

    public CursedWeaponsManager(CursedWeaponsCatalog catalog, IReadOnlyList<CursedWeaponRecord>? restored = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _byItemId = new Dictionary<int, CursedWeaponRuntime>(catalog.Blades.Count);
        foreach (var definition in catalog.Blades)
            _byItemId[definition.ItemId] = new CursedWeaponRuntime(definition);
        if (restored != null)
        {
            foreach (var row in restored)
            {
                if (!_byItemId.TryGetValue(row.ItemId, out var runtime))
                    continue;
                runtime.Activated = row.Activated;
                runtime.Dropped = row.Dropped;
                runtime.OwnerObjectId = row.CharacterId;
                runtime.PlayerReputation = row.PlayerReputation;
                runtime.PlayerPkKills = row.PlayerPkKills;
                runtime.NbKills = row.NbKills;
                runtime.EndTimeUnixMs = row.EndTimeUnixMs;
                runtime.GroundObjectId = row.GroundObjectId;
            }
        }
    }

    public CursedWeaponsCatalog Catalog => _catalog;

    public IEnumerable<CursedWeaponRuntime> All
    {
        get
        {
            lock (_gate)
            {
                foreach (var pair in _byItemId)
                    yield return pair.Value;
            }
        }
    }

    public bool IsCursedItem(int itemId) => _catalog.TryGet(itemId, out _);

    public CursedWeaponRuntime? Get(int itemId)
    {
        lock (_gate)
            return _byItemId.TryGetValue(itemId, out var runtime) ? runtime : null;
    }

    public int EquippedItemId(int ownerObjectId)
    {
        if (ownerObjectId <= 0)
            return 0;
        lock (_gate)
        {
            foreach (var runtime in _byItemId.Values)
            {
                if (runtime.Activated && runtime.OwnerObjectId == ownerObjectId)
                    return runtime.Definition.ItemId;
            }
        }

        return 0;
    }

    public bool WieldsBlade(int ownerObjectId) => EquippedItemId(ownerObjectId) > 0;

    public IReadOnlyList<int> ActiveItemIds()
    {
        lock (_gate)
        {
            var list = new List<int>(_byItemId.Count);
            foreach (var pair in _byItemId)
            {
                if (pair.Value.IsActive)
                    list.Add(pair.Key);
            }

            return list;
        }
    }

    public CursedWeaponRecord ToSave(CursedWeaponRuntime runtime) => new()
    {
        ItemId = runtime.Definition.ItemId,
        CharacterId = runtime.OwnerObjectId,
        PlayerReputation = runtime.PlayerReputation,
        PlayerPkKills = runtime.PlayerPkKills,
        NbKills = runtime.NbKills,
        EndTimeUnixMs = runtime.EndTimeUnixMs,
        Activated = runtime.Activated,
        Dropped = runtime.Dropped,
        GroundObjectId = runtime.GroundObjectId
    };

    public IReadOnlyList<CursedWeaponRecord> ExportSaves()
    {
        lock (_gate)
        {
            var rows = new List<CursedWeaponRecord>(_byItemId.Count);
            foreach (var runtime in _byItemId.Values)
            {
                if (runtime.IsActive || runtime.EndTimeUnixMs > 0)
                    rows.Add(ToSave(runtime));
            }

            return rows;
        }
    }

    /// <summary>Test hook: force blade active on a player without a drop roll.</summary>
    public bool TryMarkOwnedForTest(int itemId, int ownerObjectId, long endTimeUnixMs)
    {
        lock (_gate)
        {
            if (!_byItemId.TryGetValue(itemId, out var runtime) || runtime.IsActive)
                return false;
            runtime.Activated = true;
            runtime.OwnerObjectId = ownerObjectId;
            runtime.EndTimeUnixMs = endTimeUnixMs;
            return true;
        }
    }

    public void ClearForTest()
    {
        lock (_gate)
        {
            foreach (var runtime in _byItemId.Values)
                runtime.Reset();
        }
    }

    public static bool NpcExcludedFromDrop(string npcType) =>
        npcType is "Defender" or "Guard" or "GrandBoss" or "FeedableBeast" or "FortCommander";

    public static int TransformFor(int itemId) => itemId switch
    {
        OlympiadProtocol.Zariche => 301,
        OlympiadProtocol.Akamanah => 302,
        _ => 0
    };

    public static bool InventoryHasCursedWeapon(PlayerInventory? inventory)
    {
        if (inventory == null)
            return false;
        foreach (int itemId in CursedWeaponIds.All)
        {
            if (inventory.CountOf(itemId) > 0)
                return true;
        }

        return false;
    }
}