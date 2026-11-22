using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Economy;

namespace Fineage.Zone.World;

/// <summary>P1-5e: starter kits and GM grants post against dedicated system accounts.</summary>
public sealed partial class ZoneWorld
{
    static readonly LedgerAccount StarterKitSource = LedgerAccount.Of(SystemAccount.StarterKit);
    static readonly LedgerAccount GmGrantSource = LedgerAccount.Of(SystemAccount.GmGrants);

    static bool NeedsStarterInventory(CharacterRecord? stored) => stored is null or { Items: null };

    void ApplyStarterKitLedger(ZoneEntity player)
    {
        if (player.Inventory == null)
            return;
        var kit = _game.StarterKits.ForClass(player.ClassId);
        if (kit.IsEmpty)
            return;
        RecordBagChange(
            player,
            LedgerTxKind.StarterKit,
            StarterKitSource,
            () =>
            {
                player.Inventory.AddStarterKit(kit);
                return player.ClassId;
            },
            classId => classId);
        if (player.Session != null)
        {
            player.Inventory.DiscardChanges();
            SaveInventory(player.Session, player.Inventory);
        }
    }
}