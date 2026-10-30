using System.Buffers.Binary;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MarriageProposal;
using Dfo.Server.Protocol.Game.UseStackable;
namespace Dfo.Server.Protocol.Tests;
[Trait("Category", "Unit")]
public sealed class WeddingRepairCodecTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    public void Forced_standing_has_native_teleport_uid_coordinates_and_facing(byte direction)
    {
        var frame = Assert.Single(new Dfo.Server.Protocol.Game.TownProjection.ForcedTownPositionEncoder()
            .Encode(new Dfo.Server.Protocol.Game.TownProjection.ForcedTownPositionNotification(8, 670, 330, direction)));
        Assert.Equal(0, frame[0]);
        Assert.Equal(0x008A, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal(22, frame.Length);
        Assert.Equal(Convert.FromHexString(direction == 4 ? "08009E024A0104" : "08009E024A0105"), frame[15..]);
    }
    [Fact]
    public void Guestbook_native_requests_ignore_opaque_prefix_and_keep_raw_codepage_bytes()
    {
        var post = Assert.IsType<WeddingCommentRequest>(new WeddingCommentPostDecoder().Decode(GameMessageMetadata.Empty, Convert.FromHexString("4800000004000000D0C2BBA9")));
        Assert.Equal(72u, post.Key); Assert.Equal(Convert.FromHexString("D0C2BBA9"), post.Text);
        var read = Enumerable.Repeat((byte)0xA5, 19).ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(read.AsSpan(14), 72); read[18] = 1;
        Assert.Equal(72u, Assert.IsType<WeddingCommentRequest>(new WeddingCommentReadDecoder().Decode(GameMessageMetadata.Empty, read)).Key);
        var delete = Enumerable.Repeat((byte)0x5A, 23).ToArray(); BinaryPrimitives.WriteUInt32LittleEndian(delete.AsSpan(14), 72); BinaryPrimitives.WriteUInt32LittleEndian(delete.AsSpan(18), 8);
        Assert.Equal(8u, Assert.IsType<WeddingCommentRequest>(new WeddingCommentDeleteDecoder().Decode(GameMessageMetadata.Empty, delete)).Id);
    }
    [Theory]
    [InlineData(7)] [InlineData(9)] [InlineData(29)]
    public void Comment_post_rejects_truncated_trailing_and_oversized_text(int length) =>
        Assert.Throws<InvalidDataException>(() => new WeddingCommentPostDecoder().Decode(GameMessageMetadata.Empty, new byte[length]));
    [Fact]
    public void Guestbook_response_has_exact_42_byte_row_header_and_native_ack_shapes()
    {
        var item = new MarriageGuestComment(8, new CharacterId(99), "guest"u8.ToArray(), 1234, new byte[] { 0xD0, 0xC2 });
        var b = Assert.Single(new WeddingCommentEncoder().Encode(new WeddingCommentAck(WeddingCommentKind.Read, 72, 0, new(0, [item]))))[15..];
        Assert.Equal(57, b.Length); Assert.Equal(1, b[0]); Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(9)));
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(13)));
        Assert.Equal(1234u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(47)));
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(51))); Assert.Equal(item.Text, b[55..]);
        Assert.Equal(Convert.FromHexString("010000000008000000"), Assert.Single(new WeddingCommentEncoder().Encode(new WeddingCommentAck(WeddingCommentKind.Delete, 72, 8, new(0, []))))[15..]);
        Assert.Equal(Convert.FromHexString("01FFFFFFFF"), Assert.Single(new WeddingCommentEncoder().Encode(new WeddingCommentAck(WeddingCommentKind.Read, 72, 0, new(-1, []))))[15..]);
    }
    [Fact]
    public void Shared_effect_native_vectors_distinguish_cmd_ack_and_scene_notification()
    {
        var request = Assert.IsType<SharedEffectRequest>(new SharedEffectDecoder().Decode(GameMessageMetadata.Empty, Convert.FromHexString("D1A098004100")));
        Assert.Equal(10002641, request.ItemId); Assert.Equal(65, request.Slot);
        Assert.Equal(Convert.FromHexString("01410000"), Assert.Single(new SharedEffectAckEncoder().Encode(new SharedEffectAck(true, 65)))[15..]);
        Assert.Equal(Convert.FromHexString("0013"), Assert.Single(new SharedEffectAckEncoder().Encode(new SharedEffectAck(false, 65)))[15..]);
        Assert.Equal(Convert.FromHexString("0800D1A0980000"), Assert.Single(new SharedEffectNotificationEncoder().Encode(new SharedEffectNotification(8, 10002641, 0)))[15..]);
        Assert.Throws<InvalidDataException>(() => new SharedEffectDecoder().Decode(GameMessageMetadata.Empty, new byte[7]));
    }
}
