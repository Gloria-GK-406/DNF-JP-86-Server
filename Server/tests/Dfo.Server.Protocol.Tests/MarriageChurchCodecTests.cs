using System.Buffers.Binary;
using System.Text;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.MarriageProposal;

namespace Dfo.Server.Protocol.Tests;

public sealed class MarriageChurchCodecTests
{
    [Fact]
    public void Native_entry_keeps_opaque_prefix_and_exact_23_byte_structure()
    {
        var body = Enumerable.Repeat((byte)0xA5, 23).ToArray();
        Convert.FromHexString("480000000D00000000").CopyTo(body, 14);
        var request = Assert.IsType<WeddingChurchEnterRequest>(new WeddingChurchEnterDecoder().Decode(GameMessageMetadata.Empty, body));
        Assert.Equal(72u, request.Peer); Assert.Equal(13u, request.Mode); Assert.Equal(0, request.Flag);
        Assert.Equal(Convert.FromHexString("0100000000"), Assert.Single(new WeddingChurchEnterEncoder().Encode(new WeddingChurchEnterAck(0)))[15..]);
        Assert.Equal(Convert.FromHexString("01F6FFFFFF"), Assert.Single(new WeddingChurchEnterEncoder().Encode(new WeddingChurchEnterAck(-10)))[15..]);
    }

    [Theory]
    [InlineData(0)] [InlineData(9)] [InlineData(22)] [InlineData(24)] [InlineData(37)]
    public void Entry_rejects_compact_truncated_extra_and_full_frame_data(int size) =>
        Assert.Throws<InvalidDataException>(() => new WeddingChurchEnterDecoder().Decode(GameMessageMetadata.Empty, new byte[size]));

    [Fact]
    public void Preparation_matches_native_79_byte_reader_with_two_names_and_separate_tail_word()
    {
        var room = new MarriageChurchRoom(13, Guid.NewGuid(), 1, 21, 12, DateTimeOffset.UnixEpoch, DateTimeOffset.FromUnixTimeSeconds(1700000300));
        var snapshot = new MarriageChurchSnapshot(room, new CharacterId(72), Encoding.UTF8.GetBytes(new string('婚', 12)),
            "bride"u8.ToArray(), new(21, 12, 300, 350));
        var frame = Assert.Single(new WeddingChurchPreparationEncoder().Encode(new WeddingChurchPreparationNotification(snapshot)));
        Assert.Equal(94, frame.Length); Assert.Equal(0, frame[0]); Assert.Equal(0x03D4, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        var body = frame[15..];
        Assert.Equal(72u, BinaryPrimitives.ReadUInt32LittleEndian(body));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));
        Assert.Equal(1700000300u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));
        Assert.Equal(0, body[16]); Assert.Equal(0, body[46]); Assert.Equal(0, body[76]);
        Assert.Equal(new string('婚', 9), new UTF8Encoding(false, true).GetString(body.AsSpan(17, 27)));
        Assert.Equal("bride"u8.ToArray(), body[47..52]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(77)));
    }
}
