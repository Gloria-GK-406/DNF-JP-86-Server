using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildManagement;

namespace Dfo.Server.Protocol.Tests.Game.GuildManagement;

public sealed class GuildManagementCodecTests
{
    [Fact]
    public void Medal_aura_request_reads_symbol_then_frame_and_accepts_the_clear_pair()
    {
        var decoder = new GuildMedalAuraDecoder();
        var choice = Decode(decoder, [2, 0, 0, 0, 5, 0, 0, 0]);
        Assert.Equal(ClientMessageType.GameGuildSetMedalAura, choice.MessageType);
        Assert.True(choice.Valid);
        Assert.Equal(new Dfo.Server.Data.Guilds.GuildMedalAura(2, 5), choice.MedalAura);
        Assert.Equal(Dfo.Server.Data.Guilds.GuildMedalAura.None, Decode(decoder, [255, 255, 255, 255, 255, 255, 255, 255]).MedalAura);
        Assert.False(Decode(decoder, [2, 0, 0, 0, 5, 0, 0]).Valid);
    }

    [Fact]
    public void Management_decoders_preserve_rank_and_mask_and_require_complete_fields()
    {
        var permissions = new GuildRankPermissionsDecoder();
        var decoded = Assert.IsType<GuildManagementRequest>(permissions.Decode(GameMessageMetadata.Empty, [4, 0, 128, 0, 0]));
        Assert.True(decoded.Valid);
        Assert.Equal(4, decoded.Rank);
        Assert.Equal(0x8000u, decoded.PermissionMask);
        var unknown = Decode(permissions, [0, 0, 0, 1, 128]);
        Assert.True(unknown.Valid);
        Assert.Equal(0, unknown.Rank);
        Assert.Equal(0x80010000u, unknown.PermissionMask);
        Assert.False(Decode(permissions, [4, 0, 0, 0]).Valid);
        Assert.False(Decode(permissions, [4, 0, 0, 0, 0, 0]).Valid);

        var promotion = new GuildPromotionDecoder();
        Assert.Equal("", Decode(promotion, [0, 0, 0, 0]).Text);
        Assert.False(Decode(promotion, [1, 0, 0, 0, 0xff]).Valid);

        var rankName = new GuildRankNameDecoder();
        Assert.Equal("A", Decode(rankName, [4, 1, 0, 0, 0, 65]).Text);
        Assert.False(Decode(rankName, [4, 1, 0, 0, 0, 65, 0]).Valid);
        var unknownRank = Decode(rankName, [255, 0, 0, 0, 0]);
        Assert.True(unknownRank.Valid);
        Assert.Equal(255, unknownRank.Rank);
        Assert.Equal("", unknownRank.Text);
        Assert.False(Decode(rankName, [4, 1, 0, 0, 0]).Valid);
    }

    [Fact]
    public void Complete_management_strings_are_decoded_even_when_the_text_exceeds_gameplay_limits()
    {
        var text = new string('A', 121);
        var payload = new byte[sizeof(int) + text.Length];
        BinaryPrimitives.WriteInt32LittleEndian(payload, text.Length);
        System.Text.Encoding.UTF8.GetBytes(text, payload.AsSpan(sizeof(int)));
        foreach (var request in new[] { Decode(new GuildPromotionDecoder(), payload),
            Decode(new GuildAnnouncementDecoder(), payload), Decode(new GuildRankNameDecoder(), [4, .. payload]) })
        {
            Assert.True(request.Valid);
            Assert.Equal(text, request.Text);
        }
    }

