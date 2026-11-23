using Fineage.Game.Recipes;
using Fineage.Game.Shops;
using Fineage.Login;
using Fineage.Protocol;

namespace Fineage.Zone.World;

/// <summary>
/// Optional enter-world kit for <c>bots-economy-hammer</c> when <c>FINEAGE_LOADTEST_ECONOMY_HAMMER=1</c>.
/// Manufacturers are dwarves with Create Dwarven + recipe 1; customers get wooden-arrow ingredients.
/// </summary>
public sealed partial class ZoneWorld
{
    const int EconomyHammerRecipeId = 1;
    const int Stem = 1864;
    const int IronOre = 1869;

    public static bool LoadTestEconomyHammerEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("FINEAGE_LOADTEST_ECONOMY_HAMMER"), "1",
            StringComparison.Ordinal);

    void TryProvisionLoadTestEconomyHammer(ZoneSession session, ZoneEntity player)
    {
        if (!LoadTestEconomyHammerEnabled)
            return;
        string? prefix = LoginLoadTestSettings.AccountPrefix;
        if (prefix == null || player.Session?.Account is not { } account ||
            !account.StartsWith(prefix, StringComparison.Ordinal))
            return;
        if (!TryParseLoadBotId(player.Name, out int botId))
            return;

        int mod = Math.Abs(botId) % 6;
        if (mod == 0)
        {
            if (player.Race != PrivateStoreProtocol.DwarfRace)
                throw new InvalidOperationException(
                    $"load-test economy hammer: bot {botId} must be dwarf race to manufacture (race={player.Race})");
            if (player.ClassId != 53)
                throw new InvalidOperationException(
                    $"load-test economy hammer: bot {botId} manufacturer must be class 53 (class={player.ClassId})");

            player.LearnedSkills[RecipeCatalog.CreateDwarvenSkillId] = 1;
            RefreshKnownSkillsForTest(player);
            if (!player.KnownRecipes.Contains(EconomyHammerRecipeId))
                player.KnownRecipes.Add(EconomyHammerRecipeId);
            PersistKnownRecipes(session, player);
            player.CurMp = Math.Max(player.CurMp, 50);
            ZoneLog.Info($"[loadtest-economy] provisioned manufacturer botId={botId} obj={player.ObjectId}");
        }
        else if (mod == 1)
        {
            var err = TryGiveItem(session, Stem, 8);
            if (err != null)
                throw new InvalidOperationException($"load-test economy hammer: customer bot {botId} stem grant: {err}");
            err = TryGiveItem(session, IronOre, 4);
            if (err != null)
                throw new InvalidOperationException($"load-test economy hammer: customer bot {botId} ore grant: {err}");
            err = TryGiveItem(session, ShopTrade.AdenaItemId, 500);
            if (err != null)
                throw new InvalidOperationException($"load-test economy hammer: customer bot {botId} adena grant: {err}");
            ZoneLog.Info($"[loadtest-economy] provisioned manufacture customer botId={botId} obj={player.ObjectId}");
        }
    }

    static bool TryParseLoadBotId(string name, out int botId)
    {
        botId = 0;
        if (!name.StartsWith("Bot", StringComparison.Ordinal) || name.Length < 4)
            return false;
        return int.TryParse(name.AsSpan(3), out botId);
    }
}