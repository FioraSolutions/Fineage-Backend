using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Services;
using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Fineage.Zone.NpcHtml;

namespace Fineage.Zone.World;

public sealed partial class ZoneWorld
{
    static readonly (int CaseId, int FromItem, int ToItem, int MinLevel)[] PetEvolveRoutes =
    [
        (1, 9882, 10307, 55),
        (2, 4422, 10308, 55),
        (3, 4423, 10309, 55),
        (4, 4424, 10310, 55),
        (5, 10426, 10611, 70),
    ];

    string? ApplyPetEvolve(ZoneSession session, ZoneEntity player, ZoneEntity npc, int caseId)
    {
        var route = PetEvolveRoutes.FirstOrDefault(r => r.CaseId == caseId);
        if (route.FromItem == 0)
            return "bad_request";
        bool ok = TryEvolvePetCollar(session, player, route.FromItem, route.ToItem, route.MinLevel);
        SendHtml(session, player, npc, ok ? PetEvolveOkHtml : PetEvolveNoHtml);
        return null;
    }

    const string PetEvolveOkHtml =
        "<html><body>Your pet has successfully evolved.</body></html>";

    const string PetEvolveNoHtml =
        "<html><body>Your pet cannot evolve.<br1>You do not have a summon, or the required collar, or the level is too low.</body></html>";

    /// <summary>Mobius <c>PetManager.doEvolve</c> / <c>doRestore</c>: swap collar item id and keep progress.</summary>
    internal bool TryEvolvePetCollar(ZoneSession session, ZoneEntity player, int fromItemId, int toItemId, int minLevel)
    {
        if (_pets.ByItem(toItemId) is not { } toTemplate || _pets.ByItem(fromItemId) is not { } fromTemplate)
            return false;

        var summon = PetEntityOf(player);
        ItemInstance? collar;
        StoredPet? stored;
        if (summon is { } s && PetOf(s) is { } live)
        {
            collar = player.Inventory!.Find(live.ControlObjectId);
            if (collar == null || collar.ItemId != fromItemId || s.Level < minLevel || s.NpcId != fromTemplate.NpcId)
                return false;
            StorePet(player, s);
            UnsummonPet(player, s, 0);
            stored = LoadPet(collar.ObjectId);
        }
        else
        {
            collar = player.Inventory!.Items.FirstOrDefault(i => i.ItemId == fromItemId && !i.IsEquipped);
            if (collar == null)
                return false;
            stored = LoadPet(collar.ObjectId);
            int level = collar.EnchantLevel > 0 ? collar.EnchantLevel : stored?.Level ?? minLevel;
            if (level < minLevel)
                return false;
            if (stored != null && stored.NpcId != fromTemplate.NpcId)
                return false;
        }

        int controlId = collar.ObjectId;
        int enchant = collar.EnchantLevel;
        long exp = stored?.Exp ?? toTemplate.ExpForLevel(minLevel);
        int fed = stored?.Fed ?? toTemplate.At(minLevel).MaxMeal;
        var items = stored?.Items ?? [];
        string name = stored?.Name ?? "";

        player.Inventory!.Remove(collar);
        var replaced = player.Inventory.AddRestored(controlId, toItemId, 1, enchant);
        collar = replaced;

        var row = new StoredPet(controlId, player.ObjectId, toTemplate.NpcId, name,
            Math.Max(minLevel, enchant), exp, stored?.CurHp ?? 0, stored?.CurMp ?? 0, fed, items);
        if (_persist.Enabled)
            _persist.SavePet(row);
        else
            _petMemory[controlId] = row;

        if (player.Session is { } sess && collar != null)
            SaveInventory(sess, player.Inventory!);
        return true;
    }
}