    [Fact]
    public void Announcement_and_mark_decode_their_own_payloads()
    {
        var announcement = new GuildAnnouncementDecoder();
        Assert.Equal("Hi", Decode(announcement, [2, 0, 0, 0, 72, 105]).Text);
        Assert.False(Decode(announcement, [1, 0, 0, 0, 0xff]).Valid);
        var mark = new GuildMarkDecoder();
        Assert.True(Decode(mark, [0]).Valid);
        Assert.Equal(15, Decode(mark, [15]).Mark);
        var unknownMark = Decode(mark, [16]);
        Assert.True(unknownMark.Valid);
        Assert.Equal(16, unknownMark.Mark);
        Assert.False(Decode(mark, []).Valid);
        Assert.False(Decode(mark, [0, 1]).Valid);

        var frame = Assert.Single(new GuildAnnouncementEncoder().Encode(new GuildAnnouncementUpdated("Hi")));
        Assert.Equal(0x008D, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal("020000004869", Convert.ToHexString(frame[15..]));
    }

    [Fact]
    public void Personal_memo_uses_one_dstring_and_accepts_clearing()
    {
        var decoder = new GuildMemberMemoDecoder();
        GuildMemberMemoRequest Read(byte[] body) =>
            Assert.IsType<GuildMemberMemoRequest>(decoder.Decode(GameMessageMetadata.Empty, body));

        Assert.Equal("测", Read([3, 0, 0, 0, 0xE6, 0xB5, 0x8B]).Memo);
        Assert.Equal("", Read([0, 0, 0, 0]).Memo);
        Assert.Null(Read([4, 3, 0, 0, 0, 0xE6, 0xB5, 0x8B]).Memo);
        Assert.Null(Read([1, 0, 0, 0, 0xFF]).Memo);

        var frame = Assert.Single(new GuildManagementReplyEncoder().Encode(new GuildManagementReply(ServerMessageType.GameGuildSetMemberMemo)));
        Assert.Equal(0x00E0, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(new byte[] { 1 }, frame[15..]);
    }

    [Fact]
    public void Guild_rename_confirms_only_a_main_bag_card_slot()
    {
        var decoder = new GuildRenameDecoder();
        GuildRenameRequest Read(byte[] body) => Assert.IsType<GuildRenameRequest>(decoder.Decode(GameMessageMetadata.Empty, body));

        Assert.Equal(121, Read([121, 0, 0]).Source?.Index);
        Assert.Null(Read([121, 0, 1]).Source);
        Assert.Null(Read([121, 0, 0, 0, 0, 0, 0]).Source);
        Assert.Null(Read([177, 0, 0]).Source);
        var notice = Assert.Single(new GuildRenamedEncoder().Encode(new GuildRenamed("新公会")));
        Assert.Equal(0x00DA, BinaryPrimitives.ReadUInt16LittleEndian(notice.AsSpan(1)));
        Assert.Equal("09000000E696B0E585ACE4BC9A", Convert.ToHexString(notice[15..]));
    }

    [Fact]
    public void Leadership_transfer_matches_the_A21_target_name_frame()
    {
        var sample = Convert.FromHexString("0A0000004775696C6450696C6F74");
        var decoder = new GuildLeadershipTransferDecoder();
        var request = Assert.IsType<GuildLeadershipTransferRequest>(decoder.Decode(GameMessageMetadata.Empty, sample));
        Assert.Equal("GuildPilot"u8.ToArray(), request.TargetName);
        Assert.Null(Assert.IsType<GuildLeadershipTransferRequest>(
            decoder.Decode(GameMessageMetadata.Empty, [.. sample, 1])).TargetName);

        var notice = Assert.Single(new GuildLeadershipTransferredEncoder().Encode(new GuildLeadershipTransferred(request.TargetName!)));
        Assert.Equal(0, notice[0]);
        Assert.Equal(0x0095, BinaryPrimitives.ReadUInt16LittleEndian(notice.AsSpan(1)));
        Assert.Equal(sample, notice[15..]);
        var reply = Assert.Single(new GuildManagementReplyEncoder().Encode(new GuildManagementReply(ServerMessageType.GameGuildTransferLeadership)));
        Assert.Equal(0x009B, BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(1)));
        Assert.Equal(new byte[] { 1 }, reply[15..]);
    }

    [Fact]
    public void Reply_selector_and_flag_follow_client_command_envelope()
    {
        var encoder = new GuildManagementReplyEncoder();
        var success = Assert.Single(encoder.Encode(new GuildManagementReply(ServerMessageType.GameGuildSetPromotion)));
        var refusal = Assert.Single(encoder.Encode(new GuildManagementReply(ServerMessageType.GameGuildSetRankName, 0x22)));
        Assert.Equal(1, success[0]);
        Assert.Equal(0x02E3, BinaryPrimitives.ReadUInt16LittleEndian(success.AsSpan(1)));
        Assert.Equal(new byte[] { 1 }, success[15..]);
        Assert.Equal(0x02EC, BinaryPrimitives.ReadUInt16LittleEndian(refusal.AsSpan(1)));
        Assert.Equal(new byte[] { 0, 0x22 }, refusal[15..]);
    }

    private static GuildManagementRequest Decode(IGameMessageDecoder decoder, byte[] body) =>
        Assert.IsType<GuildManagementRequest>(decoder.Decode(GameMessageMetadata.Empty, body));
}
