using Fineage.Net.Contract;
using Fineage.Zone.Combat;

namespace Fineage.Zone.World;

/// <summary>Mobius <c>ClientSetTime</c> fan-out through the shared <see cref="Combat.ZoneGameTime"/> clock.</summary>
public sealed partial class ZoneWorld
{
    int _lastClientSetTimeFanoutMinutes = int.MinValue;

    internal ClientSetTimeNetMsg BuildClientSetTimeNet(long unixMsNow) => new()
    {
        GameMinutes = _gameTime.ClientGameMinutes(unixMsNow),
        ClockSpeed = ZoneGameTime.ClientClockSpeed
    };

    /// <summary>
    /// Mobius <c>GameTimeController</c> advances every 100 ms; <c>ClientSetTime</c> resyncs when the client-minute
    /// counter moves (EnterWorld + ongoing sync for online players).
    /// </summary>
    void TickClientGameTime(long wallUnixMs)
    {
        int minutes = _gameTime.ClientGameMinutes(wallUnixMs);
        if (minutes == _lastClientSetTimeFanoutMinutes)
            return;
        _lastClientSetTimeFanoutMinutes = minutes;

        List<int> online = [];
        foreach (var player in Players())
        {
            if (player.Session is { InWorld: true })
                online.Add(player.ObjectId);
        }

        if (online.Count == 0 || NetBridge == null)
            return;

        NetBridge.FanoutClientSetTime(online, BuildClientSetTimeNet(wallUnixMs));
    }
}