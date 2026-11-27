using Fineage.Protocol;
using Fineage.Zone.CommunityBoard;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    /// <summary>Mobius <c>CommunityBoardHandler</c> — open / parse / write for #929.</summary>
    internal CommunityBoardService CommunityBoard { get; private set; } = null!;

    int _boardTargetObjectId;

    void InitCommunityBoard(CommunityBoardCatalog? catalog)
    {
        var pages = catalog ?? CommunityBoardCatalog.LoadDefault();
        CommunityBoard = new CommunityBoardService(pages, SendBoard, msg => ZoneLog.Warn(msg));
    }

    void SendBoard(ShowBoardMsg msg)
    {
        int target = _boardTargetObjectId;
        if (target <= 0)
            return;
        NetBridge?.FanoutShowBoard([target], msg);
    }

    /// <summary>Mobius <c>RequestShowBoard</c> → <c>handleParseCommand(BBS_DEFAULT)</c>.</summary>
    public Task<string?> TryShowBoardAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return Task.FromResult<string?>("world");

        _boardTargetObjectId = session.ObjectId;
        try
        {
            CommunityBoard.ShowDefault(session.ObjectId, player.Name, _clan.Count);
        }
        finally
        {
            _boardTargetObjectId = 0;
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>Mobius <c>RequestBBSwrite</c> → <c>handleWriteCommand</c>.</summary>
    public Task<string?> TryBBSwriteAsync(ZoneSession session, BBSwriteMsg request, CancellationToken ct)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return Task.FromResult<string?>("world");

        _boardTargetObjectId = session.ObjectId;
        try
        {
            CommunityBoard.Write(
                session.ObjectId,
                player.Name,
                request.Url ?? "",
                request.Arg1 ?? "",
                request.Arg2 ?? "",
                request.Arg3 ?? "",
                request.Arg4 ?? "",
                request.Arg5 ?? "");
        }
        finally
        {
            _boardTargetObjectId = 0;
        }

        return Task.FromResult<string?>(null);
    }

    /// <summary>Mobius <c>RequestBypassToServer</c> community-board branch (no NPC).</summary>
    internal string? TryCommunityBoardBypass(ZoneSession session, ZoneEntity player, string command)
    {
        _boardTargetObjectId = session.ObjectId;
        try
        {
            CommunityBoard.Parse(session.ObjectId, player.Name, command, _clan.Count);
        }
        finally
        {
            _boardTargetObjectId = 0;
        }

        return null;
    }
}
