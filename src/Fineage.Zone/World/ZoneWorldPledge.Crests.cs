using Fineage.Accounts;
using Fineage.Net.Contract;
using Fineage.Protocol;
using Fineage.Zone.Clan;

namespace Fineage.Zone.World;

/// <summary>
/// Clan crests after Mobius <c>RequestSetPledgeCrest</c> (16x12, ≤256 bytes), <c>RequestExSetPledgeCrestLarge</c>
/// (≤2176 bytes), <c>RequestPledgeCrest</c> / <c>RequestExPledgeCrestLarge</c> and <c>CrestTable</c>: CHANGE_CREST
/// privilege, clan level 3+, refused during the dissolution grace period, empty data deletes. Blobs persist through
/// <see cref="IAccountStore.InsertCrest"/>; a store-less world keeps them in memory.
/// </summary>
public sealed partial class ZoneWorld
{
    readonly Dictionary<int, CrestSave> _localCrests = [];
    int _nextLocalCrestId = 1;

    CrestSave? FindCrest(int crestId)
    {
        if (crestId <= 0)
            return null;
        if (_accounts != null)
            return _accounts.FindCrest(crestId);
        return _localCrests.TryGetValue(crestId, out var crest) ? crest : null;
    }

    CrestSave? StoreCrest(int kind, byte[] data)
    {
        if (_accounts != null)
            return _accounts.InsertCrest(kind, data);
        var crest = new CrestSave(_nextLocalCrestId++, kind, data.ToArray());
        _localCrests[crest.Id] = crest;
        return crest;
    }

    void DropCrest(int crestId)
    {
        if (crestId <= 0)
            return;
        if (_accounts != null)
            _accounts.DeleteCrest(crestId);
        else
            _localCrests.Remove(crestId);
    }

    static int MaxCrestBytes(int kind) => kind switch
    {
        PledgeProtocol.CrestKindLarge => PledgeProtocol.MaxLargeCrestBytes,
        PledgeProtocol.CrestKindAlly => PledgeProtocol.MaxAllyCrestBytes,
        _ => PledgeProtocol.MaxPledgeCrestBytes
    };

    /// <summary>Mobius PledgeCrest / ExPledgeCrestLarge / AllyCrest: empty data when the id is unknown.</summary>
    string? SendCrest(ZoneSession session, int crestId, int kind)
    {
        var crest = FindCrest(crestId);
        NetBridge?.FanoutCrestData([session.ObjectId], new CrestDataNetMsg
        {
            CrestId = crestId,
            Kind = crest?.Kind ?? kind,
            Data = crest?.Data ?? []
        });
        return null;
    }

    string? SetClanCrest(ZoneSession session, ZoneClan clan, byte[]? data, bool large, long now)
    {
        data ??= [];
        int kind = large ? PledgeProtocol.CrestKindLarge : PledgeProtocol.CrestKindPledge;
        if (data.Length > MaxCrestBytes(kind))
        {
            if (large)
                SelfLine(session, 0, "The size of the uploaded crest or insignia does not meet the standard requirements.");
            else
                SelfLine(session, 209, "The size of the image file is inappropriate. Please adjust to 16*12.");
            return PledgeProtocol.CrestSize;
        }
        if (clan.DissolvingExpiryUnixMs > now)
        {
            SelfLine(session, 552, "During the grace period for dissolving a clan, the registration or deletion of a clan's crest is not allowed.");
            return PledgeProtocol.Dissolving;
        }
        if (!HasPriv(clan, session.ObjectId, PledgeProtocol.PrivChangeCrest))
            return NotAuthorized(session);

        int oldId = large ? clan.D.LargeCrestId : clan.CrestId;
        if (data.Length == 0)
        {
            if (oldId == 0)
                return null;
            if (!ApplyClanCrest(clan, large, 0))
                return ClanProtocol.Store;
            DropCrest(oldId);
            SelfLine(session, 1861, "The clan's crest has been deleted.");
            return null;
        }

        if (clan.Level < PledgeProtocol.CrestMinClanLevel)
        {
            SelfLine(session, 272, "A clan crest can only be registered when the clan's skill level is 3 or above.");
            return PledgeProtocol.Conditions;
        }
        var crest = StoreCrest(kind, data);
        if (crest == null)
            return ClanProtocol.Store;
        if (!ApplyClanCrest(clan, large, crest.Id))
        {
            DropCrest(crest.Id);
            return ClanProtocol.Store;
        }
        DropCrest(oldId);
        if (large)
            SelfLine(session, 1663, "The clan crest was successfully registered. Remember, only a clan that owns a clan hall or castle can have their crest displayed.");
        else
            SelfLine(session, 0, "The crest was successfully registered.");
        return null;
    }

    /// <summary>Mobius <c>changeClanCrest</c> / <c>changeLargeCrest</c>: persist, then every member's spawn row changes.</summary>
    bool ApplyClanCrest(ZoneClan clan, bool large, int crestId)
    {
        int before = large ? clan.D.LargeCrestId : clan.CrestId;
        if (large)
            clan.D.LargeCrestId = crestId;
        else
            clan.CrestId = crestId;
        if (!SaveClan(clan))
        {
            if (large)
                clan.D.LargeCrestId = before;
            else
                clan.CrestId = before;
            return false;
        }
        BroadcastClan(clan, charInfo: true);
        return true;
    }

    /// <summary>Test hook: the stored crest bytes (null when absent).</summary>
    internal byte[]? CrestBytesForTest(int crestId)
    {
        using (_affinity.Step())
            return FindCrest(crestId)?.Data;
    }
}
