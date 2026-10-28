using Fineage.Protocol;
using Fineage.Zone.Chat;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TrySayAsync(ZoneSession session, SayMsg say, DateTimeOffset now, CancellationToken ct)
    {
        if (!session.InWorld)
            return "world";
        if (!SayRules.TryNormalize(say.Text, out string text))
            return "bad_request";

        SystemMessageMsg? notice = null;
        var outbound = new List<Pending>();
        lock (_gate)
        {
            if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var speaker) || speaker.Session == null)
                return "world";

            long nowMs = now.ToUnixTimeMilliseconds();
            if (SayRules.IsFlood(session.LastChatUnixMs, nowMs))
            {
                notice = SayRules.Flood();
            }
            else if (!SayRules.IsDelivered(say.ChatType))
            {
                session.LastChatUnixMs = nowMs;
                notice = SayRules.NotSupported(say.ChatType);
            }
            else
            {
                session.LastChatUnixMs = nowMs;
                outbound = BuildSay(speaker, say.ChatType, text);
            }
        }

        if (notice != null)
        {
            await session.SendAsync(MsgType.SystemMessage, notice, ct).ConfigureAwait(false);
            return null;
        }

        ZoneLog.Info($"say object={session.ObjectId} name={session.Name} type={say.ChatType} text={text}");
        await FlushSelfAsync(session, outbound, ct).ConfigureAwait(false);
        return null;
    }

    List<Pending> BuildSay(ZoneEntity speaker, int chatType, string text)
    {
        var said = new CreatureSayMsg
        {
            ObjectId = speaker.ObjectId,
            Name = speaker.Name,
            Text = text,
            ChatType = chatType
        };

        var outbound = new List<Pending>();
        if (speaker.Session != null)
            outbound.Add(Pack(speaker.Session, MsgType.CreatureSay, said));

        bool shout = chatType == ChatProtocol.Shout;
        foreach (var entity in Players())
        {
            if (entity.Session == null || ReferenceEquals(entity.Session, speaker.Session))
                continue;
            if (shout || HearsLocal(speaker, entity))
                outbound.Add(Pack(entity.Session, MsgType.CreatureSay, said));
        }

        return outbound;
    }

    static bool HearsLocal(ZoneEntity speaker, ZoneEntity other) =>
        speaker.Knownlist.Contains(other.ObjectId) &&
        SayRules.InLocalRange(speaker.X, speaker.Y, other.X, other.Y);
}
