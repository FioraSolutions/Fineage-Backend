using System.Text.Json.Serialization;

namespace Fineage.Zone.Punishment;

public enum PunishmentAffect
{
    Account,
    Character
}

public enum PunishmentType
{
    Ban,
    ChatBan,
    Jail
}

/// <summary>One active or historical punishment row (Mobius <c>punishments</c> table).</summary>
public sealed class PunishmentRow
{
  public int Version { get; set; } = 1;

  /// <summary>Account name or character id string.</summary>
  public string Key { get; set; } = "";

  public PunishmentAffect Affect { get; set; }

  public PunishmentType Type { get; set; }

  /// <summary>Unix ms when the punishment ends; 0 = permanent / until removed.</summary>
  public long ExpirationUnixMs { get; set; }

  public string Reason { get; set; } = "";

  public string PunishedBy { get; set; } = "";
}

public sealed class PunishmentSnapshot
{
  public int Version { get; set; } = 1;

  public List<PunishmentRow> Rows { get; set; } = [];
}