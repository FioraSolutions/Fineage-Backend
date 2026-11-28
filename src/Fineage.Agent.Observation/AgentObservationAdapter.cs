using Fineage.Agent.Contracts;
using Fineage.Agent.Identity;
using Fineage.Zone;
using Fineage.Zone.Observation;
using Fineage.Zone.World;

namespace Fineage.Agent.Observation;

/// <summary>
/// Agent Gate read path: authorize <see cref="AgentScope.WorldRead"/>, capture a knownlist
/// snapshot through the existing Zone request path when needed, then project off the tick.
/// </summary>
public sealed class AgentObservationAdapter
{
    readonly AgentIdentityService _identity;

    public AgentObservationAdapter(AgentIdentityService identity)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    /// <summary>
    /// Observe what the bound character legitimately sees. Capture (if needed) runs on the
    /// sim thread via <see cref="ZoneWorld.PublishObservationAsync"/>; projection, paging,
    /// and JSON stay off the tick. The sim thread never waits for this caller or for I/O.
    /// </summary>
    public async Task<AgentObservationResult> ObserveAsync(
        VerifiedAgentIdentity identity,
        CharacterId characterId,
        ZoneWorld world,
        ZoneSession session,
        AgentObservationQuery? query = null,
        CorrelationId? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(session);

        AgentAuthorization auth = _identity.Authorize(identity, characterId, AgentScope.WorldRead);
        if (!auth.Allowed)
        {
            return AgentObservationResult.Refuse(
                auth.RefusalCode ?? AgentRefusalCodes.ScopeDenied,
                auth.Detail ?? "world.read is required to observe.");
        }

        if (session.ObjectId == 0)
            return AgentObservationResult.Refuse(AgentRefusalCodes.CharacterNotBound, "Session is not bound to a character.");

        if (!world.TryGetPublishedObservation(session.ObjectId, out ZoneObservationSnapshot? snapshot))
        {
            snapshot = await world.PublishObservationAsync(session, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return AgentObservationResult.Refuse(
                    AgentRefusalCodes.CharacterNotBound,
                    "The bound character is not in the world.");
            }
        }

        if (snapshot.ObjectId != session.ObjectId)
        {
            return AgentObservationResult.Refuse(
                AgentRefusalCodes.CharacterNotBound,
                "Published observation does not belong to this session.");
        }

        var cid = correlationId ?? new CorrelationId($"observe:{session.ObjectId}:{snapshot.Tick}");
        return AgentObservationProjector.Project(snapshot, characterId, cid, query);
    }

    /// <summary>
    /// Re-capture on the sim thread, then project off-thread. Use when the caller wants a
    /// fresh knownlist rather than the last published snapshot.
    /// </summary>
    public async Task<AgentObservationResult> ObserveFreshAsync(
        VerifiedAgentIdentity identity,
        CharacterId characterId,
        ZoneWorld world,
        ZoneSession session,
        AgentObservationQuery? query = null,
        CorrelationId? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(session);

        AgentAuthorization auth = _identity.Authorize(identity, characterId, AgentScope.WorldRead);
        if (!auth.Allowed)
        {
            return AgentObservationResult.Refuse(
                auth.RefusalCode ?? AgentRefusalCodes.ScopeDenied,
                auth.Detail ?? "world.read is required to observe.");
        }

        ZoneObservationSnapshot? snapshot = await world.PublishObservationAsync(session, cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
        {
            return AgentObservationResult.Refuse(
                AgentRefusalCodes.CharacterNotBound,
                "The bound character is not in the world.");
        }

        var cid = correlationId ?? new CorrelationId($"observe:{session.ObjectId}:{snapshot.Tick}");
        return AgentObservationProjector.Project(snapshot, characterId, cid, query);
    }
}
