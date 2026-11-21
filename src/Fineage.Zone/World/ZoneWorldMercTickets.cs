using Fineage.Accounts;
using Fineage.Game;
using Fineage.Game.Shops;
using Fineage.Protocol;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Siege;

namespace Fineage.Zone.World;

/// <summary>Mobius <c>MercTicket</c> item handler and <c>MercTicketManager</c> ticket posts.</summary>
public sealed partial class ZoneWorld
{
    static readonly MercTicketCatalog MercTickets = MercTicketCatalog.Default;

    public const int SysMsgMercWrongPlace = 655;
    public const int SysMsgMercCannotPost = 656;
    public const int SysMsgMercTooClose = 657;
    public const int SysMsgMercNoAuthority = 653;
    public const int SysMsgSealStrifeNoSummon = 1925;

    public const int SummonSiegeGolemSkillId = 13;

    string? TryUseMercTicket(ZoneSession session, ZoneEntity player, ItemInstance item)
    {
        if (!MercTickets.IsMercTicket(item.ItemId))
            return null;
        int castleId = CastleAtEntity(player);
        if (castleId == 0)
            castleId = SiegeAreas.CastleAt(player.X, player.Y, player.Z);
        if (MercTickets.TicketCastleId(item.ItemId) != castleId)
        {
            SelfLine(session, SysMsgMercWrongPlace, "Mercenaries cannot be positioned here.");
            return null;
        }

        var castle = castleId > 0 ? _siege.Board.Find(castleId) : null;
        if (castle == null || castle.Id == 0)
        {
            SelfLine(session, SysMsgMercWrongPlace, "Mercenaries cannot be positioned here.");
            return null;
        }

        if (player.ClanId <= 0 || !_clan.TryGet(player.ClanId, out var clan)
            || castle.OwnerClanId != clan.Id || clan.LeaderId != player.ObjectId)
        {
            SelfLine(session, SysMsgMercNoAuthority, "You do not have the authority to position mercenaries.");
            return null;
        }

        if (castle.InProgress)
        {
            SelfLine(session, SysMsgMercCannotPost, "This mercenary cannot be positioned anymore.");
            return null;
        }

        var effects = _sevenSigns.Engine.Effects();
        if (!effects.SealValidation)
        {
            SelfLine(session, SysMsgMercCannotPost, "This mercenary cannot be positioned anymore.");
            return null;
        }

        if (!MercTicketStrifeAllows(item.ItemId, effects))
        {
            SelfLine(session, SysMsgMercCannotPost, "This mercenary cannot be positioned anymore.");
            return null;
        }

        var row = MercTickets.Find(item.ItemId);
        if (row == null)
        {
            SelfLine(session, SysMsgMercWrongPlace, "Mercenaries cannot be positioned here.");
            return null;
        }
        if (TooCloseToMercPost(castle, player.X, player.Y, player.Z))
        {
            SelfLine(session, SysMsgMercTooClose,
                "Positioning cannot be done here because the distance between mercenaries is too short.");
            return null;
        }

        if (AtCastleMercLimit(castle) || AtTypeMercLimit(castle, row.Value))
        {
            SelfLine(session, SysMsgMercCannotPost, "This mercenary cannot be positioned anymore.");
            return null;
        }

        if (!RecordConsumeOne(player, item, item.ItemId, out bool removedEntirely))
            return InventoryProtocol.Blocked;
        NetBridge?.FanoutInventoryUpdate([session.ObjectId], InventoryMessages.Update(
            removedEntirely ? InventoryChange.Remove : InventoryChange.Modify, item));
        SaveInventory(session, player.Inventory!);

        var post = new HiredSiegeGuard
        {
            ItemId = row.Value.ItemId,
            NpcId = row.Value.NpcId,
            X = player.X,
            Y = player.Y,
            Z = player.Z,
            Heading = player.Heading
        };
        var ground = SpawnGroundStack(row.Value.ItemId, 1, post.X, post.Y, post.Z, player.InstanceId,
            [player], expireAtMs: 0);
        if (ground != null)
            post.GroundObjectId = ground.ObjectId;
        castle.HiredGuards.Add(post);
        PersistSiege();
        return null;
    }

