using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Dfo.Server.Application.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Data.Marriage;

namespace Dfo.Server.Protocol.Tests;

public sealed class MarriageProposalCodecTests
{
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
        Assert.Equal(12, response[43]); Assert.Equal(85, response[44]); Assert.Equal(0x11, response[45]);
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
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var first = WeddingProposalExecution.RunAsync(gate, a, b, async () =>
        {
            await using var own = await gate.BorrowOrAcquireSharedAsync(a.AccountId, default);
            await using var peer = await gate.BorrowOrAcquireSharedAsync(b.AccountId, default);
            order.Add("first-ACK"); entered.SetResult();
            await finish.Task;
            order.Add("first-NOTI");
        }, default).AsTask();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = WeddingProposalExecution.RunAsync(gate, b, a, () =>
        { order.Add("second-ACK+NOTI"); return ValueTask.CompletedTask; }, default).AsTask();
        Assert.False(second.IsCompleted);
        finish.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(["first-ACK", "first-NOTI", "second-ACK+NOTI"], order);
    }
}
