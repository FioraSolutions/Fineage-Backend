using System.Text.Json.Serialization;

namespace Fineage.Game.Punishment;

public enum PunishmentAffect { Account, Character }
public enum PunishmentType { Ban, ChatBan, Jail }

public sealed class PunishmentRow
{
    public int Version { get; set; } = 1;
    public string Key { get; set; } = "";
    public PunishmentAffect Affect { get; set; }
    public PunishmentType Type { get; set; }
    public long ExpirationUnixMs { get; set; }
    public string Reason { get; set; } = "";
    public string PunishedBy { get; set; } = "";
}

public sealed class PunishmentSnapshot
{
    public int Version { get; set; } = 1;
    public List<PunishmentRow> Rows { get; set; } = [];
}
