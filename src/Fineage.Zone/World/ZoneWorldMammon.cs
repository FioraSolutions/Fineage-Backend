using Fineage.Protocol;
using Fineage.Zone.Inventory;
using Fineage.Zone.SevenSigns;
using Fineage.Zone.Spawns;

namespace Fineage.Zone.World;

/// <summary>
/// Mammon NPCs tied to Seven Signs, after Mobius <c>SevenSigns.spawnSevenSignsNPC</c> and <c>AutoSpawnHandler</c>:
/// during competition results and seal validation the Merchant of Mammon walks the Necropolises while the winning
/// cabal holds Avarice, and the Blacksmith of Mammon walks the Catacombs while it holds Gnosis. Each appears 30 s
/// after its seal goes live and moves to a random spot of its list every hour; outside that window both are gone
/// (<see cref="MammonActor"/> keeps that schedule; the world places the NPCs it returns). The talk pages gate on the strict rule, the shops are the datapack multisells (Ancient Adena prices,
/// sealed A/S armor and jewel release, weapon special abilities), and the Black Marketeer swaps Ancient Adena 1:1.
/// </summary>
public sealed partial class ZoneWorld
{
    sealed class MammonSlot
    {
        public int ObjectId;
    }

    readonly MammonActor _mammon;
    readonly MammonSlot _mammonMerchant = new();
    readonly MammonSlot _mammonBlacksmith = new();

    internal Random MammonRandom
    {
        get => _mammon.Random;
        set => _mammon.Random = value;
    }

    internal ZoneEntity? MammonMerchantForTests => _mammonMerchant.ObjectId != 0 ? _byId.GetValueOrDefault(_mammonMerchant.ObjectId) : null;
    internal ZoneEntity? MammonBlacksmithForTests => _mammonBlacksmith.ObjectId != 0 ? _byId.GetValueOrDefault(_mammonBlacksmith.ObjectId) : null;

    /// <summary>Test hook: a talkable (non-attackable) template NPC at a point.</summary>
    internal ZoneEntity SpawnFolkForTests(int npcId, int x, int y, int z)
    {
        lock (_gate)
        {
            var template = Fineage.Zone.Npcs.NpcCatalog.LoadDefault().ById[npcId];
            int objectId = AllocateQuestNpcObjectId();
            MaterializeSpawn(SpawnCatalog.FromTemplate(objectId, template, x, y, z, 0, attackable: false), _activeMapId);
            var npc = _byId[objectId];
            npc.HomeSet = false;
            FanoutNewNpc(npc);
            return npc;
        }
    }

    internal void TickMammonForTests()
    {
        lock (_gate)
            AtHookTick(tick => PlaceMammons(_mammon.Step(tick)));
    }

    int PlaceMammons(IReadOnlyList<MammonPlacement> placements)
    {
        foreach (var placement in placements)
        {
            var slot = placement.NpcId == SevenSignsProtocol.MammonMerchantId ? _mammonMerchant : _mammonBlacksmith;
            RemoveMammon(slot);
            if (placement.At is not var (x, y, z))
                continue;
            if (!Fineage.Zone.Npcs.NpcCatalog.LoadDefault().ById.TryGetValue(placement.NpcId, out var template))
                continue;
            int objectId = AllocateQuestNpcObjectId();
            MaterializeSpawn(SpawnCatalog.FromTemplate(objectId, template, x, y, z, 0, attackable: false), _activeMapId);
            if (_byId.TryGetValue(objectId, out var npc))
            {
                npc.HomeSet = false;
                FanoutNewNpc(npc);
                slot.ObjectId = objectId;
                ZoneLog.Info($"[mammon] spawn npc={placement.NpcId} object={objectId} at={x},{y},{z}");
            }
        }

        return placements.Count;
    }

    void RemoveMammon(MammonSlot slot)
    {
        if (slot.ObjectId == 0)
            return;
        if (_byId.TryGetValue(slot.ObjectId, out var npc))
            DespawnNpc(npc);
        slot.ObjectId = 0;
    }

