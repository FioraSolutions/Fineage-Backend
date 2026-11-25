using Fineage.Game;

namespace Fineage.Accounts;

/// <summary>Vitals and paperdoll shown on CharSelect. Falls back to class level-1 maxima when nothing is saved.</summary>
public readonly record struct CharSelectStats(
    int CurHp,
    int MaxHp,
    int CurMp,
    int MaxMp,
    int CurCp,
    int MaxCp,
    int WeaponItemId,
    int[] Paperdoll)
{
    public const int PaperdollLength = 17;

    public static CharSelectStats For(CharacterRecord c)
    {
        ClassTemplates.TryGet(c.ClassId, out var t);
        int level = Math.Max(1, c.Level);
        int maxHp = c.MaxHp > 0 ? c.MaxHp : t?.MaxHp ?? 0;
        int maxMp = c.MaxMp > 0 ? c.MaxMp : t?.MaxMp ?? 0;
        int maxCp = 0;
        if (t != null)
        {
            try { maxCp = t.ModifiedCp(level); } catch (ArgumentOutOfRangeException) { maxCp = t.MaxCp; }
        }
        if (c.MaxCp > 0)
            maxCp = c.MaxCp;
        int hp = c.MaxHp > 0 ? Math.Clamp(c.Hp, 0, Math.Max(1, maxHp)) : (c.Hp > 0 ? Math.Min(c.Hp, maxHp) : maxHp);
        int mp = c.MaxMp > 0 ? Math.Clamp(c.Mp, 0, Math.Max(1, maxMp)) : (c.Mp > 0 ? Math.Min(c.Mp, maxMp) : maxMp);
        int cp = c.MaxCp > 0 ? Math.Clamp(c.Cp, 0, Math.Max(0, maxCp)) : (c.Cp > 0 ? Math.Min(c.Cp, maxCp) : maxCp);
        var paperdoll = PaperdollFromItems(c.Items);
        return new CharSelectStats(hp, maxHp, mp, maxMp, cp, maxCp, paperdoll[(int)PaperdollSlot.RHand], paperdoll);
    }

    /// <summary>Builds a length-17 paperdoll (item ids by slot). Later equip wins on the same slot.</summary>
    public static int[] PaperdollFromItems(IReadOnlyList<StoredItem>? items)
    {
        var doll = new int[PaperdollLength];
        if (items == null)
            return doll;
        foreach (var item in items)
        {
            if (item.EquippedSlot >= 0 && item.EquippedSlot < PaperdollLength)
                doll[item.EquippedSlot] = item.ItemId;
        }
        return doll;
    }
}
