using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Game.Wedding;
using Fineage.Zone.Friends;
using Fineage.Zone.World;

namespace Fineage.Zone.Wedding;

/// <summary>Pure Mobius wedding checks; world holds couple and engage state.</summary>
internal static class WeddingRules
{
    public const int InfidelityDebuffSkillMage = 4362;
    public const int InfidelityDebuffSkillFighter = 4361;

    public static string? EngageError(
        WeddingSettings cfg,
        ZoneEntity self,
        ZoneEntity? target,
        bool targetOnMutualFriends,
        bool targetEngagePending,
        bool selfHasPartner,
        bool targetHasPartner,
        bool targetMarried)
    {
        if (!cfg.AllowWedding)
            return "Wedding system is disabled.";
        if (target == null || target.Session == null || !target.Session.InWorld)
            return "You have no one targeted.";
        if (target.Kind != ZoneEntityKind.Player)
            return "You can only ask another player to engage you.";
        if (target.ObjectId == self.ObjectId)
            return "Is there something wrong with you, are you trying to go out with youself?";
        if (selfHasPartner)
            return "You are already engaged.";
        if (targetMarried)
            return "Player already married.";
        if (targetEngagePending)
            return "Player already asked by someone else.";
        if (targetHasPartner)
            return "Player already engaged with someone else.";
        if (!cfg.AllowSameSex && self.Sex == target.Sex)
            return "Gay marriage is not allowed on this server!";
        if (!targetOnMutualFriends)
            return "The player you want to ask is not on your friends list, you must first be on each others friends list before you choose to engage.";
        return null;
    }

    public static bool IsFormalWear(WeddingSettings cfg, PlayerInventory inventory)
    {
        if (!cfg.RequireFormalWear)
            return true;
        var worn = inventory.FindByItemId(cfg.FormalWearItemId);
        return worn != null && worn.IsEquipped;
    }

    public static long DivorceAdenaTransfer(long payerAdena, int percent) =>
        payerAdena / 100 * percent;

    public static bool MutualFriends(SocialLists selfLists, SocialLists targetLists, int selfId, int targetId) =>
        selfLists.Friends.ContainsKey(targetId) && targetLists.Friends.ContainsKey(selfId);

    public static string? GoToLoveSelfError(
        WeddingSettings cfg,
        ZoneEntity self,
        bool married,
        int partnerId,
        bool inCombat,
        bool cursedWeapon,
        bool jailed,
        bool olympiad,
        bool duel,
        bool observer,
        bool siegeInProgress,
        bool festival,
        bool dimensionalRift,
        bool noSummonZone,
        bool grandBossZone)
    {
        if (!cfg.AllowWedding || !cfg.TeleportEnabled)
            return "The 'Go to Love' teleport feature has been disabled.";
        if (!married)
            return "You're not married.";
        if (partnerId == 0)
            return "Couldn't find your fiance in the Database - Inform a Gamemaster.";
        if (grandBossZone)
            return "You are inside a Boss Zone.";
        if (cursedWeapon)
            return "While you are holding a Cursed Weapon you can't go to your love!";
        if (jailed)
            return "You are in Jail!";
        if (olympiad)
            return "You are in the Olympiad now.";
        if (duel)
            return "You are in a duel!";
        if (observer)
            return "You are in the observation.";
        if (siegeInProgress)
            return "You are in a siege, you cannot go to your partner.";
        if (festival)
            return "You are in a festival.";
        if (dimensionalRift)
            return "You are in the dimensional rift.";
        if (noSummonZone)
            return "You are in area which blocks summoning.";
        if (inCombat)
            return "You are unable to engage in combat. Please go to the nearest restart point.";
        return null;
    }

    public static string? GoToLovePartnerError(
        ZoneEntity partner,
        bool cursedWeapon,
        bool jailed,
        bool olympiad,
        bool duel,
        bool observer,
        bool siegeInProgress,
        bool festival,
        bool dimensionalRift,
        bool noSummonZone,
        bool grandBossZone,
        bool offline)
    {
        if (offline)
            return "Your partner is not online.";
        if (jailed)
            return "Your partner is in Jail.";
        if (cursedWeapon)
            return "Your partner is holding a Cursed Weapon and you can't go to your love!";
        if (grandBossZone)
            return "Your partner is inside a Boss Zone.";
        if (olympiad)
            return "Your partner is in the Olympiad now.";
        if (duel)
            return "Your partner is in a duel.";
        if (festival)
            return "Your partner is in a festival.";
        if (dimensionalRift)
            return "Your partner is in dimensional rift.";
        if (observer)
            return "Your partner is in the observation.";
        if (siegeInProgress)
            return "Your partner is in a siege, you cannot go to your partner.";
        if (noSummonZone)
            return "Your partner is in area which blocks summoning.";
        return null;
    }

    public static bool CanPayTeleport(PlayerInventory inv, long price) =>
        inv.CountOf(ShopTrade.AdenaItemId) >= price;

    public static bool CanPayCeremony(PlayerInventory a, PlayerInventory b, long price) =>
        a.CountOf(ShopTrade.AdenaItemId) >= price && b.CountOf(ShopTrade.AdenaItemId) >= price;
}