using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildManagement;

namespace Dfo.Server.Protocol.Tests.Game.GuildManagement;

public sealed class GuildManagementCodecTests
{
    [Fact]
    public void Management_decoders_check_rank_mask_text_and_complete_body()
    {
        var permissions = new GuildRankPermissionsDecoder();
        var decoded = Assert.IsType<GuildManagementRequest>(permissions.Decode(GameMessageMetadata.Empty, [4, 0, 128, 0, 0]));
        Assert.True(decoded.Valid);
        Assert.Equal(4, decoded.Rank);
        Assert.Equal(0x8000u, decoded.PermissionMask);
        Assert.False(Decode(permissions, [4, 0, 0, 1, 0]).Valid);
        Assert.False(Decode(permissions, [0, 0, 0, 0, 0]).Valid);
        Assert.False(Decode(permissions, [4, 0, 0, 0, 0, 0]).Valid);

        var promotion = new GuildPromotionDecoder();
        Assert.Equal("", Decode(promotion, [0, 0, 0, 0]).Text);
        Assert.False(Decode(promotion, [1, 0, 0, 0, 0xff]).Valid);

        var rankName = new GuildRankNameDecoder();
        Assert.Equal("A", Decode(rankName, [4, 1, 0, 0, 0, 65]).Text);
        Assert.False(Decode(rankName, [4, 1, 0, 0, 0, 65, 0]).Valid);
        Assert.False(Decode(rankName, [0, 1, 0, 0, 0, 65]).Valid);
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
