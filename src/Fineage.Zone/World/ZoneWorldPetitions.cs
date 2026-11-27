using Fineage.Protocol;
using Fineage.Zone.Petition;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>PetitionManager</c> queue (#930).</summary>
    internal PetitionQueue Petitions { get; } = new(msg => ZoneLog.Warn(msg));

    /// <summary>When false (tests / no admin gate), skip the live-GM requirement.</summary>
    internal bool PetitionRequireOnlineGm { get; set; } = ZoneAdmin.Enabled;

    /// <summary>Mobius <c>RequestPetition</c>.</summary>
    public Task<string?> TryPetitionAsync(ZoneSession session, PetitionRequestMsg request, CancellationToken ct)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return Task.FromResult<string?>("world");

        int type = request.Type;
        if (type < PetitionProtocol.MinType || type > PetitionProtocol.MaxType)
            return Task.FromResult<string?>(null);

        if (PetitionRequireOnlineGm && !AnyOnlineGm())
        {
            SelfLine(session, PetitionProtocol.NoGmsSm, PetitionProtocol.NoGmsText);
            return Task.FromResult<string?>(null);
        }

        if (Petitions.IsPlayerPending(session.ObjectId))
        {
            SelfLine(session, PetitionProtocol.OneActiveSm, PetitionProtocol.OneActiveText);
            return Task.FromResult<string?>(null);
        }

        if (Petitions.PendingCount >= PetitionProtocol.MaxPending)
        {
            SelfLine(session, PetitionProtocol.UnavailableSm, PetitionProtocol.UnavailableText);
            return Task.FromResult<string?>(null);
        }

        int total = Petitions.PlayerTotalCount(session.ObjectId) + 1;
        if (total > PetitionProtocol.MaxPerPlayer)
        {
            SelfLine(session, PetitionProtocol.SubmittedTodaySm,
                $"You have submitted {total} petition(s). You may submit 0 more petition(s) today.");
            return Task.FromResult<string?>(null);
        }

        string content = request.Content ?? "";
        if (content.Length > PetitionProtocol.MaxContentLength)
        {
            SelfLine(session, PetitionProtocol.TooLongSm, PetitionProtocol.TooLongText);
            return Task.FromResult<string?>(null);
        }

        int id = Petitions.Submit(session.ObjectId, player.Name, content, type);
        SelfLine(session, PetitionProtocol.AcceptedSm,
            $"Your petition application has been accepted. Receipt No. is {id}.");
        SelfLine(session, PetitionProtocol.SubmittedTodaySm,
            $"You have submitted {total} petition(s). You may submit {PetitionProtocol.MaxPerPlayer - total} more petition(s) today.");
        SelfLine(session, PetitionProtocol.WaitingListSm,
            $"There are {Petitions.PendingCount} petitions currently on the waiting list.");

        NotifyGmsPetition($"{player.Name} has submitted a new petition.");
        return Task.FromResult<string?>(null);
    }

    /// <summary>Mobius <c>RequestPetitionCancel</c>.</summary>
    public Task<string?> TryPetitionCancelAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return Task.FromResult<string?>("world");

        if (Petitions.IsPlayerInConsultation(session.ObjectId))
        {
            // Mobius RequestPetitionCancel: only the GM responder ends an active consultation.
            if (Petitions.TryActiveFor(session.ObjectId, out var active)
                && active.ResponderObjectId == session.ObjectId)
            {
                Petitions.End(active.Id, PetitionState.Completed, out _);
            }
            else
            {
                SelfLine(session, PetitionProtocol.BeingProcessedSm, PetitionProtocol.BeingProcessedText);
            }

            return Task.FromResult<string?>(null);
        }

        if (!Petitions.IsPlayerPending(session.ObjectId))
        {
            SelfLine(session, PetitionProtocol.NotSubmittedSm, PetitionProtocol.NotSubmittedText);
            return Task.FromResult<string?>(null);
        }

        if (!Petitions.CancelByPlayer(session.ObjectId, out _))
        {
            SelfLine(session, PetitionProtocol.CancelFailedSm, "Failed to cancel petition. Please try again later.");
            return Task.FromResult<string?>(null);
        }

        int remaining = PetitionProtocol.MaxPerPlayer - Petitions.PlayerTotalCount(session.ObjectId);
        SelfLine(session, PetitionProtocol.CanceledSm,
            $"The petition was canceled. You may submit {remaining} more petition(s) today.");
        NotifyGmsPetition($"{player.Name} has canceled a pending petition.");
        return Task.FromResult<string?>(null);
    }

    /// <summary>GM accepts a pending petition into consultation (admin / test seam).</summary>
    public Task<string?> TryAcceptPetitionAsync(ZoneSession session, int petitionId, CancellationToken ct)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var gm)
            || gm.Kind != ZoneEntityKind.Player)
            return Task.FromResult<string?>("world");
        if (PetitionRequireOnlineGm && !ZoneAdmin.IsGmCharacter(gm.Name))
            return Task.FromResult<string?>("forbidden");

        if (!Petitions.Accept(session.ObjectId, gm.Name, petitionId, out var entry) || entry == null)
            return Task.FromResult<string?>(null);

        if (_byId.TryGetValue(entry.PetitionerObjectId, out var petitioner) && petitioner.Session != null)
            SelfLine(petitioner.Session, PetitionProtocol.ApplicationAcceptedSm, PetitionProtocol.AcceptedText);

        SelfLine(session, PetitionProtocol.AcceptedSm,
            $"Your petition application has been accepted. Receipt No. is {entry.Id}.");
        return Task.FromResult<string?>(null);
    }

    bool TryPetitionChat(ZoneEntity speaker, int chatType, string text)
    {
        if (!Petitions.IsPlayerInConsultation(speaker.ObjectId))
            return false;
        if (!Petitions.TryActiveFor(speaker.ObjectId, out var petition))
        {
            ZoneLog.Warn(
                $"[petition] missing petition id for chat characterId={speaker.ObjectId} chatType={chatType}");
            return false;
        }

        int sayType = speaker.ObjectId == petition.PetitionerObjectId
            ? ChatProtocol.PetitionPlayer
            : ChatProtocol.PetitionGm;
        petition.Log.Add((sayType, speaker.ObjectId, speaker.Name, text));

        var recipients = new List<int>();
        if (_byId.ContainsKey(petition.PetitionerObjectId))
            recipients.Add(petition.PetitionerObjectId);
        if (petition.ResponderObjectId > 0 && _byId.ContainsKey(petition.ResponderObjectId))
            recipients.Add(petition.ResponderObjectId);

        if (recipients.Count == 0)
            return true;

        SayToGroup(speaker, sayType, text, recipients);
        return true;
    }

    bool AnyOnlineGm()
    {
        foreach (var e in _byId.Values)
        {
            if (e.Kind == ZoneEntityKind.Player && ZoneAdmin.IsGmCharacter(e.Name))
                return true;
        }

        return false;
    }

    void NotifyGmsPetition(string text)
    {
        foreach (var e in _byId.Values)
        {
            if (e.Kind != ZoneEntityKind.Player || e.Session == null)
                continue;
            if (!ZoneAdmin.IsGmCharacter(e.Name))
                continue;
            NetBridge?.FanoutCreatureSay([e.ObjectId], new CreatureSayMsg
            {
                ObjectId = e.ObjectId,
                Name = "Petition System",
                Text = text,
                ChatType = ChatProtocol.Hero
            });
        }
    }
}