    /// <summary>
    /// Mobius <c>Npc.showChatWindow</c> for 31092 / 31113 / 31126. True when the NPC is a Mammon; <paramref name="html"/>
    /// is null when the strict rule refused the talk (a system line went out instead).
    /// </summary>
    bool MammonFirstPage(ZoneSession session, ZoneEntity player, ZoneEntity npc, out string? html)
    {
        html = null;
        string file;
        switch (npc.NpcId)
        {
            case SevenSignsProtocol.MammonMarketeerId:
                file = "seven_signs/blkmrkt_1.htm";
                break;
            case SevenSignsProtocol.MammonMerchantId:
                if (!MammonAllowed(session, player, gnosis: false))
                    return true;
                file = "seven_signs/mammmerch_1.htm";
                break;
            case SevenSignsProtocol.MammonBlacksmithId:
                if (!MammonAllowed(session, player, gnosis: true))
                    return true;
                file = "seven_signs/mammblack_1.htm";
                break;
            default:
                return false;
        }

        html = _html.TryGet(file, out var page) ? page : NpcHtmlTextMissing(file);
        return true;
    }

    static string NpcHtmlTextMissing(string file) => "<html><body>My Text is missing:<br>" + file + "</body></html>";

    /// <summary>Mobius strict seven signs: the talker's cabal must be the winner and own the seal the NPC serves.</summary>
    bool MammonAllowed(ZoneSession session, ZoneEntity player, bool gnosis)
    {
        if (!_sevenSigns.Engine.Options.Strict)
            return true;
        var effects = _sevenSigns.Engine.Effects();
        var winner = effects.WinningCabal;
        var cabal = (SevenSignsCabal)_sevenSigns.Engine.PlayerCabal(player.ObjectId);
        var owner = gnosis ? effects.GnosisOwner : effects.AvariceOwner;
        switch (winner)
        {
            case SevenSignsCabal.Dawn when cabal != winner || cabal != owner:
                SelfLine(session, SevenSignsProtocol.OnlyDawnMessage, "Only a Lord of Dawn may use this.");
                return false;
            case SevenSignsCabal.Dusk when cabal != winner || cabal != owner:
                SelfLine(session, SevenSignsProtocol.OnlyDuskMessage, "Only a Revolutionary of Dusk may use this.");
                return false;
            case SevenSignsCabal.Dawn or SevenSignsCabal.Dusk:
                return true;
            default:
                SelfLine(session, SevenSignsProtocol.QuestEventPeriodMessage, "This is a quest event period.");
                return false;
        }
    }

    /// <summary>Mobius <c>SevenSigns 7 N</c> at the Black Marketeer: N Ancient Adena become N adena.</summary>
    string? AncientAdenaToAdena(ZoneSession session, ZoneEntity player, ZoneEntity npc, int amount)
    {
        var inventory = player.Inventory!;
        string Page(string name) => _html.TryGet("seven_signs/" + name, out var h) ? h : NpcHtmlTextMissing(name);
        if (amount < 1)
        {
            SendHtml(session, player, npc, Page("blkmrkt_3.htm"));
            return null;
        }
        if (inventory.CountOf(SevenSignsProtocol.AncientAdenaItemId) < amount)
        {
            SendHtml(session, player, npc, Page("blkmrkt_4.htm"));
            return null;
        }

        var changes = new List<InventoryUpdateEntryMsg>();
        Take(inventory, SevenSignsProtocol.AncientAdenaItemId, amount, changes);
        if (changes.Count > 0)
            NetBridge?.FanoutInventoryUpdate([session.ObjectId], new InventoryUpdateMsg { Changes = changes.ToArray() });
        GiveItem(session, inventory, SevenSignsProtocol.AdenaItemId, amount);
        ZoneLog.Info($"[mammon] aa->adena object={player.ObjectId} amount={amount}");
        SendHtml(session, player, npc, Page("blkmrkt_5.htm"));
        return null;
    }
}
