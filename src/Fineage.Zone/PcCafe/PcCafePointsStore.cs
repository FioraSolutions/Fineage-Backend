using System.Text.Json;
using System.Text.Json.Serialization;
using Fineage.Game.Persistence;
using Fineage.Protocol;

namespace Fineage.Zone.PcCafe;

public sealed class PcCafeSnapshot : IVersionedStateSnapshot
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    public List<PcCafeCharacterRow> Characters { get; set; } = [];
}

public interface IPcCafeStore
{
    PcCafeSnapshot Load();
    void Save(PcCafeSnapshot snapshot);
}

public sealed class MemoryPcCafeStore : IPcCafeStore
{
    string _json = "";

    public PcCafeSnapshot Load() =>
        _json.Length == 0 ? new PcCafeSnapshot() : JsonSerializer.Deserialize<PcCafeSnapshot>(_json) ?? new PcCafeSnapshot();

    public void Save(PcCafeSnapshot snapshot) => _json = JsonSerializer.Serialize(snapshot);
}

public sealed class PcCafeCharacterRow
{
    public int CharacterId { get; set; }
    public int Points { get; set; }
    public List<string> RedeemedCodes { get; set; } = [];
}

/// <summary>Persisted PC Cafe point balances and redeemed coupon codes.</summary>
public sealed class PcCafePointsManager
{
    readonly object _gate = new();
    readonly IPcCafeStore _store;
    readonly Dictionary<int, PcCafeCharacterRow> _byId = [];

    /// <summary>
    /// #996: the points live in the database (<see cref="Persistence.StoredPcCafeStore"/>). Before, the zone never wired
    /// this manager at all and every world kept them in <c>pc-cafe.json</c> under the temp directory, and a file that
    /// did not parse was logged and treated as nobody having points.
    /// </summary>
    public PcCafePointsManager(IPcCafeStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        foreach (var row in _store.Load().Characters ?? [])
        {
            if (row.CharacterId <= 0)
                throw new InvalidDataException($"pc-cafe state: character id {row.CharacterId} is not valid");
            row.RedeemedCodes ??= [];
            _byId[row.CharacterId] = row;
        }
    }

    public int PointsOf(int characterId)
    {
        lock (_gate)
            return _byId.TryGetValue(characterId, out var row) ? row.Points : 0;
    }

    public bool WasRedeemed(int characterId, string code)
    {
        lock (_gate)
            return _byId.TryGetValue(characterId, out var row)
                && row.RedeemedCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Adds points up to <see cref="PcCafeProtocol.MaxPoints"/>. Returns the applied delta.</summary>
    public int AddPoints(int characterId, int delta)
    {
        if (delta == 0)
            return 0;
        lock (_gate)
        {
            var row = Ensure(characterId);
            int before = row.Points;
            long next = (long)row.Points + delta;
            if (next < 0)
                next = 0;
            if (next > PcCafeProtocol.MaxPoints)
                next = PcCafeProtocol.MaxPoints;
            row.Points = (int)next;
            SaveLocked();
            return row.Points - before;
        }
    }

    public void MarkRedeemed(int characterId, string code)
    {
        lock (_gate)
        {
            var row = Ensure(characterId);
            if (!row.RedeemedCodes.Contains(code, StringComparer.OrdinalIgnoreCase))
                row.RedeemedCodes.Add(code);
            SaveLocked();
        }
    }

    /// <summary>Every character's row, for the stored-state audit.</summary>
    public IReadOnlyList<PcCafeCharacterRow> Rows
    {
        get
        {
            lock (_gate)
                return _byId.Values.OrderBy(r => r.CharacterId).ToList();
        }
    }

    /// <summary>The shutdown registry's flush.</summary>
    public void Save()
    {
        lock (_gate)
            SaveLocked();
    }

    PcCafeCharacterRow Ensure(int characterId)
    {
        if (_byId.TryGetValue(characterId, out var row))
            return row;
        row = new PcCafeCharacterRow { CharacterId = characterId };
        _byId[characterId] = row;
        return row;
    }

    void SaveLocked() =>
        _store.Save(new PcCafeSnapshot
        {
            Characters = _byId.Values.OrderBy(r => r.CharacterId).ToList()
        });
}
