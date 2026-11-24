using System.Text;

namespace Fineage.Game.Quests;

public sealed partial class QuestMachine
{
    static QuestDialog? Find(QuestDefinition quest, QuestHook hook, int npcId, string eventName, QuestProgress? state, QuestActor? actor = null, PlayerInventory? inventory = null, Func<double>? random = null, string? ownedMode = null)
    {
        if (!quest.DialogsByKey.TryGetValue(QuestDialogKey.Of(hook, npcId, eventName), out var candidates))
            return null;

        foreach (var dialog in candidates)
        {
            if (!Reaches(dialog, state, actor, inventory, ownedMode))
                continue;
            // Random branches roll only when the engine acts (talk / event / kill), never on a quest-list probe.
            if (dialog.Chance is double chance && random != null && random() >= chance)
                continue;
            return dialog;
        }

        return null;
    }

    /// <summary>
    /// The non-rolling gates of one step: owned mode, journal state, actor, and inventory. <see cref="Find"/> adds the
    /// chance roll on top; a quest-list probe asks this alone, so it never consumes a draw.
    /// </summary>
    static bool Reaches(QuestDialog dialog, QuestProgress? state, QuestActor? actor, PlayerInventory? inventory, string? ownedMode)
    {
        // Owned steps run only for the owner of a quest spawn: "solo" credit also allows "any" rows.
        if (ownedMode == null ? dialog.Owned != null : dialog.Owned == null || (dialog.Owned == "solo" && ownedMode != "solo"))
            return false;
        if (!WhenMatches(dialog, state))
            return false;
        if (!ActorMatches(dialog, actor))
            return false;
        if (dialog.HasItem != 0 && inventory != null && inventory.CountOf(dialog.HasItem) < dialog.MinCount)
            return false;
        if (!ItemsMatch(dialog, inventory))
            return false;
        if (!VarsMatch(dialog, state))
            return false;
        if (!PetMatches(dialog, actor, state) || !ExtraItemsMatch(dialog, inventory, state))
            return false;
        if (dialog.Weapons is { } weapons && inventory != null &&
            !(inventory.GetEquipped(PaperdollSlot.RHand) is { } held && weapons.Contains(held.ItemId)))
            return false;
        return true;
    }

    /// <summary>
    /// Race / level gates. Datapacks list the passing branch first and the refusal pages after,
    /// mirroring Mobius <c>race != ELF ? 01 : level &lt; MIN ? 02 : 03</c>. No actor (legacy callers) skips gates.
    /// </summary>
    static bool ActorMatches(QuestDialog dialog, QuestActor? actor)
    {
        if (actor is not { } who)
            return true;
        if (dialog.Races is { Count: > 0 } races && !races.Contains(who.Race))
            return false;
        if (dialog.MinLevel > 0 && who.Level < dialog.MinLevel)
            return false;
        if (dialog.MaxLevel > 0 && who.Level > dialog.MaxLevel)
            return false;
        if (dialog.Classes is { Count: > 0 } classes && !classes.Contains(who.ClassId))
            return false;
        if (dialog.IsMage is bool wantMage && IsMageClass(who.ClassId) != wantMage)
            return false;
        if (dialog.SubClass is bool wantSub && who.SubClass != wantSub)
            return false;
        if (dialog.InRiftPeace is bool peace && (who.InRiftPeace ?? false) != peace)
            return false;
        if (dialog.InRift is bool inRift && (who.InRift ?? false) != inRift)
            return false;
        if (dialog.MaxStarted > 0 && who.StartedQuestCount > dialog.MaxStarted)
            return false;
        return true;
    }

    /// <summary>
    /// Interlude mystic class ids (root in human/elven/dark/orc mystic). Used for Mobius <c>isMageClass</c> gates.
    /// </summary>
    static readonly HashSet<int> MageClassIds =
    [
        10, 11, 12, 13, 14, 15, 16, 17, 25, 26, 27, 28, 29, 30, 38, 39, 40, 41, 42, 43, 49, 50, 51, 52,
        94, 95, 96, 97, 98, 103, 104, 105, 110, 111, 112, 115, 116
    ];

    static bool IsMageClass(int classId) => classId >= 0 && MageClassIds.Contains(classId);

    /// <summary>
    /// Mobius path quests branch on <c>hasQuestItems(a, b, ...)</c> (all held) and <c>!hasAtLeastOneQuestItem(...)</c> (none held).
    /// No inventory (quest-list probe) skips item gates, as <c>hasItem</c> does.
    /// </summary>
    static bool ItemsMatch(QuestDialog dialog, PlayerInventory? inventory)
    {
        if (inventory == null)
            return true;
        foreach (int id in dialog.HasItems ?? [])
        {
            if (inventory.CountOf(id) <= 0)
                return false;
        }

        foreach (int id in dialog.LacksItems ?? [])
        {
            if (inventory.CountOf(id) > 0)
                return false;
        }

        // Mobius getQuestItemsCount(id) >= n for several items at once.
        foreach (var pair in dialog.AtLeast ?? new Dictionary<int, int>())
        {
            if (inventory.CountOf(pair.Key) < pair.Value)
                return false;
        }

        foreach (var pair in dialog.Below ?? new Dictionary<int, int>())
        {
            if (inventory.CountOf(pair.Key) >= pair.Value)
                return false;
        }

        // Mobius hasAtLeastOneQuestItem.
        if (dialog.HasAny is { Count: > 0 } any)
        {
            bool hit = false;
            foreach (int id in any)
            {
                if (inventory.CountOf(id) > 0)
                {
                    hit = true;
                    break;
                }
            }

            if (!hit)
                return false;
        }

        return true;
    }

