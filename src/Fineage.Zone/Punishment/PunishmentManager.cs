using Fineage.Accounts;

namespace Fineage.Zone.Punishment;

public interface IPunishmentSink
{
    void OnPunishmentStarted(PunishmentRow row);
    void OnPunishmentEnded(PunishmentRow row);
}

/// <summary>
/// Config-driven Mobius <c>PunishmentManager</c>: persisted punishments, typed handlers, no silent failures.
/// </summary>
public sealed class PunishmentManager
{
    readonly IPunishmentStore _store;
    readonly JailZoneCatalog _jail;
    readonly GmAuditLog _audit;
    readonly object _gate = new();
    readonly List<PunishmentRow> _rows = [];
    IPunishmentSink? _sink;
    IAccountStore? _accounts;

    public PunishmentManager(IPunishmentStore store, JailZoneCatalog jail, GmAuditLog audit)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _jail = jail ?? throw new ArgumentNullException(nameof(jail));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        Load();
    }

    public JailZoneCatalog Jail => _jail;

    public void Bind(IAccountStore? accounts, IPunishmentSink sink)
    {
        _accounts = accounts;
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        foreach (var row in SnapshotActive())
            _sink.OnPunishmentStarted(row);
    }

    public static PunishmentManager OpenInStateDirectory(string stateDirectory)
    {
        string dir = string.IsNullOrWhiteSpace(stateDirectory) ? "data" : stateDirectory;
        string path = Path.Combine(dir, "punishments.json");
        return new PunishmentManager(
          new JsonPunishmentStore(path),
          JailZoneCatalog.LoadDefault(),
          GmAuditLog.ForStateDirectory(dir));
    }

    void Load()
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var snapshot = _store.Load();
        int expired = 0;
        lock (_gate)
        {
            _rows.Clear();
            foreach (var row in snapshot.Rows)
            {
                if (IsExpired(row, now))
                {
                    expired++;
                    continue;
                }

                _rows.Add(Clone(row));
            }
        }

        ZoneLog.Info($"[punishment] loaded {_rows.Count} active, {expired} expired from store");
    }

    public void Save()
    {
        lock (_gate)
        {
            _store.Save(new PunishmentSnapshot { Rows = _rows.Select(Clone).ToList() });
        }
    }

    public bool HasPunishment(string key, PunishmentAffect affect, PunishmentType type)
    {
        lock (_gate)
            return Find(key, affect, type) != null;
    }

    public long GetExpirationUnixMs(string key, PunishmentAffect affect, PunishmentType type)
    {
        lock (_gate)
            return Find(key, affect, type)?.ExpirationUnixMs ?? 0;
    }

    public bool IsBanned(string account, int characterId, long nowUnixMs)
    {
        if (HasActive(PunishmentAffect.Account, PunishmentType.Ban, account, nowUnixMs))
            return true;
        return HasActive(PunishmentAffect.Character, PunishmentType.Ban, characterId.ToString(), nowUnixMs);
    }

    public bool IsJailed(int characterId, long nowUnixMs) =>
      HasActive(PunishmentAffect.Character, PunishmentType.Jail, characterId.ToString(), nowUnixMs);

    /// <summary>Session mapping uses -1 for no ban, 0 for permanent, &gt;0 for timed expiry unix ms.</summary>
    public long ChatBanUntilUnixMs(int characterId, long nowUnixMs)
    {
        var row = GetActive(PunishmentAffect.Character, PunishmentType.ChatBan, characterId.ToString(), nowUnixMs);
        if (row == null)
            return -1;
        return row.ExpirationUnixMs;
    }

    bool HasActive(PunishmentAffect affect, PunishmentType type, string key, long nowUnixMs)
    {
        lock (_gate)
            return Find(key, affect, type) is { } row && !IsExpired(row, nowUnixMs);
    }

    PunishmentRow? GetActive(PunishmentAffect affect, PunishmentType type, string key, long nowUnixMs)
    {
        lock (_gate)
        {
            var row = Find(key, affect, type);
            if (row == null || IsExpired(row, nowUnixMs))
                return null;
            return row;
        }
    }

    public string? StartPunishment(
      string key,
      PunishmentAffect affect,
      PunishmentType type,
      long durationMinutes,
      string reason,
      string punishedBy,
      int gmObjectId,
      long nowUnixMs)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            ZoneLog.Warn("[punishment] start refused: empty key");
            return "bad_request";
        }

        if (string.IsNullOrWhiteSpace(reason))
            reason = type.ToString();
        if (string.IsNullOrWhiteSpace(punishedBy))
            punishedBy = "GM";

        if (type == PunishmentType.Ban && affect == PunishmentAffect.Account && _accounts == null)
        {
            ZoneLog.Warn($"[punishment] account ban for '{key}' recorded but IAccountStore is unavailable for login enforcement");
        }

        long expiration = durationMinutes <= 0
          ? 0
          : nowUnixMs + durationMinutes * 60_000L;

        lock (_gate)
        {
            if (Find(key, affect, type) != null)
            {
                ZoneLog.Info($"[punishment] duplicate refused key={key} affect={affect} type={type}");
                return "already_active";
            }

            var row = new PunishmentRow
            {
                Key = key,
                Affect = affect,
                Type = type,
                ExpirationUnixMs = expiration,
                Reason = reason,
                PunishedBy = punishedBy
            };
            _rows.Add(row);
            Save();
            _audit.Append(punishedBy, gmObjectId, $"start_{type}", $"{affect}:{key}", reason);
            _sink?.OnPunishmentStarted(row);
            ZoneLog.Info($"[punishment] started type={type} affect={affect} key={key} expires={expiration}");
            return null;
        }
    }

    public string? StopPunishment(
      string key,
      PunishmentAffect affect,
      PunishmentType type,
      string stoppedBy,
      int gmObjectId)
    {
        lock (_gate)
        {
            var row = Find(key, affect, type);
            if (row == null)
            {
                ZoneLog.Info($"[punishment] stop ignored missing key={key} affect={affect} type={type}");
                return "not_active";
            }

            _rows.Remove(row);
            Save();
            _audit.Append(stoppedBy, gmObjectId, $"stop_{type}", $"{affect}:{key}", "");
            _sink?.OnPunishmentEnded(row);
            ZoneLog.Info($"[punishment] stopped type={type} affect={affect} key={key}");
            return null;
        }
    }

    public IReadOnlyList<PunishmentRow> SnapshotActive() =>
      SnapshotAt(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public IReadOnlyList<PunishmentRow> SnapshotAt(long nowUnixMs)
    {
        lock (_gate)
            return _rows.Where(r => !IsExpired(r, nowUnixMs)).Select(Clone).ToList();
    }

    /// <summary>Remove every punishment whose expiration passed; returns rows that ended.</summary>
    public List<PunishmentRow> TickExpirations(long nowUnixMs)
    {
        var ended = new List<PunishmentRow>();
        lock (_gate)
        {
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                var row = _rows[i];
                if (row.ExpirationUnixMs <= 0 || row.ExpirationUnixMs > nowUnixMs)
                    continue;
                _rows.RemoveAt(i);
                ended.Add(Clone(row));
            }

            if (ended.Count > 0)
                Save();
        }

        foreach (var row in ended)
        {
            _sink?.OnPunishmentEnded(row);
            ZoneLog.Info($"[punishment] expired type={row.Type} affect={row.Affect} key={row.Key}");
        }

        return ended;
    }

    public string ResolveCharacterKey(string nameOrId)
    {
        if (int.TryParse(nameOrId, out int id) && id > 0)
            return id.ToString();
        if (_accounts == null)
        {
            ZoneLog.Warn($"[punishment] cannot resolve character name '{nameOrId}' without IAccountStore");
            return nameOrId;
        }

        var record = _accounts.FindByCharacterName(nameOrId);
        if (record == null)
        {
            ZoneLog.Warn($"[punishment] character name '{nameOrId}' does not exist");
            return "";
        }

        return record.Id.ToString();
    }

    PunishmentRow? Find(string key, PunishmentAffect affect, PunishmentType type)
    {
        foreach (var row in _rows)
        {
            if (row.Affect == affect && row.Type == type && string.Equals(row.Key, key, StringComparison.Ordinal))
                return row;
        }

        return null;
    }

    static bool IsExpired(PunishmentRow row, long nowUnixMs) =>
      row.ExpirationUnixMs > 0 && nowUnixMs > row.ExpirationUnixMs;

    static PunishmentRow Clone(PunishmentRow row) => new()
    {
        Version = row.Version,
        Key = row.Key,
        Affect = row.Affect,
        Type = row.Type,
        ExpirationUnixMs = row.ExpirationUnixMs,
        Reason = row.Reason,
        PunishedBy = row.PunishedBy
    };
}