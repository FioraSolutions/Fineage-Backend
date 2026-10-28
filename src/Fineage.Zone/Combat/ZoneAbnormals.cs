using Fineage.Protocol;

namespace Fineage.Zone.Combat;

public static class ZoneAbnormals
{
    public static int RemainingSeconds(long nowUnixMs, ZoneAbnormalEntry entry)
    {
        if (entry.ExpiresAtUnixMs <= nowUnixMs)
            return 0;
        long remainingMs = entry.ExpiresAtUnixMs - nowUnixMs;
        return (int)Math.Max(1, (remainingMs + 999) / 1000);
    }

    public static AbnormalEffectMsg ToWire(long nowUnixMs, ZoneAbnormalEntry entry) => new()
    {
        Id = entry.Id,
        Value = entry.Value,
        DurationSeconds = RemainingSeconds(nowUnixMs, entry)
    };

    public static void Apply(
        IList<ZoneAbnormalEntry> list,
        int effectId,
        int value,
        int durationSeconds,
        long nowUnixMs)
    {
        if (effectId <= 0 || durationSeconds <= 0)
            return;

        long expires = nowUnixMs + durationSeconds * 1000L;
        int index = FindIndex(list, effectId);
        if (index >= 0)
        {
            list[index].Value = value;
            list[index].ExpiresAtUnixMs = expires;
        }
        else
        {
            list.Add(new ZoneAbnormalEntry
            {
                Id = effectId,
                Value = value,
                ExpiresAtUnixMs = expires
            });
        }
    }

    public static bool TickExpired(IList<ZoneAbnormalEntry> list, long nowUnixMs)
    {
        bool changed = false;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].ExpiresAtUnixMs <= nowUnixMs)
            {
                list.RemoveAt(i);
                changed = true;
            }
        }

        return changed;
    }

    static int FindIndex(IList<ZoneAbnormalEntry> list, int effectId)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Id == effectId)
                return i;
        }

        return -1;
    }
}
