using Fineage.Game.Quests;
using Fineage.Game.Skills;
using Fineage.Protocol;
using Fineage.Zone.Combat;
using Fineage.Zone.Skills;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    public Task<string?> TrySkillUseAsync(
        ZoneSession session,
        int skillId,
        int targetObjectId,
        CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");

        string? error;
        lock (_gate)
            error = TrySkillUse(session, new SkillUseIntent(skillId, targetObjectId));
        if (error != null)
            return Task.FromResult<string?>(error);

        return Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Trainer mode. false (default) keeps the pre-trainer behaviour: NPC-learned tree skills are granted free at their
    /// level. true = Mobius: only autoGet rows are free; NPC rows must be bought with SP at a trainer (learn_skill).
    /// Env <c>FINEAGE_TRAINER_SP_LEARN=1</c>. Learned skills are persisted either way (character_skills).
    /// </summary>
    public bool TrainerSpLearn { get; set; } =
        Environment.GetEnvironmentVariable("FINEAGE_TRAINER_SP_LEARN") is "1" or "true";

    /// <summary>
    /// Rebuild the live skill book from the class auto-tree + persisted learned rows.
    /// Does <b>not</b> clear <see cref="ZoneEntity.ClanSkills"/> — those come from the clan (Mobius
    /// <c>Clan.addSkillEffects</c>) and are merged at fold time via <c>PassiveSkillLevels</c>.
    /// Skill enchant: when EnchantSkill updates a learned level, that higher level lands here through
    /// <see cref="ZoneEntity.LearnedSkills"/> and is what <c>skill-passives.json</c> ValueAt uses.
    /// </summary>
    void RefreshKnownSkills(ZoneEntity entity)
    {
        entity.KnownSkills.Clear();
        foreach (var row in PlayerSkillBook.AutoSkills(_game.SkillTrees, _game.Skills, entity.ClassId, entity.Level, _game.Classes,
                     includeNpcSkills: !TrainerSpLearn))
            entity.KnownSkills[row.SkillId] = row.Level;
        foreach (var pair in entity.LearnedSkills)
        {
            if (_game.Skills.Find(pair.Key) == null)
                continue;
            if (!entity.KnownSkills.TryGetValue(pair.Key, out int have) || have < pair.Value)
                entity.KnownSkills[pair.Key] = pair.Value;
        }

        // Passive runSpd masteries (Quick Step, Agile Movement, armor sets) are part of the live run speed.
        RefreshRunSpeed(entity);
    }

    internal string? TrainerServiceForTest(ZoneSession session, string intent, int skillId = -1, int npcObjectId = 0)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player))
                return "world";
            var npc = new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = 30157, Name = "Greenis", ObjectId = npcObjectId };
            return intent == NpcServiceIntent.SkillList
                ? ShowTrainerSkills(session, player, npc)
                : LearnTrainerSkill(session, player, npc, skillId);
        }
    }

    /// <summary>
    /// Mobius <c>RequestAcquireSkillInfo</c> (the confirmation page) and <c>RequestAcquireSkill</c> (the purchase)
    /// straight from the trainer window, without a bypass round trip.
    /// </summary>
    public Task<string?> TryAcquireSkillAsync(ZoneSession session, int skillId, int level, int skillType, bool informing, CancellationToken ct)
    {
        if (!IsLive(session))
            return Task.FromResult<string?>("world");
        if (skillId <= 0 || level <= 0)
            return Task.FromResult<string?>("bad_request");

        string? error = null;
        lock (_gate)
        {
            if (!_byId.TryGetValue(session.ObjectId, out var player) || player.Session == null)
                return Task.FromResult<string?>("world");
            if (player.Dead)
                return Task.FromResult<string?>(InventoryProtocol.Dead);
            if (informing)
                PublishAcquireSkillInfo(session, player, skillId, level, skillType);
            else
                error = LearnTrainerSkill(session, player, TrainerNpcOf(session), skillId);
        }

        if (error != null)
            return Task.FromResult<string?>(error);
        return Task.FromResult<string?>(null);
    }

    /// <summary>The trainer this player last opened, or a stand-in so the gate does not need the NPC in range.</summary>
    ZoneEntity TrainerNpcOf(ZoneSession session) =>
        _byId.TryGetValue(session.TrainerNpcId, out var npc) && npc.Kind == ZoneEntityKind.Npc
            ? npc
            : new ZoneEntity { Kind = ZoneEntityKind.Npc, NpcId = 0, Name = "Trainer", ObjectId = 0 };

    void LoadLearnedSkills(ZoneEntity entity, Fineage.Accounts.CharacterRecord? stored)
    {
        entity.LearnedSkills.Clear();
        foreach (var s in stored?.LearnedSkills ?? [])
        {
            if (s.IsValid)
                entity.LearnedSkills[s.SkillId] = s.Level;
        }
    }

    string? ShowTrainerSkills(ZoneSession session, ZoneEntity player, ZoneEntity npc)
    {
        var rows = TrainerSkills.Available(_game.SkillTrees, _game.Skills, player.ClassId, player.Level, player.KnownSkills, _game.Classes);
        if (!TrainerSpLearn && rows.Count == 0)
        {
            var html = new System.Text.StringBuilder("<html><body>%npcname%:<br>");
            html.Append("In this world your class skills come to you on their own when they reach their level.<br>");
            int next = TrainerSkills.NextLearnLevel(_game.SkillTrees, player.ClassId, player.Level, _game.Classes);
            if (next > 0)
                html.Append("Your next new skill arrives at level ").Append(next).Append('.');
            html.Append("</body></html>");
            SendHtml(session, player, npc, html.ToString());
            return null;
        }

        // Mobius Folk.showSkillList -> AcquireSkillList: the trainer window is the primary surface on Net.
        session.TrainerNpcId = npc.ObjectId;
        PublishAcquireSkillList(session, player, rows);
        if (rows.Count == 0)
        {
            int next = TrainerSkills.NextLearnLevel(_game.SkillTrees, player.ClassId, player.Level, _game.Classes);
            // Mobius SystemMessage 607 "You do not have any further skills to learn. Come back when you have reached Level S1."
            SendHtml(session, player, npc, NothingToLearnHtml(next));
            return null;
        }

        // The HTML list stays for clients without the acquire window; it links the same learn_skill bypass.
        SendHtml(session, player, npc, TrainerListHtml(player, rows));
        return null;
    }

    static string NothingToLearnHtml(int nextLevel) =>
        new System.Text.StringBuilder("<html><body>%npcname%:<br>")
            .Append(nextLevel > 0
                ? $"There is nothing more I can teach you now. Come back at level {nextLevel}."
                : "There is nothing more I can teach you.")
            .Append("</body></html>")
            .ToString();

    string TrainerListHtml(ZoneEntity player, IReadOnlyList<LearnableSkill> rows)
    {
        var html = new System.Text.StringBuilder("<html><body>%npcname%:<br>");
        html.Append("SP: ").Append(player.Sp).Append("<br>");
        foreach (var r in rows)
        {
            string name = _game.Skills.Find(r.SkillId)?.Name ?? $"Skill {r.SkillId}";
            html.Append("<a action=\"bypass -h npc_%objectId%_learn_skill ").Append(r.SkillId).Append("\">")
                .Append(HtmlText(name)).Append(" Lv ").Append(r.Level).Append(" - ").Append(r.LevelUpSp).Append(" SP</a><br>");
        }

        html.Append("</body></html>");
        return html.ToString();
    }

    /// <summary>
    /// Mobius <c>AcquireSkillList</c> (<c>AcquireSkillType.USUAL</c>): one row per offer with the next level, the
    /// skill's highest level in the class tree and the SP price. Requirement bit 1 is Mobius' "previous level learned".
    /// </summary>
    void PublishAcquireSkillList(ZoneSession session, ZoneEntity player, IReadOnlyList<LearnableSkill> rows)
    {
        if (NetBridge == null || session.ObjectId <= 0)
            return;
        var entries = new AcquireSkillEntryMsg[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            entries[i] = new AcquireSkillEntryMsg
            {
                SkillId = row.SkillId,
                NextLevel = row.Level,
                MaxLevel = MaxTreeLevel(player.ClassId, row.SkillId, row.Level),
                SpCost = row.LevelUpSp,
                // Mobius Folk.showSkillList passes 0; the previous-level rule is the client's own display hint.
                Requirements = row.Items.Count > 0 ? 2 : 0
            };
        }

        NetBridge.FanoutAcquireSkillList([player.ObjectId], new AcquireSkillListMsg
        {
            SkillType = AcquireSkillTypeUsual,
            Skills = entries
        });
    }

    /// <summary>Mobius <c>AcquireSkillType.USUAL</c> ordinal.</summary>
    internal const int AcquireSkillTypeUsual = 0;

    /// <summary>SystemMessage 276 "You do not have the necessary materials or prerequisites to learn this skill."</summary>
    internal const int PrerequisiteMessageId = 276;

    /// <summary>SystemMessage 2208 "You do not meet the skill level requirements."</summary>
    internal const int SkillLevelRequirementMessageId = 2208;

    /// <summary>Highest <c>level</c> the class chain's tree lists for this skill (Mobius <c>getMaxLevel</c>).</summary>
    int MaxTreeLevel(int classId, int skillId, int fallback)
    {
        int max = fallback;
        IReadOnlyList<int> chain = _game.Classes != null && _game.Classes.TryGet(classId, out _)
            ? _game.Classes.ParentChain(classId)
            : [classId];
        foreach (int chainClassId in chain)
        {
            var tree = _game.SkillTrees.FindClass(chainClassId);
            if (tree == null)
                continue;
            foreach (var e in tree.Skills)
            {
                if (e.SkillId == skillId && e.Level > max)
                    max = e.Level;
            }
        }

        return max;
    }

}
