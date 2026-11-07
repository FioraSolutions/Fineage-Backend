using Fineage.Accounts;
using Fineage.Net.Contract;

namespace Fineage.Zone.World;

/// <summary>
/// Mobius ShortCuts (hotbar): restored on enter world and sent as ShortCutInit after ItemList / SkillList;
/// RequestShortCutReg / RequestShortCutDel change one slot and write <c>character_shortcuts</c> right away
/// (Mobius registerShortCut / deleteShortCut persist immediately). Item shortcuts whose object is no longer
/// in the bag are dropped on restore (Mobius ShortCuts.restoreMe); skill shortcuts carry the known level.
/// </summary>
public sealed partial class ZoneWorld
{
    /// <summary>Outcome of a shortcut request: echo one slot as registered, or tell the client the slot is empty.</summary>
    internal readonly record struct ShortcutReply(ShortCutNetMsg? Register, int? DeleteSlot, string? Error);

    void LoadShortcuts(ZoneEntity entity, CharacterRecord? stored)
    {
        entity.Shortcuts.Clear();
        if (stored?.Shortcuts == null)
            return;
        bool pruned = false;
        foreach (var s in stored.Shortcuts)
        {
            if (s == null || !s.IsValid)
            {
                pruned = true;
                continue;
            }

            var row = s;
            if (s.Type == StoredShortcut.TypeItem && entity.Inventory?.Find(s.Id) == null)
            {
                pruned = true;
                continue;
            }

            if (s.Type == StoredShortcut.TypeSkill && entity.KnownSkills.TryGetValue(s.Id, out int lvl) && lvl != s.Level)
            {
                row = s with { Level = lvl };
                pruned = true;
            }

            entity.Shortcuts[row.WorldSlot] = row;
        }

        if (pruned && entity.Session != null)
            SaveShortcuts(entity.Session, entity);
    }

    void SaveShortcuts(ZoneSession session, ZoneEntity entity)
    {
        if (_accounts == null)
            return;
        _accounts.TryReplaceShortcuts(session.Account, session.ObjectId, entity.Shortcuts.Values.ToList());
    }

    static ShortCutNetMsg ToNet(StoredShortcut s) =>
        new() { Type = s.Type, Slot = s.WorldSlot, Id = s.Id, Level = s.Level };

    /// <summary>ShortCutInit rows for the live player (world-slot order).</summary>
    internal ShortCutInitNetMsg NetShortcutInit(ZoneSession session)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var e) || e.Kind != ZoneEntityKind.Player)
                return new ShortCutInitNetMsg();
            return new ShortCutInitNetMsg
            {
                Shortcuts = e.Shortcuts.Values.OrderBy(s => s.WorldSlot).Select(ToNet).ToArray()
            };
        }
    }

    /// <summary>Mobius RequestShortCutReg. Rejected requests reply with the slot's current server state.</summary>
    internal ShortcutReply RegisterShortcut(ZoneSession session, int type, int worldSlot, int id, int level)
    {
        lock (_gate)
        {
            if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var e) || e.Kind != ZoneEntityKind.Player)
                return new ShortcutReply(null, null, "world");

            if (worldSlot < 0 || worldSlot >= StoredShortcut.SlotsPerPage * StoredShortcut.MaxPages)
                return new ShortcutReply(null, null, "shortcut_slot");

            var row = new StoredShortcut(
                worldSlot % StoredShortcut.SlotsPerPage,
                worldSlot / StoredShortcut.SlotsPerPage,
                type,
                id,
                Math.Max(0, level));
            string? error = null;
            if (!row.IsValid)
                error = "shortcut_invalid";
            else if (type == StoredShortcut.TypeItem)
            {
                if (e.Inventory?.Find(id) == null)
                    error = Fineage.Protocol.InventoryProtocol.NotOwned;
                else
                    row = row with { Level = 0 };
            }
            else if (type == StoredShortcut.TypeSkill)
            {
                // Mobius: the bar shows the level the player knows, not what the client claims.
                if (!e.KnownSkills.TryGetValue(id, out int known))
                    error = "skill_unknown";
                else
                    row = row with { Level = known };
            }

            if (error != null)
                return Current(e, worldSlot, error);

            e.Shortcuts[worldSlot] = row;
            SaveShortcuts(session, e);
            return new ShortcutReply(ToNet(row), null, null);
        }
    }

    /// <summary>Mobius RequestShortCutDel.</summary>
    internal ShortcutReply DeleteShortcut(ZoneSession session, int worldSlot)
    {
        lock (_gate)
        {
            if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var e) || e.Kind != ZoneEntityKind.Player)
                return new ShortcutReply(null, null, "world");
            if (worldSlot < 0 || worldSlot >= StoredShortcut.SlotsPerPage * StoredShortcut.MaxPages)
                return new ShortcutReply(null, null, "shortcut_slot");
            if (e.Shortcuts.Remove(worldSlot))
                SaveShortcuts(session, e);
            return new ShortcutReply(null, worldSlot, null);
        }
    }

    static ShortcutReply Current(ZoneEntity e, int worldSlot, string error) =>
        e.Shortcuts.TryGetValue(worldSlot, out var existing)
            ? new ShortcutReply(ToNet(existing), null, error)
            : new ShortcutReply(null, worldSlot, error);
}
