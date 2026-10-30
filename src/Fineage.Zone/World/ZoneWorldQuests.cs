using Fineage.Game;
using Fineage.Game.Quests;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Npcs;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public async Task<string?> TryQuestListAsync(ZoneSession session, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";

        var toSelf = new List<Pending>();
        lock (_gate)
            QueueList(session, toSelf);
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return null;
    }

    public async Task<string?> TryQuestAbortAsync(ZoneSession session, int questId, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        if (questId <= 0)
            return "bad_request";

        QuestOutcome outcome;
        var toSelf = new List<Pending>();
        lock (_gate)
        {
            if (!TryPlayer(session, out var player))
                return "world";
            outcome = _quests.Abort(player.Quests, player.Inventory!, questId);
            if (outcome.Ok || outcome.Lines.Count > 0)
                Publish(session, player, outcome, npcObjectId: 0, npcId: 0, toSelf);
        }

        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return outcome.Ok ? null : FailureCode(outcome.Failure);
    }

    public async Task<string?> TryNpcTalkAsync(ZoneSession session, int npcObjectId, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        if (npcObjectId <= 0)
            return "bad_request";

        string? error;
        var toSelf = new List<Pending>();
        lock (_gate)
            error = Talk(session, npcObjectId, questId: 0, eventName: null, toSelf, out _);
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return error;
    }

    public async Task<string?> TryQuestEventAsync(ZoneSession session, int npcObjectId, int questId, string? eventName, CancellationToken ct)
    {
        if (!IsLive(session))
            return "world";
        if (npcObjectId <= 0 || questId <= 0)
            return "bad_request";
        eventName ??= "";
        if (eventName.Length > 64 || !EventOk(eventName))
            return "bad_request";

        string? error;
        var toSelf = new List<Pending>();
        lock (_gate)
            error = Talk(session, npcObjectId, questId, eventName, toSelf, out _);
        await FlushSelfAsync(session, toSelf, ct).ConfigureAwait(false);
        return error;
    }

    void BootstrapQuestNpcs()
    {
        var templates = NpcCatalog.LoadDefault();
        foreach (var npc in TalkingIsland.Villagers)
        {
            if (_byId.ContainsKey(npc.ObjectId))
                throw new InvalidOperationException($"Quest npc object id {npc.ObjectId} is already used.");

            templates.ById.TryGetValue(npc.NpcId, out var template);
            var entity = new ZoneEntity
            {
                ObjectId = npc.ObjectId,
                Kind = ZoneEntityKind.Npc,
                Name = npc.Name,
                Title = template?.Title ?? "",
                NpcType = template?.Type ?? "",
                X = npc.X,
                Y = npc.Y,
                Z = npc.Z,
                Heading = npc.Heading,
                NpcId = npc.NpcId,
                Attackable = false,
                MaxHp = 100,
                CurHp = 100,
                RunSpeed = npc.RunSpeed,
                WalkSpeed = npc.WalkSpeed
            };
            _byId.Add(entity.ObjectId, entity);
            _aoi.Enter(entity.ObjectId, entity.X, entity.Y);
        }
    }

    string? Talk(ZoneSession session, int npcObjectId, int questId, string? eventName, List<Pending> toSelf, out QuestOutcome? outcome)
    {
        outcome = null;
        if (!TryPlayer(session, out var player))
            return "world";
        if (!_byId.TryGetValue(npcObjectId, out var npc) || npc.Kind != ZoneEntityKind.Npc)
            return QuestProtocol.Npc;
        if (npc.Attackable)
            return NpcHtmlProtocol.Attackable;
        if (player.Dead)
            return InventoryProtocol.Dead;
        if (!InTalkRange(player, npc))
            return "range";

        // Mobius NewbieHelper onFirstTalk — before guide HTML / other quest hooks.
        if (eventName == null)
            TryNewbieHelperGift(session, player, npc, toSelf);

        outcome = eventName == null
            ? _quests.FirstTalk(player.Quests, player.Inventory!, npc.NpcId, npc.Name)
            : eventName.Length == 0
                ? _quests.Talk(player.Quests, player.Inventory!, questId, npc.NpcId)
                : _quests.AdvEvent(player.Quests, player.Inventory!, questId, npc.NpcId, eventName);
        if (IsNpcPage(outcome))
        {
            SendNpcPage(session, player, npc, 0, toSelf);
            return null;
        }

        if (outcome.Ok || outcome.Lines.Count > 0 || outcome.JournalChanged)
            Publish(session, player, outcome, npc.ObjectId, npc.NpcId, toSelf);
        return outcome.Ok ? null : FailureCode(outcome.Failure);
    }

    static bool IsNpcPage(QuestOutcome outcome) =>
        outcome.Ok
        && outcome.Hook == "default"
        && outcome.QuestId == 0
        && !outcome.JournalChanged
        && outcome.Lines.Count == 0
        && outcome.Items.Count == 0
        && outcome.Sounds.Count == 0
        && outcome.Exp == 0
        && outcome.Sp == 0;

    void Publish(ZoneSession session, ZoneEntity player, QuestOutcome outcome, int npcObjectId, int npcId, List<Pending> toSelf)
    {
        ApplyProgress(player, outcome);
        if (outcome.Items.Count > 0 && player.Inventory != null)
        {
            toSelf.Add(Pack(session, MsgType.InventoryUpdate, new InventoryUpdateMsg
            {
                Changes = outcome.Items.Select(touch => InventoryMessages.ChangeRow((int)touch.Kind, touch.Item)).ToArray()
            }));
            SaveInventory(session, player.Inventory);
        }

        foreach (var line in outcome.Lines)
        {
            toSelf.Add(Pack(session, MsgType.SystemMessage, new SystemMessageMsg
            {
                Id = (int)line.Notice,
                Params = line.Params,
                Text = line.Text
            }));
        }

        foreach (string sound in outcome.Sounds)
            toSelf.Add(Pack(session, MsgType.QuestSound, new QuestSoundMsg { Name = sound }));

        if (outcome.Html.Length > 0)
        {
            toSelf.Add(Pack(session, MsgType.QuestDialog, new QuestDialogMsg
            {
                NpcObjectId = npcObjectId,
                NpcId = npcId,
                QuestId = outcome.QuestId,
                Hook = outcome.Hook,
                Event = outcome.Event,
                Html = outcome.Html
            }));
        }

        if (outcome.JournalChanged)
            QueueList(session, toSelf);

        if (outcome.JournalChanged || outcome.Exp != 0 || outcome.Sp != 0)
            SaveQuestProgress(session, player);
    }

    void ApplyProgress(ZoneEntity player, QuestOutcome outcome) => ApplyExpSp(player, outcome.Exp, outcome.Sp);

    void ApplyExpSp(ZoneEntity player, long exp, int sp)
    {
        if (exp == 0 && sp == 0)
            return;

        var table = _game.Classes?.Experience;
        if (table == null)
        {
            player.Exp = exp > long.MaxValue - player.Exp ? long.MaxValue : player.Exp + exp;
            player.Sp = sp > int.MaxValue - player.Sp ? int.MaxValue : player.Sp + sp;
            return;
        }

        int previous = player.Level;
        var progress = table.Add(player.Exp, player.Level, player.Sp, exp, sp);
        player.Exp = progress.Exp;
        player.Sp = progress.Sp;
        if (progress.Level == previous || _game.Classes == null || !_game.Classes.TryGet(player.ClassId, out var template) || template == null)
        {
            player.Level = progress.Level;
            return;
        }

        player.Level = progress.Level;
        player.MaxHp = template.ModifiedHp(progress.Level);
        player.MaxMp = template.ModifiedMp(progress.Level);
        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
    }

    void QueueList(ZoneSession session, List<Pending> toSelf)
    {
        if (!TryPlayer(session, out var player))
            return;
        var list = _quests.List(player.Quests);
        toSelf.Add(Pack(session, MsgType.QuestList, new QuestListMsg
        {
            Active = list.Active.Select(entry => new QuestJournalEntryMsg
            {
                QuestId = entry.QuestId,
                Name = entry.Name,
                State = entry.State,
                Cond = entry.Cond,
                ProgressFlags = entry.ProgressFlags,
                JournalWord = entry.JournalWord,
                Repeatable = entry.Repeatable
            }).ToArray(),
            CompletedMask = list.CompletedMask,
            CompletedQuestIds = list.CompletedQuestIds,
            MaxActive = list.MaxActive
        }));
    }

    void LoadQuestProgress(ZoneSession session, ZoneEntity entity)
    {
        entity.Quests = new QuestJournal();
        if (_accounts == null)
            return;
        var record = _accounts.FindById(session.Account, session.ObjectId);
        if (record == null)
            return;
        entity.Exp = record.Exp;
        entity.Sp = record.Sp;
        entity.Quests = QuestJournal.Load(record.Quests);
    }

    /// <summary>
    /// Mobius <c>NewbieHelper.onFirstTalk</c>: one-time beginner kit under level 18.
    /// Does not open its own HTML — the guide page still comes from <see cref="SendNpcPage"/>.
    /// </summary>
    void TryNewbieHelperGift(ZoneSession session, ZoneEntity player, ZoneEntity npc, List<Pending> toSelf)
    {
        if (!NewbieHelperGift.IsGuide(npc.NpcId))
            return;
        if (player.Level >= NewbieHelperGift.MaxLevelExclusive)
            return;
        if (player.Inventory == null)
            return;

        var prior = player.Quests.Find(NewbieHelperGift.QuestId);
        if (prior != null && prior.Phase == QuestPhase.Completed)
            return;

        if (!_game.Items.TryGet(NewbieHelperGift.ScrollRecovery, out _) ||
            !_game.Items.TryGet(NewbieHelperGift.TravelToken, out _) ||
            !_game.Items.TryGet(NewbieHelperGift.SoulshotNovice, out _) ||
            !_game.Items.TryGet(NewbieHelperGift.SpiritshotNovice, out _))
        {
            ZoneLog.Info($"newbie-helper-gift skipped — missing item templates (npc={npc.NpcId})");
            return;
        }

        var (shotId, shotCount) = NewbieHelperGift.ShotForClass(_newbieBuffs, player.ClassId);
        var changes = new List<InventoryUpdateEntryMsg>(3);
        void Grant(int itemId, int count)
        {
            var added = player.Inventory.Add(itemId, count);
            changes.Add(InventoryMessages.ChangeRow(InventoryChange.Add, added));
        }

        Grant(shotId, shotCount);
        Grant(NewbieHelperGift.TravelToken, NewbieHelperGift.TokenCount);
        Grant(NewbieHelperGift.ScrollRecovery, NewbieHelperGift.ScrollCount);

        player.Quests.Add(NewbieHelperGift.QuestId, QuestPhase.Completed);
        toSelf.Add(Pack(session, MsgType.InventoryUpdate, new InventoryUpdateMsg { Changes = changes.ToArray() }));
        SaveInventory(session, player.Inventory);
        SaveQuestProgress(session, player);
        QueueList(session, toSelf);
        ZoneLog.Info(
            $"newbie-helper-gift object={session.ObjectId} npc={npc.NpcId} shot={shotId}x{shotCount}");
    }

    /// <summary>
    /// Mobius-style solo kill reward: template acquire exp/sp to the killer, then UserInfo and
    /// SystemMessage 95 ("You have earned $s1 experience and $s2 SP.").
    /// </summary>
    void GrantKillReward(ZoneEntity killer, ZoneEntity npc, List<Pending> outbound)
    {
        if (killer.Kind != ZoneEntityKind.Player || killer.Session == null || npc.Kind != ZoneEntityKind.Npc)
            return;
        long exp = npc.RewardExp;
        int sp = npc.RewardSp;
        if (exp <= 0 && sp <= 0)
            return;

        ApplyExpSp(killer, exp, sp);
        var session = killer.Session;
        outbound.Add(Pack(session, MsgType.SystemMessage, new SystemMessageMsg
        {
            Id = KillRewardMessageId,
            Params = [exp.ToString(System.Globalization.CultureInfo.InvariantCulture), sp.ToString(System.Globalization.CultureInfo.InvariantCulture)],
            Text = $"You have earned {exp} experience and {sp} SP."
        }));
        // StatusUpdate carries live Exp/Sp/Level; UserInfo refreshes the full sheet (vitals + template stats).
        outbound.Add(Pack(session, MsgType.StatusUpdate, new StatusUpdateMsg
        {
            ObjectId = killer.ObjectId,
            AttributeIds =
            [
                InterludeStatus.Level,
                InterludeStatus.Exp,
                InterludeStatus.Sp,
                InterludeStatus.CurHp,
                InterludeStatus.MaxHp,
                InterludeStatus.CurMp,
                InterludeStatus.MaxMp
            ],
            Values =
            [
                killer.Level,
                (int)Math.Min(int.MaxValue, killer.Exp),
                killer.Sp,
                killer.CurHp,
                killer.MaxHp,
                killer.CurMp,
                killer.MaxMp
            ]
        }));
        outbound.Add(Pack(session, MsgType.UserInfo, UserInfo(killer)));
        SaveQuestProgress(session, killer);
    }

    internal const int KillRewardMessageId = 95;

    void SaveQuestProgress(ZoneSession session, ZoneEntity entity)
    {
        if (_accounts == null)
            return;
        _accounts.TryReplaceQuests(session.Account, session.ObjectId, entity.Quests.Save());
        _accounts.TrySaveProgress(
            session.Account,
            session.ObjectId,
            entity.Level,
            entity.Exp,
            entity.Sp,
            entity.MaxHp,
            entity.CurHp,
            entity.MaxMp,
            entity.CurMp);
    }

    bool TryPlayer(ZoneSession session, out ZoneEntity player)
    {
        if (_byId.TryGetValue(session.ObjectId, out player!) &&
            player.Kind == ZoneEntityKind.Player &&
            player.Inventory != null)
            return true;
        player = null!;
        return false;
    }

    bool InTalkRange(ZoneEntity player, ZoneEntity npc) =>
        NpcHtmlProtocol.InRange(player.X, player.Y, player.Z, npc.X, npc.Y, npc.Z, _game.Quests.TalkRange);

    static bool EventOk(string text)
    {
        foreach (char c in text)
        {
            if (c is not ((>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
                return false;
        }

        return true;
    }

    static string FailureCode(QuestFailure failure) => failure switch
    {
        QuestFailure.Unknown => QuestProtocol.Unknown,
        QuestFailure.Limit => QuestProtocol.Limit,
        QuestFailure.State => QuestProtocol.State,
        QuestFailure.Abort => QuestProtocol.Abort,
        QuestFailure.Item => QuestProtocol.Item,
        QuestFailure.BadRequest => "bad_request",
        _ => "bad_request"
    };
}
