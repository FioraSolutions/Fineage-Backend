using System.Text.Json;

namespace Fineage.Game.Quests;

public sealed partial class QuestCatalog
{
    static QuestOp ReadOp(OpDto op, ItemCatalog items, string where, int questId)
    {
        string name = (op.Op ?? "").Trim().ToLowerInvariant();
        return name switch
        {
            "create" => new CreateOp(),
            "start" => new StartOp(),
            "set_cond" => ReadCond(op, where, questId),
            "set" => ReadSet(op, where, questId),
            "unset" => ReadUnset(op, where, questId),
            "add" => ReadAdd(op, where, questId),
            "take_all" => op.Items is { Count: > 0 } ids && ids.All(id => items.TryGet(id, out _))
                ? new TakeAllOp(ids.Distinct().ToArray())
                : throw new InvalidDataException($"{where} quest {questId} take_all needs known item ids."),
            "give_item" => ReadGive(op, items, where, questId),
            "give_adena" => ReadAdena(op, where, questId),
            "give_exp_sp" => ReadExp(op, where, questId),
            "take_item" => ReadTake(op, items, where, questId),
            "complete" => new CompleteOp(),
            "drop_item" => ReadDrop(op, items, where, questId),
            "exchange_all" => ReadExchange(op, items, where, questId),
            "sell_items" => ReadSell(op, items, where, questId),
            "sound" => ReadSound(op, where, questId),
            "spawn_npc" => ReadSpawn(op, where, questId),
            "set_noble" => new SetNobleOp(),
            "summon" => ReadSummon(op, where, questId),
            "say" => !string.IsNullOrWhiteSpace(op.Text) && op.Text.Length <= 500
                ? new SayOp(op.Text)
                : throw new InvalidDataException($"{where} quest {questId} say needs text of 1..500 characters."),
            "change_class" => op.ClassId is int cls and >= 0 and <= 136
                ? new ChangeClassOp(cls)
                : throw new InvalidDataException($"{where} quest {questId} change_class needs classId 0..136."),
            "despawn" => new DespawnOp(),
            "pick_random" => ReadVarKey(op.Key, where, questId, "pick_random key") is { } pool &&
                             ReadVarKey(op.Name, where, questId, "pick_random name") is { } target
                ? new PickRandomOp(pool, target)
                : throw new InvalidDataException($"{where} quest {questId} pick_random needs key (pool) and name (target)."),
            "remember_item" => ReadVarKey(op.Key, where, questId, "remember_item key") is { } rememberKey &&
                               op.Items is { Count: > 0 } rememberItems && rememberItems.All(id => items.TryGet(id, out _))
                ? new RememberItemOp(rememberKey, rememberItems.Distinct().ToArray(), Math.Max(0, op.MinEnchant ?? 0))
                : throw new InvalidDataException($"{where} quest {questId} remember_item needs key and known items."),
            "exchange_var_item" => ReadVarKey(op.Key, where, questId, "exchange_var_item key") is { } swapKey &&
                                   op.Items is { Count: > 0 } swapItems && op.Offset is int offset &&
                                   swapItems.All(id => items.TryGet(id, out _) && items.TryGet(id + offset, out _))
                ? new ExchangeVarItemOp(swapKey, swapItems.Distinct().ToArray(), offset)
                : throw new InvalidDataException($"{where} quest {questId} exchange_var_item needs key, items and an offset to known items."),
            "teleport" => op is { X: int x, Y: int y, Z: int z }
                ? new TeleportOp(x, y, z)
                : throw new InvalidDataException($"{where} quest {questId} teleport needs x, y and z."),
            "social" => op.Action is int action and > 0
                ? new SocialOp(action)
                : throw new InvalidDataException($"{where} quest {questId} social needs a positive action."),
            _ => throw new InvalidDataException($"{where} quest {questId} has unknown op '{op.Op}'.")
        };
    }

    static AddVarOp ReadAdd(OpDto op, string where, int questId)
    {
        string key = (op.Key ?? "").Trim();
        if (!QuestJournal.IsStoredVar(key, "0") || key == QuestProgress.CondVar || !int.TryParse(op.Value ?? "1", out int delta))
            throw new InvalidDataException($"{where} quest {questId} add needs a counter key (not cond) and an integer value.");
        return new AddVarOp(key, delta);
    }

