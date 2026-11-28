using Fineage.Agent.Contracts;
using Fineage.Agent.Identity;
using Fineage.Zone;
using Fineage.Zone.World;

namespace Fineage.Agent.Gate;

/// <summary>
/// Zone-side Agent Gate write path: authorize the tool scope, resolve the live
/// <see cref="ZoneSession"/> by character ObjectId, and submit through
/// <see cref="ZoneWorld.ExecuteAsync"/> with <see cref="CommandSource.Ai"/>.
/// </summary>
public sealed class AgentCommandAdapter
{
    readonly AgentIdentityService _identity;

    public AgentCommandAdapter(AgentIdentityService identity)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    public async Task<AgentCommandResult> ExecuteAsync(
        VerifiedAgentIdentity identity,
        CharacterId characterId,
        AgentActionRequest request,
        ZoneWorld world,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(world);

        var operationId = new OperationId(request.CommandId.Value);
        if (request.Deadline < DateTimeOffset.UtcNow)
        {
            return AgentCommandResult.Denied(operationId, AgentRefusalCodes.Expired, "The action deadline has passed.");
        }

        if (request.Principal.AgentId != identity.AgentId || request.Principal.TenantId != identity.TenantId)
        {
            return AgentCommandResult.Denied(
                operationId,
                AgentRefusalCodes.AgentMismatch,
                "The request principal does not match the verified identity.");
        }

        if (request.Principal.CharacterId is { } claimed && claimed != characterId)
        {
            return AgentCommandResult.Denied(
                operationId,
                AgentRefusalCodes.CharacterNotBound,
                "The request character does not match the bound character.");
        }

        if (!AgentToolScopes.TryGetRequiredScope(request.ToolName, out AgentScope scope))
        {
            return AgentCommandResult.Denied(
                operationId,
                AgentRefusalCodes.UnknownTool,
                $"Tool '{request.ToolName}' is not published.");
        }

        if (!AgentCommandMapper.IsWriteTool(request.ToolName))
        {
            return AgentCommandResult.Denied(
                operationId,
                AgentRefusalCodes.InvalidContract,
                $"Tool '{request.ToolName}' is not a Zone write command.");
        }

        AgentAuthorization auth = _identity.Authorize(identity, characterId, scope);
        if (!auth.Allowed)
        {
            return AgentCommandResult.Denied(
                operationId,
                auth.RefusalCode ?? AgentRefusalCodes.ScopeDenied,
                auth.Detail ?? $"{scope} is required.");
        }

        if (!TryResolveLiveSession(world, characterId, out ZoneSession? session))
        {
            return AgentCommandResult.Denied(
                operationId,
                AgentRefusalCodes.CharacterNotBound,
                "No live ZoneSession is registered for the bound character.");
        }

        if (!AgentCommandMapper.TryMap(request.ToolName, request.Arguments, out ZoneCommand? command, out string? mapError) ||
            command is null)
        {
            return AgentCommandResult.Denied(
                operationId,
                AgentRefusalCodes.InvalidContract,
                mapError ?? "The tool arguments are invalid.");
        }

        try
        {
            string? outcome = await world.ExecuteAsync(session!, command, CommandSource.Ai, cancellationToken)
                .ConfigureAwait(false);
            return outcome is null
                ? AgentCommandResult.Succeeded(operationId)
                : AgentCommandResult.IngressRefused(operationId, outcome);
        }
        catch (ZoneIngressRefusal ex)
        {
            return AgentCommandResult.IngressRefused(operationId, ex.Message);
        }
    }

    /// <summary>
    /// Resolves the directory instance only. A throwaway <see cref="ZoneSession"/> with a forged ObjectId is not used.
    /// </summary>
    public static bool TryResolveLiveSession(ZoneWorld world, CharacterId characterId, out ZoneSession? session)
    {
        session = null;
        if (!int.TryParse(characterId.Value, out int objectId) || objectId == 0)
            return false;
        return world.TryGetLiveSession(objectId, out session);
    }
}
