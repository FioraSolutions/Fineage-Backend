namespace Fineage.Game.Punishment;

/// <summary>
/// Login's account-ban check. The bans are the zone's punishment state in the shared database (#996): login reads the
/// saved <see cref="PunishmentSnapshot"/> on each check, so a ban the zone committed applies to the next login on any
/// host. Before, login read <c>punishments.json</c> beside the zone and, when that file was absent (another host, a
/// different state directory), logged "bans are not enforced" and let everyone in.
/// </summary>
public sealed class AccountBanGate
{
    public static AccountBanGate Disabled { get; } = new(null);

    readonly Func<PunishmentSnapshot?>? _load;

    /// <param name="load">
    /// The saved punishments, or null when the zone has never saved any (nobody is punished). A saved state that
    /// cannot be read throws, and the login fails with that error rather than skipping the check.
    /// </param>
    public AccountBanGate(Func<PunishmentSnapshot?>? load) => _load = load;

    public bool IsAccountLoginBanned(string account, long nowUnixMs, out string message)
    {
        message = "";
        if (_load == null || string.IsNullOrWhiteSpace(account))
            return false;
        var snapshot = _load();
        if (snapshot == null)
            return false;
        foreach (var row in snapshot.Rows ?? [])
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