    static SpawnNpcOp ReadSpawn(OpDto op, string where, int questId)
    {
        if (op.NpcId <= 0)
            throw new InvalidDataException($"{where} quest {questId} spawn_npc needs a positive npcId.");
        if (op is { X: int x, Y: int y, Z: int z })
            return new SpawnNpcOp(op.NpcId, x, y, z);
        if (op.X != null || op.Y != null || op.Z != null)
            throw new InvalidDataException($"{where} quest {questId} spawn_npc needs all of x, y, z or none.");
        return new SpawnNpcOp(op.NpcId);
    }

    static SummonOp ReadSummon(OpDto op, string where, int questId)
    {
        if (op.NpcId <= 0)
            throw new InvalidDataException($"{where} quest {questId} summon needs a positive npcId.");
        bool fixedPoint = op is { X: not null, Y: not null, Z: not null };
        if (fixedPoint == op.AtPlayer && (fixedPoint || op.X != null || op.Y != null || op.Z != null))
            throw new InvalidDataException($"{where} quest {questId} summon needs either x/y/z or atPlayer, not both.");
        if (op.DespawnSec is < 0 or > 86_400)
            throw new InvalidDataException($"{where} quest {questId} summon despawnSec must be 0..86400.");
        return new SummonOp(new QuestSummon(op.NpcId, op.X, op.Y, op.Z, op.AtPlayer, op.DespawnSec ?? 0, op.Owned,
            op.SoloGuard, op.Say, op.GoneSay, op.GuardSay,
            ReadVarMap(op.GoneVars, where, questId), ReadVarMap(op.DeadVars, where, questId)));
    }

    static IReadOnlyDictionary<string, string>? ReadVarMap(Dictionary<string, string>? rows, string where, int questId)
    {
        if (rows is not { Count: > 0 })
            return null;
        foreach (var pair in rows)
        {
            if (!QuestJournal.IsStoredVar(pair.Key, pair.Value) || pair.Key == QuestProgress.CondVar)
                throw new InvalidDataException($"{where} quest {questId} summon var '{pair.Key}' is not a stored variable.");
        }

        return new Dictionary<string, string>(rows, StringComparer.Ordinal);
    }

    static DropItemOp ReadDrop(OpDto op, ItemCatalog items, string where, int questId)
    {
        int count = op.Count ?? 1;
        double chance = op.Chance ?? 1.0;
        int limit = op.Limit ?? 0;
        if (count < 1 || !items.TryGet(op.ItemId, out _) || op.ItemId == AdenaItemId)
            throw new InvalidDataException($"{where} quest {questId} drop_item {op.ItemId} is unknown or has a bad count.");
        if (chance is <= 0 or > 1 || limit < 0 || op.CondAtLimit is < 0 or > 255)
            throw new InvalidDataException($"{where} quest {questId} drop_item needs chance 0..1, limit >= 0, condAtLimit 0..255.");
        return new DropItemOp(op.ItemId, count, chance, limit, op.CondAtLimit);
    }

    static SellItemsOp ReadSell(OpDto op, ItemCatalog items, string where, int questId)
    {
        var rows = new List<(int, int)>();
        foreach (var row in op.Prices ?? [])
        {
            if (!items.TryGet(row.ItemId, out _) || row.Adena < 0)
                throw new InvalidDataException($"{where} quest {questId} sell_items has a bad price row.");
            rows.Add((row.ItemId, row.Adena));
        }

        if (rows.Count == 0)
            throw new InvalidDataException($"{where} quest {questId} sell_items needs prices.");
        return new SellItemsOp(rows, op.BonusMin ?? 0, op.BonusAdena ?? 0);
    }

    static ExchangeAllOp ReadExchange(OpDto op, ItemCatalog items, string where, int questId)
    {
        int reward = op.RewardItemId ?? 0;
        if (!items.TryGet(op.ItemId, out _) || !items.TryGet(reward, out _) || (op.Per ?? 1) < 1 || (op.BonusAdena ?? 0) < 0)
            throw new InvalidDataException($"{where} quest {questId} exchange_all needs known itemId/rewardItemId and per >= 1.");
        return new ExchangeAllOp(op.ItemId, reward, op.Per ?? 1, op.BonusMin ?? 0, op.BonusAdena ?? 0);
    }

    static SetCondOp ReadCond(OpDto op, string where, int questId)
    {
        if (op.Cond is not int cond || cond is < 0 or > 255)
            throw new InvalidDataException($"{where} quest {questId} set_cond needs cond 0..255.");
        return new SetCondOp(cond);
    }

