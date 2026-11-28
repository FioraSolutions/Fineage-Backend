using Fineage.Protocol;
using Fineage.Zone.Petition;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    string? TryAdminPetition(ZoneSession session, ZoneEntity gm, string verb, string args)
    {
        if (verb.Equals("admin_view_petitions", StringComparison.OrdinalIgnoreCase))
            return AdminViewPetitions(session, gm);
        if (verb.Equals("admin_view_petition", StringComparison.OrdinalIgnoreCase))
            return AdminViewPetition(session, gm, args);
        if (verb.Equals("admin_accept_petition", StringComparison.OrdinalIgnoreCase))
            return AdminAcceptPetition(session, gm, args);
        if (verb.Equals("admin_reject_petition", StringComparison.OrdinalIgnoreCase))
            return AdminRejectPetition(session, gm, args);
        if (verb.Equals("admin_reset_petitions", StringComparison.OrdinalIgnoreCase))
            return AdminResetPetitions(session, gm);
        if (verb.Equals("admin_force_peti", StringComparison.OrdinalIgnoreCase))
            return AdminForcePetition(session, gm, args);
        return null;
    }

    string? AdminViewPetitions(ZoneSession session, ZoneEntity gm)
    {
        var pending = Petitions.PendingOrdered();
        if (pending.Count == 0)
        {
            FanGmSystem(session, "There are no currently pending petitions.");
            return null;
        }

        FanGmSystem(session, $"Pending petitions: {pending.Count}");
        foreach (var p in pending)
            FanGmSystem(session, $"#{p.Id} {p.PetitionerName} type={p.Type} state={p.State}");
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "view_petitions", "", $"count={pending.Count}");
        return null;
    }

    string? AdminViewPetition(ZoneSession session, ZoneEntity gm, string args)
    {
        if (!int.TryParse(args.Trim(), out int id) || id <= 0)
        {
            FanGmSystem(session, "Usage: admin_view_petition <id>");
            return "bad_request";
        }

        if (!Petitions.TryGet(id, out var entry))
        {
            ZoneLog.Warn($"[petition] missing petition id={id} view by gm={gm.ObjectId}");
            FanGmSystem(session, $"Petition #{id} not found.");
            return "bad_request";
        }

        FanGmSystem(session, $"Petition #{entry.Id} from {entry.PetitionerName}: {entry.Content}");
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "view_petition", entry.PetitionerName, $"id={id}");
        return null;
    }

    string? AdminAcceptPetition(ZoneSession session, ZoneEntity gm, string args)
    {
        if (!int.TryParse(args.Trim(), out int id) || id <= 0)
        {
            FanGmSystem(session, "Usage: admin_accept_petition <id>");
            return "bad_request";
        }

        if (!Petitions.Accept(gm.ObjectId, gm.Name, id, out var entry) || entry == null)
        {
            FanGmSystem(session, $"Could not accept petition #{id}.");
            return "bad_request";
        }

        if (_byId.TryGetValue(entry.PetitionerObjectId, out var petitioner) && petitioner.Session != null)
            SelfLine(petitioner.Session, PetitionProtocol.ApplicationAcceptedSm, PetitionProtocol.AcceptedText);

        FanGmSystem(session, $"Accepted petition #{entry.Id} from {entry.PetitionerName}.");
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "accept_petition", entry.PetitionerName, $"id={id}");
        return null;
    }

    string? AdminRejectPetition(ZoneSession session, ZoneEntity gm, string args)
    {
        if (!int.TryParse(args.Trim(), out int id) || id <= 0)
        {
            FanGmSystem(session, "Usage: admin_reject_petition <id>");
            return "bad_request";
        }

        if (!Petitions.Reject(gm.ObjectId, id, out var entry) || entry == null)
        {
            FanGmSystem(session, $"Could not reject petition #{id}.");
            return "bad_request";
        }

        FanGmSystem(session, $"Rejected petition #{entry.Id}.");
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "reject_petition", entry.PetitionerName, $"id={id}");
        return null;
    }

    string? AdminResetPetitions(ZoneSession session, ZoneEntity gm)
    {
        int n = Petitions.ResetPending();
        FanGmSystem(session, $"Reset {n} pending petition(s).");
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "reset_petitions", "", $"count={n}");
        return null;
    }

    string? AdminForcePetition(ZoneSession session, ZoneEntity gm, string args)
    {
        // Mobius: admin_force_peti <player> <message…>
        string trimmed = args.Trim();
        if (trimmed.Length == 0)
        {
            FanGmSystem(session, "Usage: admin_force_peti <player> <message>");
            return "bad_request";
        }

        int space = trimmed.IndexOf(' ');
        string name = space < 0 ? trimmed : trimmed[..space];
        string content = space < 0 ? "Forced petition" : trimmed[(space + 1)..].Trim();
        var target = FindOnlinePlayer(name);
        if (target == null)
        {
            FanGmSystem(session, "That player is not online.");
            return "bad_request";
        }

        if (content.Length > PetitionProtocol.MaxContentLength)
            content = content[..PetitionProtocol.MaxContentLength];

        int id = Petitions.Submit(target.ObjectId, target.Name, content, PetitionProtocol.MinType);
        FanGmSystem(session, $"Forced petition #{id} for {target.Name}.");
        _punishments.RecordGmAction(gm.Name, gm.ObjectId, "force_peti", target.Name, $"id={id}");
        return null;
    }
}
