using System.Diagnostics.Metrics;
using Fineage.Net.Contract;
using Fineage.Net.Host;
using Fineage.Protocol.Telemetry;
using Xunit;

namespace Fineage.Net.Tests;

/// <summary>
/// #970 / #951: the per-connection mailbox is bounded in bytes. Above the soft limit visuals are shed and movement
/// keeps the newest frame per entity; reliable frames are never dropped or replaced; above the hard limit the
/// admission is rejected so the connection closes that slow client.
/// </summary>
public sealed class OutboundMailboxBudgetTests
{
    const int Soft = 4 * 1024;
    const int Hard = 16 * 1024;

    static OutboundMailbox Mailbox(string endpoint) =>
        new(maxFrames: 4096, maxBytes: Soft, connectionId: 77, endpoint: endpoint, hardBytes: Hard);

    [Fact]
    public void Visuals_are_shed_oldest_first_before_a_reliable_frame_is_refused()
    {
        var mailbox = Mailbox("budget-shed");
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Frame(NetOpcode.SystemMessage, 100)));
        for (int i = 0; i < 30; i++)
            Assert.NotEqual(MailboxAdmit.Rejected, mailbox.TryAppend(Frame(NetOpcode.AttackState, 500, tag: (byte)i)));

        // Above soft, a reliable frame still goes in (it is under hard) and visuals give way for it.
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Frame(NetOpcode.InventoryUpdate, 3000)));
        var drained = new List<PacketEnvelope>();
        mailbox.Take(drained);

        Assert.Equal(NetOpcode.SystemMessage, drained[0].Opcode);
        Assert.Contains(drained, f => f.Opcode == NetOpcode.InventoryUpdate);
        int visuals = drained.Count(f => f.Opcode == NetOpcode.AttackState);
        Assert.True(visuals < 30, $"expected visuals to be shed above the soft limit, kept {visuals}");
        // Shed oldest first: the visuals that survive are the newest ones.
        var tags = drained.Where(f => f.Opcode == NetOpcode.AttackState).Select(f => (int)f.Payload[0]).ToList();
        Assert.Equal(tags.OrderBy(t => t).ToList(), tags);
        if (tags.Count > 0)
            Assert.Equal(29, tags[^1]);
    }

    [Fact]
    public void Reliable_frames_between_soft_and_hard_are_kept_and_past_hard_are_rejected()
    {
        var mailbox = Mailbox("budget-hard");
        int accepted = 0;
        MailboxAdmit last = MailboxAdmit.Accepted;
        for (int i = 0; i < 40 && last == MailboxAdmit.Accepted; i++)
        {
            last = mailbox.TryAppend(Frame(NetOpcode.CreatureSay, 986));
            if (last == MailboxAdmit.Accepted)
                accepted++;
        }

        Assert.Equal(MailboxAdmit.Rejected, last);
        Assert.Equal(Hard / 986, accepted);
        Assert.True(mailbox.QueuedBytes <= Hard);
    }

    [Fact]
    public void Chat_is_never_dropped_as_a_visual()
    {
        var mailbox = Mailbox("budget-chat");
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Frame(NetOpcode.SystemMessage, Soft)));
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Frame(NetOpcode.CreatureSay, 200)));
        Assert.Equal(MailboxAdmit.Dropped, mailbox.TryAppend(Frame(NetOpcode.SocialAction, 200)));
    }

    [Fact]
    public void Knownlist_snapshots_are_never_replaced()
    {
        var mailbox = Mailbox("budget-snapshot");
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryMergeSnapshot([Frame(NetOpcode.EntitySpawn, 3000, tag: 1)]));
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryMergeSnapshot([Frame(NetOpcode.EntitySpawn, 3000, tag: 2)]));
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryMergeSnapshot([Frame(NetOpcode.EntitySpawn, 3000, tag: 3)]));
        var drained = new List<PacketEnvelope>();
        mailbox.Take(drained);
        Assert.Equal(new[] { 1, 2, 3 }, drained.Select(f => (int)f.Payload[0]).ToArray());

        var full = Mailbox("budget-snapshot-hard");
        Assert.Equal(MailboxAdmit.Accepted, full.TryAppend(Frame(NetOpcode.SystemMessage, 100)));
        Assert.Equal(MailboxAdmit.Rejected, full.TryMergeSnapshot([Frame(NetOpcode.EntitySpawn, Hard)]));
    }

    [Fact]
    public void Status_updates_coalesce_by_merging_attributes()
    {
        var mailbox = Mailbox("budget-status");
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Status(42, (9, 100), (11, 50))));
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Status(42, (9, 80))));
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Status(43, (9, 10))));

        var drained = new List<PacketEnvelope>();
        mailbox.Take(drained);
        Assert.Equal(2, drained.Count);
        StatusUpdateNetMsg merged = NetCodec.DecodeStatusUpdate(drained.Single(f => NetCodec.DecodeStatusUpdate(f.Payload).EntityValue == 42).Payload);
        var byId = merged.AttributeIds.Zip(merged.Values).ToDictionary(p => p.First, p => p.Second);
        Assert.Equal(80, byId[9]);
        Assert.Equal(50, byId[11]);
    }

    [Fact]
    public void Newest_movement_moves_after_frames_coalesced_since_the_superseded_one()
    {
        var mailbox = Mailbox("budget-move-order");
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Move(1, destX: 10)));
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Correction(1, x: 20)));
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Move(1, destX: 30)));

        var drained = new List<PacketEnvelope>();
        mailbox.Take(drained);
        Assert.Equal(new[] { NetOpcode.PositionCorrection, NetOpcode.MoveState }, drained.Select(f => f.Opcode).ToArray());
        Assert.Equal(30, NetCodec.DecodeMoveState(drained[1].Payload).DestX);
    }

    [Fact]
    public void Empty_abnormal_status_update_coalesces_by_object_id()
    {
        var mailbox = Mailbox("budget-abnormal");
        byte[] cleared = NetCodec.EncodeAbnormalStatusUpdate(new AbnormalStatusUpdateNetMsg { ObjectId = 5, Effects = [] });
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(new PacketEnvelope { Opcode = NetOpcode.AbnormalStatusUpdate, Payload = cleared }));
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(new PacketEnvelope { Opcode = NetOpcode.AbnormalStatusUpdate, Payload = cleared }));
        var drained = new List<PacketEnvelope>();
        Assert.Equal(1, mailbox.Take(drained).Frames);
    }

    [Fact]
    public void Movement_never_drops_the_newest_frame_per_entity_above_the_soft_limit()
    {
        var mailbox = Mailbox("budget-move");
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Frame(NetOpcode.SystemMessage, Soft)));
        for (int e = 1; e <= 20; e++)
            Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Move(e, destX: e)));
        var drained = new List<PacketEnvelope>();
        mailbox.Take(drained);
        Assert.Equal(20, drained.Count(f => f.Opcode == NetOpcode.MoveState));
    }

    [Fact]
    public void Shed_bytes_are_counted_by_lane()
    {
        long visual = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == FineageTelemetry.MeterName && instrument.Name == "fineage.outbound.shed_bytes")
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "lane" && (string?)tag.Value == "visual")
                    Interlocked.Add(ref visual, value);
            }
        });
        listener.Start();

        var mailbox = Mailbox("budget-metric");
        Assert.Equal(MailboxAdmit.Accepted, mailbox.TryAppend(Frame(NetOpcode.SystemMessage, Soft)));
        Assert.Equal(MailboxAdmit.Dropped, mailbox.TryAppend(Frame(NetOpcode.MagicSkillUse, 300)));
        Assert.True(Interlocked.Read(ref visual) >= 300);
    }

    static PacketEnvelope Frame(ushort opcode, int totalBytes, byte tag = 0)
    {
        var payload = new byte[Math.Max(1, totalBytes - PacketEnvelope.HeaderSize)];
        payload[0] = tag;
        return new PacketEnvelope { Opcode = opcode, Payload = payload };
    }

    static PacketEnvelope Status(ulong entity, params (int Id, int Value)[] attributes) => new()
    {
        Opcode = NetOpcode.StatusUpdate,
        Payload = NetCodec.EncodeStatusUpdate(new StatusUpdateNetMsg
        {
            EntityValue = entity,
            EntityGeneration = 1,
            AttributeIds = attributes.Select(a => a.Id).ToArray(),
            Values = attributes.Select(a => a.Value).ToArray()
        })
    };

    static PacketEnvelope Move(int entity, int destX) => new()
    {
        Opcode = NetOpcode.MoveState,
        Payload = NetCodec.EncodeMoveState(new MoveStateMsg { EntityValue = (ulong)entity, EntityGeneration = 1, DestX = destX })
    };

    static PacketEnvelope Correction(int entity, int x) => new()
    {
        Opcode = NetOpcode.PositionCorrection,
        Payload = NetCodec.EncodePositionCorrection(new PositionCorrectionMsg { EntityValue = (ulong)entity, EntityGeneration = 1, X = x })
    };
}
