using Fineage.Protocol;
using Fineage.Zone.Chat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TrySayAsync(
        ZoneSession session, string? text, int chatType, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return Task.FromResult<string?>("world");
        if (!SayRules.TryNormalize(text, out string normalized))
            return Task.FromResult<string?>("bad_request");

        SystemMessageMsg? notice = null;
        lock (_gate)
        {
            if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var speaker) || speaker.Session == null)
                return Task.FromResult<string?>("world");

            long nowMs = now.ToUnixTimeMilliseconds();
            if (SayRules.IsFlood(session.LastChatUnixMs, nowMs))
            {
                notice = SayRules.Flood();
            }
            else if (!SayRules.IsDelivered(chatType))
            {
                session.LastChatUnixMs = nowMs;
                notice = SayRules.NotSupported(chatType);
            }
            else
            {
                session.LastChatUnixMs = nowMs;
                BuildSay(speaker, chatType, normalized);
            }
        }

        if (notice != null)
        {
            NetBridge?.FanoutSystemMessage(
                [session.ObjectId],
                new SystemMessageMsg
                {
                    Id = notice.Id,
                    Text = notice.Text,
                    Params = notice.Params ?? []
                });

            return Task.FromResult<string?>(null);
        }

        ZoneLog.Info($"say object={session.ObjectId} name={session.Name} type={chatType} text={normalized}");
        return Task.FromResult<string?>(null);
    }

    void BuildSay(ZoneEntity speaker, int chatType, string text)
    {
        var said = new CreatureSayMsg
        {
            ObjectId = speaker.ObjectId,
            Name = speaker.Name,
            Text = text,
            ChatType = chatType
        };

        List<int>? netWatchers = null;
        if (speaker.Session != null)
        {
            netWatchers = [speaker.ObjectId];
        }

        bool shout = chatType == ChatProtocol.Shout;
        foreach (var entity in Players())
        {
            if (entity.Session == null || ReferenceEquals(entity.Session, speaker.Session))
                continue;
            if (shout || HearsLocal(speaker, entity))
            {
                netWatchers ??= [];
                netWatchers.Add(entity.ObjectId);
            }
        }

        if (netWatchers is { Count: > 0 } && NetBridge != null)
            NetBridge.FanoutCreatureSay(netWatchers, said);
    }

    static bool HearsLocal(ZoneEntity speaker, ZoneEntity other) =>
        speaker.Knownlist.Contains(other.ObjectId) &&
        SayRules.InLocalRange(speaker.X, speaker.Y, other.X, other.Y);
}
