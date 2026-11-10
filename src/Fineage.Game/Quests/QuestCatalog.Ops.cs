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
            _ => throw new InvalidDataException($"{where} quest {questId} has unknown op '{op.Op}'.")
        };
    }

    static SpawnNpcOp ReadSpawn(OpDto op, string where, int questId)
    {
        if (op.NpcId <= 0)
            throw new InvalidDataException($"{where} quest {questId} spawn_npc needs a positive npcId.");
        return new SpawnNpcOp(op.NpcId);
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
