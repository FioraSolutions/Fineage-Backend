using System.Text.Json;
using Fineage.Protocol;
using Fineage.Zone.Punishment;
using Fineage.Zone.Security;

namespace Fineage.Zone.World;

/// <summary>
/// Shared admin audit capture. Every client-facing admin surface (HTML bypass, GM chat, Net admin
/// opcode) records the same versioned row: actor, action, target, before, after, source and outcome.
/// The sink is the punishment manager's <c>gm-audit.jsonl</c>, so punishments and admin actions share
/// one append-only history.
/// </summary>
public sealed partial class ZoneWorld
{
    internal const string AdminSourceBypass = "client-bypass";
    internal const string AdminSourceChat = "client-chat";
    internal const string AdminSourceNet = "net-opcode";
    internal const string AdminSourceInternal = "internal";

    GmAuditLog _gmAudit = null!;

    /// <summary>Read-only audit sink for tests and diagnostics.</summary>
    internal GmAuditLog GmAudit => _gmAudit;

    internal string AdminStateSnapshot(ZoneEntity? entity)
    {
        if (entity == null)
            return "";
        try
        {
            return JsonSerializer.Serialize(new
            {
                entity.ObjectId,
                entity.Name,
                kind = entity.Kind.ToString(),
                entity.X,
                entity.Y,
                entity.Z,
                entity.Level,
                entity.ClassId,
                entity.CurHp,
                entity.MaxHp,
                entity.CurMp,
                entity.MaxMp,
                entity.CurCp,
                entity.MaxCp,
                entity.Dead,
                adena = entity.Inventory?.CountOf(Fineage.Game.Shops.ShopTrade.AdenaItemId) ?? 0
            });
        }
        catch (Exception ex)
        {
            ZoneLog.Error($"[admin-audit] state snapshot failed object={entity.ObjectId} error={ex.GetType().Name}: {ex.Message}");
            return "";
        }
    }

    /// <summary>Best-effort target snapshot for the audit row, without letting an unknown name fail the action.</summary>
    internal string AdminAuditState(ZoneSession session, ZoneEntity? actor, string target)
    {
        if (actor == null && !_byId.TryGetValue(session.ObjectId, out actor))
            return "";
        string self = AdminStateSnapshot(actor);
        if (string.IsNullOrWhiteSpace(target) || string.Equals(target, actor.Name, StringComparison.OrdinalIgnoreCase))
            return self;
        var other = FindOnlinePlayer(target);
        return other == null || other.ObjectId == actor.ObjectId
            ? self
            : self + " target=" + AdminStateSnapshot(other);
    }

    internal void AuditAdmin(
        ZoneSession session,
        ZoneEntity? actor,
        string action,
        string target,
        string before,
        string after,
        string source,
        string? result)
    {
        string who = actor?.Name ?? session.Name;
        int whoId = actor?.ObjectId ?? session.ObjectId;
        var record = new GmAuditRecord(
            GmAuditRecord.CurrentVersion,
            _clock.UtcNow.ToUnixTimeMilliseconds(),
            who,
            whoId,
            action,
            target ?? "",
            before ?? "",
            after ?? "",
            source,
            result ?? "ok");
        try
        {
            _gmAudit.Append(record);
        }
        catch (Exception ex)
        {
            // Audit is a security requirement, not a silent best-effort. The action already ran on this
            // path, so surface the concrete failure instead of pretending it was recorded.
            ZoneLog.Error(
                $"[admin-audit] append failed path={_gmAudit.PathForTests} who={who} action={action} " +
                $"target={target} error={ex.GetType().Name}: {ex.Message}");
        }
    }

    internal void AuditAdminDenied(
        ZoneSession session,
        string action,
        string target,
        AdminRole actual,
        AdminRole required,
        string source)
    {
        AuditAdmin(
            session,
            actor: null,
            action,
            target,
            before: "",
            after: "",
            source,
            result: $"denied role={actual} required={required}");
    }

    /// <summary>
    /// Common Net-opcode gate. Direct world test/admin APIs are trusted internal calls; the client boundary
    /// is the Net handler, which must call this before submitting an admin command.
    /// </summary>
    internal bool TryAuthorizeAdmin(
        ZoneSession session,
        AdminRole required,
        string action,
        string source,
        out string refusal)
    {
        AdminRole actual = ZoneAdmin.RoleOf(session.Name);
        if (AdminCommandCatalog.Allows(actual, required))
        {
            refusal = "";
            return true;
        }

        AuditAdminDenied(session, action, target: "", actual, required, source);
        refusal = InventoryProtocol.Forbidden;
        return false;
    }

    /// <summary>Short, operator-readable target for a bypass/chat audit line.</summary>
    internal static string AdminAuditTarget(ZoneSession session, string? args)
    {
        string text = (args ?? "").Trim();
        if (text.Length > 0)
            return text.Length <= 200 ? text : text[..200];
        return session.SelectedTargetId > 0 ? $"selected:{session.SelectedTargetId}" : session.Name;
    }
}
