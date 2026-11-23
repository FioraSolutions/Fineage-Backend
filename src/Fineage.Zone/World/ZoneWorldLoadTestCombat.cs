using Fineage.Game;
using Fineage.Accounts;

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

        player.Level = LoadTestCombatSettings.CombatLevel;
        player.Sp = Math.Max(player.Sp, 10_000_000);
        player.ClassId = LoadTestCombatSettings.WarlordClassId;
        player.Session.ChangeClass(LoadTestCombatSettings.WarlordClassId);
        if (PlayerClasses.TryGet(LoadTestCombatSettings.WarlordClassId, out var template) && template != null)
        {
            player.BaseRunSpeed = template.RunSpeed;
            player.BaseWalkSpeed = template.WalkSpeed;
            player.Dex = template.Dex;
            player.MaxHp = template.ModifiedHp(player.Level);
            player.MaxMp = template.ModifiedMp(player.Level);
        }

        player.CurHp = player.MaxHp;
        player.CurMp = player.MaxMp;
        RefreshKnownSkills(player);

        if (player.Inventory != null && _game.Items.TryGet(LoadTestCombatSettings.PolearmItemId, out _))
        {
            player.Inventory.Add(LoadTestCombatSettings.PolearmItemId, 1);
            var weapon = player.Inventory.FindByItemId(LoadTestCombatSettings.PolearmItemId);
            if (weapon != null && !weapon.IsEquipped)
            {
                try
                {
                    player.Inventory.Equip(weapon);
                }
                catch (InvalidOperationException)
                {
                    /* equipped elsewhere */
                }
            }

            SaveInventory(player.Session, player.Inventory);
        }

        ZoneLog.Info($"loadtest combat profile object={player.ObjectId} class={player.ClassId} level={player.Level}");
    }

    internal bool LoadTestCombatProfileNeeded(ZoneEntity player) =>
        LoadTestCombatSettings.AppliesToAccount(player.Session?.Account);
}