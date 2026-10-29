using Fineage.Game;

namespace Fineage.Login;

/// <summary>Vitals and weapon shown on CharSelect, like Mobius CharSelectionInfo. Falls back to class level-1 maxima when nothing is saved.</summary>
readonly record struct CharSelectStats(int CurHp, int MaxHp, int CurMp, int MaxMp, int CurCp, int MaxCp, int WeaponItemId)
{
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
        int hp = c.Hp > 0 ? Math.Min(c.Hp, maxHp) : maxHp;
        int mp = c.Mp > 0 ? Math.Min(c.Mp, maxMp) : maxMp;
        int weapon = 0;
        if (c.Items != null)
            foreach (var item in c.Items)
                if (item.EquippedSlot == (int)PaperdollSlot.RHand) { weapon = item.ItemId; break; }
        return new CharSelectStats(hp, maxHp, mp, maxMp, maxCp, maxCp, weapon);
    }
}
