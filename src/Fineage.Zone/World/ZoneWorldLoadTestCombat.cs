using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Accounts;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>Warlord pole + AoE skills for perf load bots (real combat pipeline, no broadcast shortcuts).</summary>
public sealed partial class ZoneWorld
{
    internal void TryApplyLoadTestCombatProfile(ZoneEntity player)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Session == null)
            return;
        if (!LoadTestCombatSettings.AppliesToAccount(player.Session.Account))
            return;
        ApplyLoadTestCombatProfile(player);
    }

    /// <summary>Ungated body of <see cref="TryApplyLoadTestCombatProfile"/>; throws when the datapack cannot build the profile.</summary>
    internal void ApplyLoadTestCombatProfile(ZoneEntity player)
    {
        if (player.Kind != ZoneEntityKind.Player || player.Session == null)
            throw new InvalidOperationException($"loadtest combat profile: object {player.ObjectId} is not a live player.");

        if (!PlayerClasses.TryGet(LoadTestCombatSettings.WarlordClassId, out var template) || template == null)
            throw new InvalidOperationException(
                $"loadtest combat profile: class template {LoadTestCombatSettings.WarlordClassId} (Warlord) is missing from the datapack.");
        if (!_game.Items.TryGet(LoadTestCombatSettings.PolearmItemId, out _))
            throw new InvalidOperationException(
                $"loadtest combat profile: polearm item {LoadTestCombatSettings.PolearmItemId} is missing from the datapack.");
        if (player.Inventory == null)
            throw new InvalidOperationException($"loadtest combat profile: player {player.ObjectId} has no inventory.");

        player.Level = LoadTestCombatSettings.CombatLevel;
        // Level and Exp must agree, or the next Exp change (kill, death penalty) recomputes a different level.
        player.Exp = Math.Max(player.Exp, PlayerClasses.Experience.ExpToBeLevel(LoadTestCombatSettings.CombatLevel));
        player.Sp = Math.Max(player.Sp, 10_000_000);
        player.ClassId = LoadTestCombatSettings.WarlordClassId;
        player.Session.ChangeClass(LoadTestCombatSettings.WarlordClassId);
        player.BaseRunSpeed = template.RunSpeed;
        player.BaseWalkSpeed = template.WalkSpeed;
        player.Dex = template.Dex;
        player.MaxHp = template.ModifiedHp(player.Level);
        player.MaxMp = template.ModifiedMp(player.Level);
        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
        // Mobius trainer mode (#1049) no longer auto-grants NPC-tree skills. Perf bots still need the rotation
        // on KnownSkills: seed LearnedSkills from the class tree at combat level, then refresh.
        GrantLoadTestRotationSkills(player);
        RefreshKnownSkills(player);
        foreach (int skillId in LoadTestCombatSettings.RequiredSkillIds)
        {
            if (!player.KnownSkills.ContainsKey(skillId))
                throw new InvalidOperationException(
                    $"loadtest combat profile: class {player.ClassId} level {player.Level} does not learn skill {skillId}; " +
                    "the perf scenario skillRotation would be rejected as skill_unknown.");
        }

        // Relogs keep the pole from the previous enter: add one only when the bag has none.
        var weapon = player.Inventory.FindByItemId(LoadTestCombatSettings.PolearmItemId);
        if (weapon == null)
        {
            player.Inventory.Add(LoadTestCombatSettings.PolearmItemId, 1);
            weapon = player.Inventory.FindByItemId(LoadTestCombatSettings.PolearmItemId)
                ?? throw new InvalidOperationException(
                    $"loadtest combat profile: polearm {LoadTestCombatSettings.PolearmItemId} not in bag after add.");
        }

        if (!weapon.IsEquipped)
            player.Inventory.Equip(weapon);

        // Fee-paying flows (gatekeeper teleport, shop, private store, freight) need funds; a blank purse turns every
        // one of them into a refusal and the perf run measures the rejection path instead of the pipeline.
        if (player.Inventory.CountOf(ShopTrade.AdenaItemId) <= 0)
            player.Inventory.Add(ShopTrade.AdenaItemId, LoadTestCombatSettings.StartingAdena);

        SaveInventory(player.Session, player.Inventory);

        ZoneLog.Info($"loadtest combat profile object={player.ObjectId} class={player.ClassId} level={player.Level}");
    }

    /// <summary>
    /// Sim-thread relocation for load-test accounts only. Same path as a gatekeeper or admin move
    /// (<see cref="Relocate"/>: knownlist rebuild, TeleportToLocation fan-out, persisted position).
    /// </summary>
    internal string? TryLoadTestTeleport(ZoneSession session, int x, int y, int z)
    {
        if (!LoadTestCombatSettings.AllowsTeleport(session.Account))
            return "unknown_opcode";
        if (!IsLive(session) || !_byId.TryGetValue(session.ObjectId, out var player))
            return "world";
        if (player.CurHp <= 0)
            return "dead";
        if (!Loaded(x, y))
            return NpcServiceProtocol.TeleportRefusalCode(NpcServiceProtocol.DestinationUnloaded);
        Relocate(player, x, y, z);
        return null;
    }

    internal bool LoadTestCombatProfileNeeded(ZoneEntity player) =>
        LoadTestCombatSettings.AppliesToAccount(player.Session?.Account);

    void GrantLoadTestRotationSkills(ZoneEntity player)
    {
        var tree = _game.SkillTrees.FindClass(player.ClassId)
            ?? throw new InvalidOperationException(
                $"loadtest combat profile: skill tree for class {player.ClassId} is missing from the datapack.");
        foreach (int skillId in LoadTestCombatSettings.RequiredSkillIds)
        {
            int level = 0;
            foreach (var row in tree.Skills)
            {
                if (row.SkillId != skillId || row.GetLevel > player.Level)
                    continue;
                if (row.Level > level)
                    level = row.Level;
            }

            if (level <= 0)
                throw new InvalidOperationException(
                    $"loadtest combat profile: class {player.ClassId} level {player.Level} has no tree row for skill {skillId}.");
            if (!player.LearnedSkills.TryGetValue(skillId, out int have) || have < level)
                player.LearnedSkills[skillId] = level;
        }
    }
}