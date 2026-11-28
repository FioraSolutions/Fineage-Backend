using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Simulation;

namespace Fineage.Zone.World;

/// <summary>
/// Quest 350 Enhance Your Weapon soul crystals (Mobius Q350 levelSoulCrystals behaviour): a crystal used on a listed
/// monster casts Soul Crystal (2096) and marks the caster as an absorber; when the monster dies, holders of exactly one
/// levelable crystal (quest started) roll by the monster's absorb mode and the crystal steps up one level.
/// </summary>
public sealed partial class ZoneWorld
{
    public const int SoulCrystalSucceeded = 974, SoulCrystalFailed = 975, SoulCrystalResonation = 977,
        SoulCrystalRefused = 978;

    readonly SoulCrystalCatalog _soulCrystals = SoulCrystalCatalog.LoadDefault();
    internal IRegionRandom SoulCrystalRandom { get; set; } = null!;

    /// <summary>Mobius ItemSkills for a soul crystal: the selected monster, then the Soul Crystal cast.</summary>
    string? TryUseSoulCrystal(ZoneSession session, ZoneEntity player, ItemInstance item)
    {
        int targetId = session.SelectedTargetId;
        if (targetId <= 0 || !_byId.TryGetValue(targetId, out var target) || target.Kind != ZoneEntityKind.Npc ||
            !target.Attackable || target.Dead || target.CurHp <= 0 || !VisibleTo(player.ObjectId, targetId))
        {
            Notice(session, ManorProtocol.InvalidTarget, "Invalid target.");
            return ManorProtocol.Target;
        }

        return CastManorSkill(session, player, target, SoulCrystalCatalog.SoulCrystalSkillId, item.ItemId,
            (t, _) => AbsorbSoul(player, t));
    }

    /// <summary>Mobius <c>onSkillSee</c> → <c>Attackable.addAbsorber</c>: remember the caster and the monster HP at the cast.</summary>
    void AbsorbSoul(ZoneEntity player, ZoneEntity target)
    {
        if (player.Dead || target.Dead || target.CurHp <= 0 || !target.Attackable || !_soulCrystals.HasRules(target.NpcId))
            return;
        (target.SoulAbsorbers ??= [])[player.ObjectId] = target.CurHp;
    }

    /// <summary>Mobius <c>Q00350.levelSoulCrystals</c> on the kill.</summary>
    void LevelSoulCrystals(ZoneEntity killer, ZoneEntity npc)
    {
        var absorbers = npc.SoulAbsorbers;
        npc.SoulAbsorbers = null;
        if (!_soulCrystals.HasRules(npc.NpcId))
            return;

        var party = PartyMembersOf(killer);
        var holders = new Dictionary<ZoneEntity, SoulCrystalStep>();
        int maxLevel = 0;
        IEnumerable<ZoneEntity> candidates = _soulCrystals.IsPartyLeveling(npc.NpcId) && party.Count > 1
            ? party.Where(m => m == killer || WithinQuestPartyRange(killer, m))
            : [killer];
        foreach (var member in candidates)
        {
            if (member.Inventory is not { } bag || member.Quests.Find(SoulCrystalCatalog.QuestId) is not
                { Phase: Fineage.Game.Quests.QuestPhase.Started } || _soulCrystals.Single(bag) is not { } step)
                continue;
            holders[member] = step;
            if (step.Level > maxLevel && _soulCrystals.Rule(npc.NpcId, step.Level) != null)
                maxLevel = step.Level;
        }

        if (holders.Count == 0 || _soulCrystals.Rule(npc.NpcId, maxLevel) is not { } rule)
            return;
        if (rule.SkillNeeded)
        {
            // The killer must have used the crystal on this monster at no more than half of its HP.
            if (absorbers == null || !absorbers.TryGetValue(killer.ObjectId, out int hpAtCast) || hpAtCast > npc.MaxHp / 2.0)
                return;
        }

        void Roll(ZoneEntity member)
        {
            if (holders.TryGetValue(member, out var step))
                LevelCrystal(member, step, npc);
        }

        var soulRng = Rng(SoulCrystalRandom, killer);
        switch (rule.Mode)
        {
            case SoulAbsorbMode.PartyOneRandom:
                Roll(party.Count > 1 ? party[soulRng.Next(party.Count)] : killer);
                break;
            case SoulAbsorbMode.PartyRandom:
                if (party.Count > 1)
                {
                    var pool = new List<ZoneEntity>(party);
                    while (pool.Count > 0 && soulRng.Next(100) < 33)
                    {
                        int i = soulRng.Next(pool.Count);
                        Roll(pool[i]);
                        pool.RemoveAt(i);
                    }
                }
                else if (soulRng.Next(100) < 33)
                    Roll(killer);
                break;
            case SoulAbsorbMode.FullParty:
                foreach (var member in party.Count > 1 ? party : [killer])
                    Roll(member);
                break;
            default:
                Roll(killer);
                break;
        }
    }

    void LevelCrystal(ZoneEntity player, SoulCrystalStep step, ZoneEntity npc)
    {
        if (player.Session is not { } session || player.Inventory is not { } bag)
            return;
        if (_soulCrystals.Rule(npc.NpcId, step.Level) is not { } rule)
        {
            Notice(session, SoulCrystalRefused, "The soul crystal is refusing to absorb a soul.");
            return;
        }

        if (Rng(SoulCrystalRandom, player).Next(100) > rule.Chance)
        {
            Notice(session, SoulCrystalFailed, "The soul crystal was not able to absorb a soul.");
            return;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        if (!Take(bag, step.ItemId, 1, changes))
            return;
        FanInventory(session, Collapse(changes));
        GiveItem(session, bag, step.NextItemId, 1);
        Notice(session, SoulCrystalSucceeded, "The soul crystal succeeded in absorbing a soul.");
        string name = _game.Items.Require(step.NextItemId).Name;
        SelfLine(session, 54, $"You have earned {name}.", name);
        ZoneLog.Info($"[soul] player={player.ObjectId} crystal {step.ItemId}->{step.NextItemId} npc={npc.NpcId}");
    }

    List<ZoneEntity> PartyMembersOf(ZoneEntity player)
    {
        var list = new List<ZoneEntity> { player };
        if (_party.Of(player.ObjectId) is not { } party)
            return list;
        foreach (int id in party.MemberIds)
        {
            if (id != player.ObjectId && _byId.TryGetValue(id, out var m) && m.Kind == ZoneEntityKind.Player)
                list.Add(m);
        }

        return list;
    }

    static bool WithinQuestPartyRange(ZoneEntity a, ZoneEntity b)
    {
        long dx = a.X - (long)b.X, dy = a.Y - (long)b.Y, dz = a.Z - (long)b.Z;
        return dx * dx + dy * dy + dz * dz <= (long)QuestPartyRange * QuestPartyRange;
    }

    /// <summary>Test hook: the Soul Crystal skill landed on <paramref name="npc"/> from <paramref name="player"/>.</summary>
    internal void SoulAbsorbForTest(ZoneEntity player, ZoneEntity npc)
    {
        using (_affinity.Step())
            AbsorbSoul(player, npc);
    }

    /// <summary>Test hook: <paramref name="npc"/> died to <paramref name="killer"/> (soul crystal step only).</summary>
    internal void SoulKillForTest(ZoneEntity killer, ZoneEntity npc)
    {
        using (_affinity.Step())
            LevelSoulCrystals(killer, npc);
    }
}