    static SetVarOp ReadSet(OpDto op, string where, int questId)
    {
        string key = (op.Key ?? "").Trim();
        string value = op.Value ?? "";
        if (!QuestJournal.IsStoredVar(key, value))
            throw new InvalidDataException($"{where} quest {questId} set key is not a stored variable.");
        if (key == QuestProgress.CondVar && (!int.TryParse(value, out int cond) || cond is < 0 or > 255))
            throw new InvalidDataException($"{where} quest {questId} cond value must be 0..255.");
        if (key == QuestProgress.FlagsVar && !int.TryParse(value, out _))
            throw new InvalidDataException($"{where} quest {questId} {QuestProgress.FlagsVar} must be an integer.");
        return new SetVarOp(key, value);
    }

    static UnsetVarOp ReadUnset(OpDto op, string where, int questId)
    {
        string key = (op.Key ?? "").Trim();
        if (!QuestJournal.IsStoredVar(key, ""))
            throw new InvalidDataException($"{where} quest {questId} unset key is not a stored variable.");
        return new UnsetVarOp(key);
    }

    static GiveItemOp ReadGive(OpDto op, ItemCatalog items, string where, int questId)
    {
        int count = op.Count ?? 1;
        if (op.ItemId == AdenaItemId)
            throw new InvalidDataException($"{where} quest {questId} give_item cannot grant adena. Use give_adena.");
        if (count < 1 || !items.TryGet(op.ItemId, out _))
            throw new InvalidDataException($"{where} quest {questId} give_item {op.ItemId} is unknown or has a bad count.");
        return new GiveItemOp(op.ItemId, count);
    }

    static GiveAdenaOp ReadAdena(OpDto op, string where, int questId)
    {
        int count = op.Count ?? 1;
        if (count < 1)
            throw new InvalidDataException($"{where} quest {questId} give_adena needs a positive count.");
        return new GiveAdenaOp(count);
    }

    static GiveExpSpOp ReadExp(OpDto op, string where, int questId)
    {
        long exp = op.Exp ?? 0;
        int sp = op.Sp ?? 0;
        if (exp < 0 || sp < 0 || (exp == 0 && sp == 0))
            throw new InvalidDataException($"{where} quest {questId} give_exp_sp needs a non-negative exp or sp.");
        return new GiveExpSpOp(exp, sp);
    }

    static TakeItemOp ReadTake(OpDto op, ItemCatalog items, string where, int questId)
    {
        int count = op.Count ?? 1;
        if (count < 1 || !items.TryGet(op.ItemId, out _))
            throw new InvalidDataException($"{where} quest {questId} take_item {op.ItemId} is unknown or has a bad count.");
        return new TakeItemOp(op.ItemId, count);
    }

    static SoundOp ReadSound(OpDto op, string where, int questId)
    {
        string name = (op.Name ?? "").Trim();
        if (!QuestSounds.Known(name))
            throw new InvalidDataException($"{where} quest {questId} sound '{name}' is unknown.");
        return new SoundOp(name);
    }

    static QuestHook ParseHook(string? text, string where, int questId) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "first_talk" => QuestHook.FirstTalk,
        "talk" => QuestHook.Talk,
        "event" => QuestHook.Event,
        "kill" => QuestHook.Kill,
        "attack" => QuestHook.Attack,
        _ => throw new InvalidDataException($"{where} quest {questId} has unknown hook '{text}'.")
    };

    static QuestWhen ParseWhen(string? text, string where, int questId) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        "absent" => QuestWhen.Absent,
        "any" => QuestWhen.Any,
        "created" => QuestWhen.Created,
        "started" => QuestWhen.Started,
        "completed" => QuestWhen.Completed,
        _ => throw new InvalidDataException($"{where} quest {questId} has unknown when '{text}'.")
    };

    static bool IsEvent(string text)
    {
        if (text.Length is < 1 or > 64)
            return false;
        foreach (char c in text)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
                return false;
        }

        return true;
    }

    static string ResolveHtml(DialogDto dialog, string? htmlBaseDir, string where, int questId)
    {
        string file = (dialog.HtmlFile ?? "").Trim();
        if (file.Length == 0)
            return dialog.Html ?? "";

        if (htmlBaseDir == null)
            throw new InvalidDataException($"{where} quest {questId} uses htmlFile but has no quest folder.");
        if (file.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(file) || file.Contains('/') || file.Contains('\\'))
            throw new InvalidDataException($"{where} quest {questId} htmlFile must be a plain file name in the quest folder.");

        string path = Path.Combine(htmlBaseDir, file);
        if (!File.Exists(path))
            throw new FileNotFoundException($"{where} quest {questId} htmlFile '{file}' not found.", path);
        return File.ReadAllText(path);
    }
}
