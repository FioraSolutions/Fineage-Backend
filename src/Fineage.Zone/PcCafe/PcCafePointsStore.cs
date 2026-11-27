using System.Text.Json;
using Fineage.Protocol;

namespace Fineage.Zone.PcCafe;

public sealed class PcCafeSnapshot
{
    public List<PcCafeCharacterRow> Characters { get; set; } = [];
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
    readonly string _path;
    readonly Dictionary<int, PcCafeCharacterRow> _byId = [];
    readonly Action<string> _warn;

    public PcCafePointsManager(string stateDirectory, Action<string>? warn = null)
    {
        Directory.CreateDirectory(stateDirectory);
        _path = Path.Combine(stateDirectory, "pc-cafe.json");
        _warn = warn ?? (msg => ZoneLog.Warn(msg));
        Load();
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
            Save();
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
            Save();
        }
    }

    PcCafeCharacterRow Ensure(int characterId)
    {
        if (_byId.TryGetValue(characterId, out var row))
            return row;
        row = new PcCafeCharacterRow { CharacterId = characterId };
        _byId[characterId] = row;
        return row;
    }

    void Load()
    {
        if (!File.Exists(_path))
            return;
        try
        {
            var snap = JsonSerializer.Deserialize<PcCafeSnapshot>(File.ReadAllText(_path)) ?? new();
            foreach (var row in snap.Characters)
            {
                if (row.CharacterId > 0)
                    _byId[row.CharacterId] = row;
            }
        }
        catch (Exception ex)
        {
            _warn($"[pc-cafe] failed to load {_path}: {ex.Message}");
        }
    }

    void Save()
    {
        var snap = new PcCafeSnapshot
        {
            Characters = _byId.Values.OrderBy(r => r.CharacterId).ToList()
        };
        File.WriteAllText(_path, JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }));
    }
}
