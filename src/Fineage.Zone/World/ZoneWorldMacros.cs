using System.Text;
using Fineage.Accounts;
using Fineage.Protocol;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    void LoadMacros(ZoneEntity entity, CharacterRecord? stored)
    {
        entity.Macros.Clear();
        entity.MacroRevision = 1;
        entity.MacroNextId = StoredMacro.FirstId;
        if (stored?.Macros == null)
            return;

        foreach (var m in stored.Macros)
        {
            if (m.Id <= 0 || string.IsNullOrEmpty(m.Name))
                continue;
            entity.Macros[m.Id] = CloneMacro(m);
            if (m.Id >= entity.MacroNextId)
                entity.MacroNextId = m.Id + 1;
        }
    }

    void SaveMacros(ZoneSession session, ZoneEntity entity)
    {
        if (!_persist.Enabled)
            return;
        var rows = entity.Macros.Values.OrderBy(m => m.Id).Select(CloneMacro).ToList();
        try
        {
            if (!_persist.ReplaceMacros(session.Account, session.ObjectId, rows))
                ZoneLog.Warn(
                    $"[macros] persist refused account={session.Account} char={session.ObjectId} count={rows.Count}");
        }
        catch (Exception ex)
        {
            ZoneLog.Warn($"[macros] persist failed account={session.Account} char={session.ObjectId}: {ex.Message}");
        }
    }

    /// <summary>Mobius EnterWorld: <c>player.getMacros().sendUpdate()</c>.</summary>
    internal void SendMacroList(ZoneSession session, ZoneEntity player)
    {
        var all = player.Macros.Values.OrderBy(m => m.Id).ToList();
        if (all.Count == 0)
        {
            NetBridge?.FanoutMacroList([session.ObjectId], new MacroListMsg
            {
                Revision = player.MacroRevision,
                Count = 0,
                Macro = null
            });
            return;
        }

        foreach (var m in all)
        {
            NetBridge?.FanoutMacroList([session.ObjectId], new MacroListMsg
            {
                Revision = player.MacroRevision,
                Count = all.Count,
                Macro = ToProtocol(m)
            });
        }
    }

    string? TryMakeMacro(ZoneSession session, MakeMacroMsg request)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        var cmds = request.Commands ?? [];
        if (cmds.Length > MacroProtocol.MaxCommands)
            cmds = cmds.Take(MacroProtocol.MaxCommands).ToArray();

        int commandsLength = 0;
        foreach (var c in cmds)
            commandsLength += (c.Cmd ?? "").Length;

        if (commandsLength > MacroProtocol.MaxCommandsLength)
        {
            SelfLine(session, MacroProtocol.InvalidMacro, MacroProtocol.InvalidMacroText);
            return null;
        }

        // Mobius: create only when id==0 and size > MaxMacros (strict greater-than).
        if (request.Id == 0 && player.Macros.Count > MacroProtocol.MaxMacros)
        {
            ZoneLog.Warn($"[macros] characterId={session.ObjectId} rejected make: over limit ({player.Macros.Count})");
            SelfLine(session, MacroProtocol.LimitReached, MacroProtocol.LimitReachedText);
            return null;
        }

        string name = (request.Name ?? "").Trim();
        if (name.Length == 0)
        {
            SelfLine(session, MacroProtocol.EmptyName, MacroProtocol.EmptyNameText);
            return null;
        }

        string descr = request.Descr ?? "";
        if (descr.Length > MacroProtocol.MaxDescrLength)
        {
            SelfLine(session, MacroProtocol.DescrTooLong, MacroProtocol.DescrTooLongText);
            return null;
        }

        int id = request.Id;
        if (id == 0)
        {
            id = player.MacroNextId++;
            while (player.Macros.ContainsKey(id))
                id = player.MacroNextId++;
        }

        var storedCmds = new List<StoredMacroCmd>(cmds.Length);
        for (int i = 0; i < cmds.Length; i++)
        {
            var c = cmds[i];
            int type = c.Type is < 1 or > 6 ? 0 : c.Type;
            storedCmds.Add(new StoredMacroCmd(c.Entry, type, c.D1, c.D2, c.Cmd ?? ""));
        }

        player.Macros[id] = new StoredMacro(
            id, request.Icon, name, descr, request.Acronym ?? "", storedCmds);
        player.MacroRevision++;
        SaveMacros(session, player);
        SendMacroList(session, player);
        return null;
    }

    string? TryDeleteMacro(ZoneSession session, int macroId)
    {
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player)
            || player.Kind != ZoneEntityKind.Player)
            return "world";

        if (!player.Macros.Remove(macroId))
        {
            // Mobius still sendUpdates even when missing; keep list in sync.
            player.MacroRevision++;
            SendMacroList(session, player);
            return null;
        }

        // Mobius: drop hotbar shortcuts pointing at this macro.
        var remove = player.Shortcuts
            .Where(kv => kv.Value.Type == StoredShortcut.TypeMacro && kv.Value.Id == macroId)
            .Select(kv => kv.Key)
            .ToArray();
        foreach (int slot in remove)
            player.Shortcuts.Remove(slot);
        if (remove.Length > 0)
            SaveShortcuts(session, player);

        player.MacroRevision++;
        SaveMacros(session, player);
        SendMacroList(session, player);
        return null;
    }

    public Task<string?> TryMakeMacroAsync(ZoneSession session, MakeMacroMsg request, CancellationToken ct) =>
        OnSimulationAsync(session, "make-macro", () => TryMakeMacro(session, request), ct);

    public Task<string?> TryDeleteMacroAsync(ZoneSession session, int macroId, CancellationToken ct) =>
        OnSimulationAsync(session, "delete-macro", () => TryDeleteMacro(session, macroId), ct);

    static MacroEntryMsg ToProtocol(StoredMacro m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        Descr = m.Descr,
        Acronym = m.Acronym,
        Icon = m.Icon,
        Commands = m.Commands.Select(c => new MacroCmdMsg
        {
            Entry = c.Entry,
            Type = c.Type,
            D1 = c.D1,
            D2 = c.D2,
            Cmd = c.Cmd
        }).ToArray()
    };

    static StoredMacro CloneMacro(StoredMacro m) =>
        new(m.Id, m.Icon, m.Name ?? "", m.Descr ?? "", m.Acronym ?? "",
            (m.Commands ?? []).Select(c => new StoredMacroCmd(c.Entry, c.Type, c.D1, c.D2, c.Cmd ?? "")).ToArray());

    /// <summary>Mobius DB commands column: <c>type,d1,d2[,cmd];...</c> capped at 255.</summary>
    internal static string EncodeMacroCommands(IReadOnlyList<StoredMacroCmd> commands)
    {
        var sb = new StringBuilder(300);
        foreach (var cmd in commands)
        {
            sb.Append(cmd.Type).Append(',').Append(cmd.D1).Append(',').Append(cmd.D2);
            if (!string.IsNullOrEmpty(cmd.Cmd))
                sb.Append(',').Append(cmd.Cmd);
            sb.Append(';');
        }

        if (sb.Length > StoredMacro.MaxCommandsLength)
            sb.Length = StoredMacro.MaxCommandsLength;
        return sb.ToString();
    }

    internal static List<StoredMacroCmd> DecodeMacroCommands(string? raw)
    {
        var list = new List<StoredMacroCmd>();
        if (string.IsNullOrEmpty(raw))
            return list;
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(',');
            if (bits.Length < 3)
                continue;
            if (!int.TryParse(bits[0], out int type)
                || !int.TryParse(bits[1], out int d1)
                || !int.TryParse(bits[2], out int d2))
                continue;
            string cmd = bits.Length > 3 ? bits[3] : "";
            list.Add(new StoredMacroCmd(list.Count, type, d1, d2, cmd));
        }

        return list;
    }
}