    static bool MercTicketStrifeAllows(int itemId, SevenSignsEffects effects)
    {
        return effects.StrifeOwner switch
        {
            SevenSignsCabal.None when MercTickets.IsDawnPostingTicket(itemId) => false,
            SevenSignsCabal.Dusk when !MercTickets.IsRookiePostingTicket(itemId) => false,
            _ => true
        };
    }

    bool AtCastleMercLimit(CastleSiege castle)
    {
        int limit = MercTickets.MaxPerCastle(castle.Id);
        return limit > 0 && castle.HiredGuards.Count >= limit;
    }

    bool AtTypeMercLimit(CastleSiege castle, MercTicketRow row)
    {
        int count = castle.HiredGuards.Count(g => g.ItemId == row.ItemId);
        return count >= row.MaxPerType;
    }

    bool TooCloseToMercPost(CastleSiege castle, int x, int y, int z)
    {
        const int minSq = 25 * 25;
        foreach (var post in castle.HiredGuards)
        {
            long dx = x - post.X, dy = y - post.Y, dz = z - post.Z;
            if (dx * dx + dy * dy + dz * dz < minSq)
                return true;
        }

        return false;
    }

    void ClearMercTicketGroundItems(int castleId)
    {
        var castle = _siege.Board.Find(castleId);
        if (castle == null)
            return;
        foreach (var post in castle.HiredGuards)
        {
            if (post.GroundObjectId > 0 && _byId.TryGetValue(post.GroundObjectId, out var ground)
                && ground.Kind == ZoneEntityKind.GroundItem)
                RemoveGroundItem(ground);
            post.GroundObjectId = 0;
        }
    }

    void SpawnHiredSiegeGuards(CastleSiege castle, SiegeRuntime run, CastleDataRow data, long now)
    {
        int limit = MercTickets.MaxPerCastle(castle.Id);
        if (limit <= 0)
            limit = data.MaxMercenaries;
        if (limit <= 0)
            return;
        int spawned = 0;
        foreach (var post in castle.HiredGuards)
        {
            if (++spawned > limit)
                break;
            int respawn = MercTickets.Find(post.ItemId)?.RespawnSeconds ?? 0;
            var placement = new CastlePlacement(post.NpcId, post.X, post.Y, post.Z, post.Heading, respawn);
            SpawnSiegeGuard(run, data, placement, ClosestTower(data, placement));
        }
    }

    void OnMercTicketGroundPickup(ZoneEntity player, ZoneEntity ground)
    {
        if (!MercTickets.IsMercTicket(ground.GroundItemId))
            return;
        int castleId = MercTickets.TicketCastleId(ground.GroundItemId);
        var castle = _siege.Board.Find(castleId);
        if (castle == null)
            return;
        var post = castle.HiredGuards.FirstOrDefault(g => g.GroundObjectId == ground.ObjectId);
        if (post == null)
            return;
        castle.HiredGuards.Remove(post);
        PersistSiege();
    }

    bool CanSummonSiegeGolem(ZoneSession session, ZoneEntity player)
    {
        if (player.Dead || HasCursedWeapon(player.Inventory) || player.ClanId <= 0)
        {
            IncorrectTarget(session);
            return false;
        }

        int castleId = CastleAtEntity(player);
        if (castleId == 0)
            castleId = SiegeAreas.CastleAt(player.X, player.Y, player.Z);
        var castle = castleId > 0 ? _siege.Board.Find(castleId) : null;
        if (castle == null || castle.Id == 0)
        {
            IncorrectTarget(session);
            return false;
        }

        if (!castle.InProgress)
        {
            IncorrectTarget(session);
            return false;
        }

        int clanId = SiegeClanOf(player);
        if (clanId == 0 || SiegeSide(clanId, castle.Id) != SiegeProtocol.StateAttacker)
        {
            IncorrectTarget(session);
            return false;
        }

        var cabal = (SevenSignsCabal)_sevenSigns.Engine.PlayerCabal(player.ObjectId);
        if (_sevenSigns.Engine.Effects().BlocksSummon(cabal))
        {
            SelfLine(session, SysMsgSealStrifeNoSummon,
                "Due to the affects of the Seal of Strife, it is not possible to summon at this time.");
            return false;
        }

        return true;
    }

    internal bool MercTicketIsSiegeGuardItem(int itemId) =>
        MercTickets.IsMercTicket(itemId)
        || (itemId >= ShopTrade.SiegeGuardFirst && itemId <= ShopTrade.SiegeGuardLast);
}