using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Testing;

namespace Dfo.Server.Protocol.Tests;

public sealed class MarriageProposalCodecTests
{
    [Fact]
    public void Partner_profile_places_level_before_job_to_create_the_native_knight_model()
    {
        var body = MarriageEngagementPayload.Partner(new(1, "000"u8.ToArray(), 12, 85, 0, new int[13]));
        Assert.Equal(85, body[43]); Assert.Equal(12, body[44]); Assert.Equal(0, body[45]);
    }
    [Fact]
    public void Marriage_date_and_love_points_match_native_offsets_and_coupon_uses_main_slot()
    {
        var at = DateTimeOffset.FromUnixTimeSeconds(1792206195);
        var snapshot = new Dfo.Server.Data.Marriage.MarriageEngagementSnapshot(Guid.NewGuid(), new(8, "peer"u8.ToArray(), 0, 70, 0, new int[13])) { Stage = MarriageStage.Married, MarriedAt = at, LovePoints = 17 };
        var info = MarriageEngagementPayload.Info(snapshot);
        Assert.Equal(15, info.Length); Assert.Equal(4, info[4]);
        Assert.Equal(1792206195u, BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(6)));
        Assert.Equal(17u, BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(10)));
        var request = Assert.IsType<WeddingLovePointRequest>(new WeddingLovePointDecoder().Decode(GameMessageMetadata.Empty, Convert.FromHexString("8CF61A00FE70E902FFFFFFFF9CF351000000")));
        Assert.Equal(81u, request.Slot);
        foreach (var size in new[] { 0, 4, 17, 19, 32 }) Assert.Throws<InvalidDataException>(() => new WeddingLovePointDecoder().Decode(GameMessageMetadata.Empty, new byte[size]));
        Assert.Equal(Convert.FromHexString("01FEFFFFFF"), Assert.Single(new WeddingLovePointAckEncoder().Encode(new WeddingLovePointAck(-2)))[15..]);
        Assert.Equal(Convert.FromHexString("0800000011000000"), Assert.Single(new WeddingLovePointNotificationEncoder().Encode(new WeddingLovePointNotification(8, 17)))[15..]);
        Assert.IsAssignableFrom<Dfo.Server.Application.Commands.IRequestSettlementCommand>(new WeddingLovePointCommand(SelectedSessionIdentity.Create(new(1), new(8), new("test"), 1, 10011), new(Dfo.Server.Data.Inventory.BusinessInventorySpace.Consumable, 16), [new(1), new(2)]));
    }

    [Fact]
    public void Reservation_uses_action_two_without_shifting_tier_or_response_and_projects_stage_two()
    {
        Assert.Equal(Convert.FromHexString("0000000048000000020100"), WeddingProposalPayload.Fields(0, 72, 2, tier: 1));
        Assert.Equal(Convert.FromHexString("010000000048000000020000"), WeddingProposalPayload.Cmd(0, 72, 2));
        Assert.Equal(Convert.FromHexString("01000000004800000002000001"), WeddingProposalPayload.Cmd(0, 72, 2, 1));
        Assert.Equal(Convert.FromHexString("0000000048000000020100"),
            Assert.Single(new WeddingProposalNotificationEncoder().Encode(new WeddingProposalNotification(72, 2, 1)))[15..]);
        Assert.Equal(Convert.FromHexString("FDFFFFFF4800000002000000"),
            Assert.Single(new WeddingResponseNotificationEncoder().Encode(new WeddingResponseNotification(72, 0, 2, -3)))[15..]);
        var profile = new MarriagePartnerProfile(72, "partner"u8.ToArray(), 0, 85, 0, new int[13]);
        Assert.Equal(Convert.FromHexString("480000000200000000000000000000"),
            MarriageEngagementPayload.Info(new(Guid.NewGuid(), profile) { Stage = MarriageStage.Preparing }));
    }
    [Theory]
    [InlineData(null, 0)]
    [InlineData(MarriageStage.Proposed, 0)]
    [InlineData(MarriageStage.Engaged, 1)]
    [InlineData(MarriageStage.Requested, 1)]
    [InlineData(MarriageStage.Preparing, 2)]
    [InlineData(MarriageStage.Married, 4)]
    [InlineData(MarriageStage.Closed, 0)]
    public void Wire_state_projects_each_stage(MarriageStage? stage, byte expected) =>
        Assert.Equal(expected, MarriageEngagementPayload.WireState(stage));
    [Fact]
    public void Engagement_snapshot_changes_the_native_menu_stage_and_empty_snapshot_clears_it()
    {
        var profile = new MarriagePartnerProfile(72, "partner"u8.ToArray(), 0, 85, 0, Enumerable.Range(100, 13).ToArray());
        var bytes = MarriageEngagementPayload.Info(new(Guid.NewGuid(), profile));
        Assert.Equal(Convert.FromHexString("480000000100000000000000000000"), bytes);
        Assert.Equal(new byte[15], MarriageEngagementPayload.Info(MarriageEngagementSnapshot.Empty));
    }
    [Fact]
    public void Partner_query_keeps_the_native_prefix_and_response_matches_97_byte_reader()
    {
        var body = NativeBody("48000000");
        Assert.Equal(72u, ((WeddingPartnerQuery)new WeddingPartnerDecoder().Decode(GameMessageMetadata.Empty, body)).Target);
        var profile = new MarriagePartnerProfile(72, "partner"u8.ToArray(), 12, 85, 0x11, Enumerable.Range(100, 13).ToArray());
        var response = MarriageEngagementPayload.Partner(profile);
        Assert.Equal(98, response.Length); Assert.Equal(1, response[0]);
        Assert.Equal(72u, BinaryPrimitives.ReadUInt32LittleEndian(response.AsSpan(5)));
        Assert.Equal("partner"u8.ToArray(), response[13..20]);
        Assert.Equal(85, response[43]); Assert.Equal(12, response[44]); Assert.Equal(0x11, response[45]);
        Assert.Equal(112, BinaryPrimitives.ReadInt32LittleEndian(response.AsSpan(94)));
        Assert.Equal(new byte[] { 0, 1 }, MarriageEngagementPayload.Partner(null));
    }
    [Theory]
    [InlineData(4)] [InlineData(17)] [InlineData(19)] [InlineData(32)]
    public void Partner_query_rejects_compact_truncated_extra_and_full_frames(int length) =>
        Assert.Throws<InvalidDataException>(() => new WeddingPartnerDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));
    [Fact]
    public void Missing_partner_appearance_parts_use_native_minus_one_sentinel_including_weapon_fallbacks()
    {
        // Native RVA013A52D0 chooses appearance entry11, falling back to entry9.
        // RVA013973A0 skips asset lookup only for -1; 0 performs a null asset lookup.
        var equipment = new int[13]; equipment[0] = 100; equipment[3] = 200;
        var body = MarriageEngagementPayload.Partner(new(8, "peer"u8.ToArray(), 0, 70, 3, equipment));
        for (var slot = 0; slot < 13; slot++)
            Assert.Equal(slot == 0 ? 100 : slot == 3 ? 200 : -1,
                BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(46 + slot * 4)));
        Assert.Equal(98, body.Length);
    }
    [Fact]
    public void Partner_name_is_zero_terminated_without_splitting_utf8()
    {
        var name = System.Text.Encoding.UTF8.GetBytes(new string('婚', 12));
        var body = MarriageEngagementPayload.Partner(new(72, name, 0, 85, 0, new int[13]));
        var field = body[13..43];
        Assert.Equal(0, field[^1]);
        Assert.Equal(new string('婚', 9), new System.Text.UTF8Encoding(false, true).GetString(field.AsSpan(0, Array.IndexOf(field, (byte)0))));
    }
    [Fact]
    public void Native_raw_struct_bodies_keep_fourteen_opaque_bytes_before_the_fields()
    {
        // WriteHeader emits a separate 14B frame header; WriteRaw copies the complete struct.
        // Non-zero opaque data must neither be interpreted as another header nor as the target.
        var proposal = Assert.IsType<WeddingProposalRequest>(new WeddingProposalDecoder()
            .Decode(GameMessageMetadata.Empty, NativeBody("47000000010000")));
        Assert.Equal(71u, proposal.Target);
        Assert.Equal(1, proposal.Action);
        var response = Assert.IsType<WeddingResponseRequest>(new WeddingResponseDecoder()
            .Decode(GameMessageMetadata.Empty, NativeBody("0000000001000001")));
        Assert.Equal(0u, response.Target);
        Assert.Equal(1, response.Outcome);
    }
    [Theory]
    [InlineData(0)] [InlineData(7)] [InlineData(20)] [InlineData(22)] [InlineData(35)]
    public void Proposal_rejects_compact_truncated_extra_and_full_frame_data(int length) =>
        Assert.Throws<InvalidDataException>(() => new WeddingProposalDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));
    [Theory]
    [InlineData(0)] [InlineData(8)] [InlineData(21)] [InlineData(23)] [InlineData(36)]
    public void Response_rejects_compact_truncated_extra_and_full_frame_data(int length) =>
        Assert.Throws<InvalidDataException>(() => new WeddingResponseDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));
    private static byte[] NativeBody(string fields) => [.. Enumerable.Repeat((byte)0xA5, 14), .. Convert.FromHexString(fields)];
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Cmd_envelope_does_not_shift_the_status_id_or_outcome(byte response)
    {
        var ack = WeddingProposalPayload.Cmd(0, 71, 1, response);
        Assert.Equal(13, ack.Length);
        Assert.Equal(1, ack[0]);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(ack.AsSpan(1)));
        Assert.Equal(71u, BinaryPrimitives.ReadUInt32LittleEndian(ack.AsSpan(5)));
        Assert.Equal(1, ack[9]);
        Assert.Equal(response, ack[12]);
        Assert.Equal(ack[1..], WeddingProposalPayload.Fields(0, 71, 1, response));
    }
    [Fact]
    public void Business_refusal_keeps_the_typed_status_payload_after_ok_envelope()
    {
        var body = WeddingProposalPayload.Cmd(-151, 72, 1);
        Assert.Equal(Convert.FromHexString("0169FFFFFF48000000010000"), body);
    }
    [Fact]
    public async Task Two_party_delivery_is_ordered_through_peer_notification_and_command_can_borrow_fences()
    {
        var gate = new SelectedSessionTransitionGate();
        var a = SelectedSessionIdentity.Create(new(1), new(71), new("first"), 1, 10011);
        var b = SelectedSessionIdentity.Create(new(2), new(72), new("second"), 1, 10011);
        var order = new List<string>();
        // Each party's proposal is one CMD; the second runs after the first has sent both of its frames.
        var first = TestGameplay.RunAsync(async () => await WeddingProposalExecution.RunAsync(gate, a, b, async () =>
        {
            await using var own = await gate.BorrowOrAcquireSharedAsync(a.AccountId, default);
            await using var peer = await gate.BorrowOrAcquireSharedAsync(b.AccountId, default);
            order.Add("first-ACK");
            order.Add("first-NOTI");
        }, default));
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        var second = TestGameplay.RunAsync(async () => await WeddingProposalExecution.RunAsync(gate, b, a, () =>
        { order.Add("second-ACK+NOTI"); return ValueTask.CompletedTask; }, default));
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["first-ACK", "first-NOTI", "second-ACK+NOTI"], order);
    }
}