    /// <summary>Var holding the registered pet collar object id (Mobius Q421 <c>summonOid</c>).</summary>
    public const string PetVar = "summonOid";

    static bool PetMatches(QuestDialog dialog, QuestActor? actor, QuestProgress? state)
    {
        if (actor is not { } who)
            return true;
        if (dialog.ByPet is bool byPet && who.ByPet != byPet)
            return false;
        if (dialog.Pet == null)
            return true;
        int pet = who.PetControlObjectId;
        bool registered = pet != 0 && pet == IntVar(state, PetVar);
        return dialog.Pet switch
        {
            "none" => pet == 0,
            "out" => pet != 0,
            "registered" => registered,
            "other" => pet != 0 && !registered,
            _ => false
        };
    }

    static bool ExtraItemsMatch(QuestDialog dialog, PlayerInventory? inventory, QuestProgress? state)
    {
        if (inventory == null)
            return true;
        if (dialog.MinEnchant > 0 &&
            !inventory.Items.Any(i => (dialog.HasAny ?? []).Contains(i.ItemId) && i.EnchantLevel >= dialog.MinEnchant))
            return false;
        if (dialog.SumItems is { Count: > 0 } sumItems)
        {
            long sum = 0;
            foreach (int id in sumItems)
                sum += inventory.CountOf(id);
            if (sum < dialog.SumMin || (dialog.SumMax >= 0 && sum > dialog.SumMax))
                return false;
        }

        if (dialog.HoldsVarItem is { } key && (IntVar(state, key) is var oid && (oid == 0 || inventory.Find(oid) == null)))
            return false;
        return true;
    }

    static int IntVar(QuestProgress? state, string key) =>
        state?.Get(key) is { } raw && int.TryParse(raw, out int value) ? value : 0;

    static bool VarsMatch(QuestDialog dialog, QuestProgress? state)
    {
        foreach (var (key, min) in dialog.VarAtLeast ?? new Dictionary<string, int>())
        {
            if (IntVar(state, key) < min)
                return false;
        }
        foreach (var (key, below) in dialog.VarBelow ?? new Dictionary<string, int>())
        {
            if (IntVar(state, key) >= below)
                return false;
        }
        return true;
    }

    static bool WhenMatches(QuestDialog dialog, QuestProgress? state)
    {
        switch (dialog.When)
        {
            case QuestWhen.Absent:
                if (state != null)
                    return false;
                break;
            case QuestWhen.Any:
                break;
            case QuestWhen.Created:
                if (state == null || state.Phase != QuestPhase.Created)
                    return false;
                break;
            case QuestWhen.Started:
                if (state == null || state.Phase != QuestPhase.Started)
                    return false;
                break;
            case QuestWhen.Completed:
                if (state == null || state.Phase != QuestPhase.Completed)
                    return false;
                break;
        }

        if (dialog.Cond is int cond && (state == null || state.Cond != cond))
            return false;
        if (dialog.MinCond > 0 && (state == null || state.Cond < dialog.MinCond))
            return false;
        if (dialog.MaxCond > 0 && (state == null || state.Cond > dialog.MaxCond))
            return false;
        if (dialog.WhenVar != null)
        {
            if (state == null || state.Get(dialog.WhenVar) != dialog.WhenValue)
                return false;
        }

        return true;
    }

    static string DefaultHtml(string npcName, IReadOnlyList<(int Id, string Name)> links)
    {
        var html = new StringBuilder();
        html.Append("<html><body>").Append(Escape(npcName)).Append(":<br>");
        if (links.Count == 0)
            html.Append("I have nothing for you.");
        else
        {
            foreach (var link in links)
            {
                html.Append("<a action=\"quest_talk:").Append(link.Id).Append("\">")
                    .Append(Escape(link.Name)).Append("</a><br>");
            }
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);

    static QuestLine Line(QuestNotice notice, string text, params string[] args) => new(notice, text, args);

    static QuestOutcome Fail(
        QuestFailure failure,
        string message,
        int questId,
        string hook,
        string evt = "",
        IReadOnlyList<QuestLine>? lines = null) => new()
        {
            Failure = failure,
            Message = message,
            QuestId = questId,
            Hook = hook,
            Event = evt,
            Lines = lines ?? []
        };

    sealed class Plan
    {
        public Plan(QuestProgress? current)
        {
            if (current == null)
            {
                Present = false;
                Phase = QuestPhase.Created;
                Vars = new Dictionary<string, string>(StringComparer.Ordinal);
                return;
            }

            Present = true;
            Phase = current.Phase;
            Vars = new Dictionary<string, string>(current.Vars, StringComparer.Ordinal);
        }

        public bool Present;
        public bool Delete;
        public QuestPhase Phase;
        public Dictionary<string, string> Vars;
        public Dictionary<int, int> Delta { get; } = [];
        public List<string> Sounds { get; } = [];
        public List<QuestLine> Lines { get; } = [];
        public List<int> Spawns { get; } = [];
        /// <summary>Object id → new item id (enchant kept), applied at commit.</summary>
        public List<(int ObjectId, int NewItemId)> Exchanges { get; } = [];
        public List<QuestSpawnPoint> SpawnsAt { get; } = [];
        public bool Noble;
        public (int X, int Y, int Z)? Teleport;
        public int Social;
        public QuestExtras Extras { get; } = new();
        public long Exp;
        public int Sp;
        public List<TutorialUiOp> TutorialUi { get; } = [];
    }
}
