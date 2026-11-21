using Fineage.Zone.Punishment;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
  static bool IsAdminPunishmentVerb(string verb) =>
    verb.Equals("admin_jail", StringComparison.OrdinalIgnoreCase)
    || verb.Equals("admin_unjail", StringComparison.OrdinalIgnoreCase)
    || verb.Equals("admin_ban_chat", StringComparison.OrdinalIgnoreCase)
    || verb.Equals("admin_unban_chat", StringComparison.OrdinalIgnoreCase)
    || verb.Equals("admin_ban_char", StringComparison.OrdinalIgnoreCase)
    || verb.Equals("admin_unban_char", StringComparison.OrdinalIgnoreCase)
    || verb.Equals("admin_ban_acc", StringComparison.OrdinalIgnoreCase)
    || verb.Equals("admin_unban_acc", StringComparison.OrdinalIgnoreCase);

  string? TryAdminPunishment(ZoneSession gmSession, ZoneEntity gm, string verb, string args)
  {
    long now = NowUnixMs();
    if (verb.Equals("admin_jail", StringComparison.OrdinalIgnoreCase))
      return AdminJail(gmSession, gm, args, now);
    if (verb.Equals("admin_unjail", StringComparison.OrdinalIgnoreCase))
      return AdminUnjail(gmSession, gm, args);
    if (verb.Equals("admin_ban_chat", StringComparison.OrdinalIgnoreCase))
      return AdminBanChat(gmSession, gm, args, now);
    if (verb.Equals("admin_unban_chat", StringComparison.OrdinalIgnoreCase))
      return AdminUnbanChat(gmSession, gm, args);
    if (verb.Equals("admin_ban_char", StringComparison.OrdinalIgnoreCase))
      return AdminBanChar(gmSession, gm, args, now);
    if (verb.Equals("admin_unban_char", StringComparison.OrdinalIgnoreCase))
      return AdminUnbanChar(gmSession, gm, args);
    if (verb.Equals("admin_ban_acc", StringComparison.OrdinalIgnoreCase))
      return AdminBanAccount(gmSession, gm, args, now);
    if (verb.Equals("admin_unban_acc", StringComparison.OrdinalIgnoreCase))
      return AdminUnbanAccount(gmSession, gm, args);
    return null;
  }

  string? AdminJail(ZoneSession gmSession, ZoneEntity gm, string args, long nowUnixMs)
  {
    if (!TryParseTargetAndMinutes(args, out string target, out long minutes))
      return "bad_request";
    string key = _punishments.ResolveCharacterKey(target);
    if (key.Length == 0)
      return "bad_request";
    return _punishments.StartPunishment(
      key,
      PunishmentAffect.Character,
      PunishmentType.Jail,
      minutes,
      "Jailed by admin",
      gm.Name,
      gm.ObjectId,
      nowUnixMs);
  }

  string? AdminUnjail(ZoneSession gmSession, ZoneEntity gm, string args)
  {
    string target = args.Trim();
    if (target.Length == 0)
      return "bad_request";
    string key = _punishments.ResolveCharacterKey(target);
    if (key.Length == 0)
      return "bad_request";
    return _punishments.StopPunishment(key, PunishmentAffect.Character, PunishmentType.Jail, gm.Name, gm.ObjectId);
  }

  string? AdminBanChat(ZoneSession gmSession, ZoneEntity gm, string args, long nowUnixMs)
  {
    if (!TryParseTargetAndMinutes(args, out string target, out long minutes))
      return "bad_request";
    string key = _punishments.ResolveCharacterKey(target);
    if (key.Length == 0)
      return "bad_request";
    return _punishments.StartPunishment(
      key,
      PunishmentAffect.Character,
      PunishmentType.ChatBan,
      minutes,
      "Chat banned by admin",
      gm.Name,
      gm.ObjectId,
      nowUnixMs);
  }

  string? AdminUnbanChat(ZoneSession gmSession, ZoneEntity gm, string args)
  {
    string target = args.Trim();
    if (target.Length == 0)
      return "bad_request";
    string key = _punishments.ResolveCharacterKey(target);
    if (key.Length == 0)
      return "bad_request";
    return _punishments.StopPunishment(key, PunishmentAffect.Character, PunishmentType.ChatBan, gm.Name, gm.ObjectId);
  }

  string? AdminBanChar(ZoneSession gmSession, ZoneEntity gm, string args, long nowUnixMs)
  {
    string target = args.Trim();
    if (target.Length == 0)
      return "bad_request";
    string key = _punishments.ResolveCharacterKey(target);
    if (key.Length == 0)
      return "bad_request";
    return _punishments.StartPunishment(
      key,
      PunishmentAffect.Character,
      PunishmentType.Ban,
      0,
      "Banned by admin",
      gm.Name,
      gm.ObjectId,
      nowUnixMs);
  }

  string? AdminUnbanChar(ZoneSession gmSession, ZoneEntity gm, string args)
  {
    string target = args.Trim();
    if (target.Length == 0)
      return "bad_request";
    string key = _punishments.ResolveCharacterKey(target);
    if (key.Length == 0)
      return "bad_request";
    return _punishments.StopPunishment(key, PunishmentAffect.Character, PunishmentType.Ban, gm.Name, gm.ObjectId);
  }

  string? AdminBanAccount(ZoneSession gmSession, ZoneEntity gm, string args, long nowUnixMs)
  {
    string account = args.Trim();
    if (account.Length == 0)
      return "bad_request";
    if (_accounts == null)
    {
      ZoneLog.Warn($"admin_ban_acc account={account}: IAccountStore unavailable");
      return "bad_request";
    }

    if (!_accounts.AccountExists(account))
    {
      ZoneLog.Warn($"admin_ban_acc account={account} does not exist");
      return "bad_request";
    }

    return _punishments.StartPunishment(
      account,
      PunishmentAffect.Account,
      PunishmentType.Ban,
      0,
      "Banned by admin",
      gm.Name,
      gm.ObjectId,
      nowUnixMs);
  }

  string? AdminUnbanAccount(ZoneSession gmSession, ZoneEntity gm, string args)
  {
    string account = args.Trim();
    if (account.Length == 0)
      return "bad_request";
    return _punishments.StopPunishment(account, PunishmentAffect.Account, PunishmentType.Ban, gm.Name, gm.ObjectId);
  }

  static bool TryParseTargetAndMinutes(string args, out string target, out long minutes)
  {
    target = "";
    minutes = 0;
    string[] parts = args.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 1)
      return false;
    target = parts[0];
    if (parts.Length >= 2 && long.TryParse(parts[1], out long parsed))
      minutes = parsed;
    return target.Length > 0;
  }
}