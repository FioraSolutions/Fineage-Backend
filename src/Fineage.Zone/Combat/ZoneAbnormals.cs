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
        long nowUnixMs,
        int skillLevel = 0,
        string slot = "")
    {
        if (effectId <= 0 || durationSeconds <= 0)
            return;

        long expires = nowUnixMs + durationSeconds * 1000L;
        int index = FindIndex(list, effectId);
        if (index >= 0)
        {
            list[index].Value = value;
            list[index].SkillLevel = skillLevel;
            list[index].ExpiresAtUnixMs = expires;
            list[index].NextDotAtUnixMs = 0;
            if (slot.Length > 0)
                list[index].Slot = slot;
        }
        else
        {
            list.Add(new ZoneAbnormalEntry
            {
                Id = effectId,
                Value = value,
                SkillLevel = skillLevel,
                Slot = slot,
                ExpiresAtUnixMs = expires,
                NextDotAtUnixMs = 0
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

    /// <summary>
    /// Mobius <c>DispelByCategory</c> / <c>Formulas.calcCancelEffects</c> for slot=buff|debuff.
    /// Walks abnormals newest-first; <paramref name="isCategory"/> filters; <paramref name="roll"/> is
    /// calcCancelSuccess. Removes up to <paramref name="max"/>.
    /// </summary>
    public static int DispelByCategory(
        IList<ZoneAbnormalEntry> list,
        Func<ZoneAbnormalEntry, bool> isCategory,
        Func<ZoneAbnormalEntry, bool> roll,
        int max)
    {
        if (max <= 0 || list.Count == 0)
            return 0;
        int removed = 0;
        for (int i = list.Count - 1; i >= 0 && removed < max; i--)
        {
            var entry = list[i];
            if (!isCategory(entry) || !roll(entry))
                continue;
            list.RemoveAt(i);
            removed++;
        }
        return removed;
    }

    /// <summary>
    /// Mobius <c>EffectDispelBySlot</c>: removes up to <paramref name="count"/> abnormals of
    /// <paramref name="slot"/> (POISON, BLEEDING, STUN, …). Slot comes from the skill's Mobius
    /// <c>abnormalType</c> (see <c>skills.json</c>).
    /// </summary>
    public static int DispelBySlot(IList<ZoneAbnormalEntry> list, string slot, int count)
    {
        if (string.IsNullOrWhiteSpace(slot) || count <= 0)
            return 0;
        int removed = 0;
        for (int i = list.Count - 1; i >= 0 && removed < count; i--)
        {
            if (list[i].Slot is { Length: > 0 } entrySlot &&
                string.Equals(entrySlot, slot, StringComparison.OrdinalIgnoreCase))
            {
                list.RemoveAt(i);
                removed++;
            }
        }

        return removed;
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
