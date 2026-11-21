namespace Fineage.Game.Punishment;

public sealed class AccountBanGate
{
    public static AccountBanGate Disabled { get; } = new("", null, true);
    readonly JsonPunishmentStore? _store;
    readonly Action<string> _log;
    readonly bool _disable;
    bool _loggedMissingStore;
    public static AccountBanGate FromEnvironment(Action<string>? log = null) =>
      ForStateDirectory(PunishmentStatePaths.ResolveStateDirectory(), log);
    public static AccountBanGate ForStateDirectory(string stateDirectory, Action<string>? log = null) =>
      ForPath(PunishmentStatePaths.PunishmentsJsonPath(stateDirectory), log);
    public static AccountBanGate ForPath(string punishmentsJsonPath, Action<string>? log = null) => new(punishmentsJsonPath, log);
    AccountBanGate(string punishmentsJsonPath, Action<string>? log, bool disable = false)
    {
        _disable = disable;
        _log = log ?? (message => Console.WriteLine("[LOGIN] " + message));
        _store = disable ? null : new JsonPunishmentStore(punishmentsJsonPath);
    }
    public string? StorePath => _store?.Path;
    public bool IsAccountLoginBanned(string account, long nowUnixMs, out string message)
    {
        message = "";
        if (_disable || _store == null || string.IsNullOrWhiteSpace(account)) return false;
        if (!File.Exists(_store.Path))
        {
            if (!_loggedMissingStore)
            {
                _loggedMissingStore = true;
                _log($"punishment store not found at {_store.Path}; account login bans are not enforced");
            }
            return false;
        }
        foreach (var row in _store.Load().Rows)
        {
            if (row.Affect != PunishmentAffect.Account || row.Type != PunishmentType.Ban) continue;
            if (!string.Equals(row.Key, account.Trim(), StringComparison.Ordinal)) continue;
            if (row.ExpirationUnixMs > 0 && nowUnixMs > row.ExpirationUnixMs) continue;
            message = BanMessage(row);
            return true;
        }
        return false;
    }
    public static string BanMessage(PunishmentRow row) =>
      string.IsNullOrWhiteSpace(row.Reason)
        ? (row.ExpirationUnixMs <= 0 ? "This account is permanently banned from login." : "This account is banned from login.")
        : row.Reason;
}
