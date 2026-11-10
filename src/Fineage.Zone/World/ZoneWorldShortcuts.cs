using Fineage.Accounts;
using Fineage.Protocol;
using Fineage.Zone.Shortcuts;

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
    internal readonly record struct ShortcutReply(ShortCutMsg? Register, int? DeleteSlot, string? Error);

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
                // Stackables (soulshots etc.): objectId changes when the stack is emptied and restocked.
                // Remap to a live bag row of the same itemId so the hotbar survives restock/relog.
                var remapped = RemapItemShortcut(entity, s);
                if (remapped == null)
                {
                    pruned = true;
                    continue;
                }
                row = remapped;
                pruned = true;
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
        // Persist immediately (Mobius registerShortCut). Log when the store refuses the write —
        // silent false left players with a painted hotbar that vanished on relog.
        var rows = entity.Shortcuts.Values.ToList();
        if (!_persist.Enabled)
            return;
        try
        {
            if (!_persist.ReplaceShortcuts(session.Account, session.ObjectId, rows))
                ZoneLog.Warn(
                    $"shortcut persist refused account={session.Account} char={session.ObjectId} count={rows.Count}");
        }
        catch (Exception ex)
        {
            ZoneLog.Warn($"shortcut persist failed account={session.Account} char={session.ObjectId}: {ex.Message}");
        }
    }

    static ShortCutMsg ToProtocol(StoredShortcut s) =>
        new() { Type = s.Type, Slot = s.WorldSlot, Id = s.Id, Level = s.Level };

    /// <summary>ShortCutInit rows for the live player (world-slot order).</summary>
    internal ShortCutInitMsg BuildShortcutInit(ZoneSession session)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var e) || e.Kind != ZoneEntityKind.Player)
                return new ShortCutInitMsg();
            return new ShortCutInitMsg
            {
                Shortcuts = e.Shortcuts.Values.OrderBy(s => s.WorldSlot).Select(ToProtocol).ToArray()
            };
        }
    }

    /// <summary>Legacy name used by enter / tests; same as <see cref="BuildShortcutInit"/>.</summary>
    internal ShortCutInitMsg NetShortcutInit(ZoneSession session) => BuildShortcutInit(session);

    /// <summary>Mobius RequestShortCutReg. Rejected requests reply with the slot's current server state.</summary>
    internal ShortcutReply RegisterShortcut(ZoneSession session, int type, int worldSlot, int id, int level)
    {
        lock (_gate)
        {
            if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var e) || e.Kind != ZoneEntityKind.Player)
                return new ShortcutReply(null, null, "world");

            if (ShortCutRules.SlotError(worldSlot) is { } slotError)
                return new ShortcutReply(null, null, slotError);

            var row = new StoredShortcut(
                worldSlot % StoredShortcut.SlotsPerPage,
                worldSlot / StoredShortcut.SlotsPerPage,
                type,
                id,
                Math.Max(0, level));
            bool itemOwned = type != StoredShortcut.TypeItem || e.Inventory?.Find(id) != null;
            int knownLevel = 0;
            bool skillKnown = type != StoredShortcut.TypeSkill
                || e.KnownSkills.TryGetValue(id, out knownLevel);
            if (ShortCutRules.RegisterError(row.IsValid, type, itemOwned, skillKnown) is { } error)
                return Current(e, worldSlot, error);

            if (type == StoredShortcut.TypeItem)
            {
                // Persist itemId in Level so LoadShortcuts can rebind after a stack is emptied/restocked.
                int itemId = e.Inventory?.Find(id)?.ItemId ?? 0;
                row = row with { Level = itemId };
            }
            else if (type == StoredShortcut.TypeSkill)
                row = row with { Level = knownLevel };

            e.Shortcuts[worldSlot] = row;
            SaveShortcuts(session, e);
            return new ShortcutReply(ToProtocol(row), null, null);
        }
    }

    /// <summary>Mobius RequestShortCutDel.</summary>
    internal ShortcutReply DeleteShortcut(ZoneSession session, int worldSlot)
    {
        lock (_gate)
        {
            if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var e) || e.Kind != ZoneEntityKind.Player)
                return new ShortcutReply(null, null, "world");
            if (ShortCutRules.SlotError(worldSlot) is { } slotError)
                return new ShortcutReply(null, null, slotError);
            if (e.Shortcuts.Remove(worldSlot))
                SaveShortcuts(session, e);
            return new ShortcutReply(null, worldSlot, null);
        }
    }

    static ShortcutReply Current(ZoneEntity e, int worldSlot, string error) =>
        e.Shortcuts.TryGetValue(worldSlot, out var existing)
            ? new ShortcutReply(ToProtocol(existing), null, error)
            : new ShortcutReply(null, worldSlot, error);

    /// <summary>
    /// When an ITEM shortcut's objectId is gone, rebind via Level (stored itemId) to a live bag stack.
    /// </summary>
    static StoredShortcut? RemapItemShortcut(ZoneEntity entity, StoredShortcut stale)
    {
        if (entity.Inventory == null || stale.Level <= 0)
            return null;
        var stack = entity.Inventory.FindByItemId(stale.Level);
        return stack == null ? null : stale with { Id = stack.ObjectId };
    }
}
