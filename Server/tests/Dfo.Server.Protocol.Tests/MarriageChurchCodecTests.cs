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
        Assert.Equal(72u, request.Peer); Assert.Equal(13u, request.ChurchArea); Assert.Equal(0, request.Flag);
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
        var room = new MarriageChurchRoom(13, Guid.NewGuid(), 1, 2, 12, DateTimeOffset.UnixEpoch, DateTimeOffset.FromUnixTimeSeconds(1700000300));
        var snapshot = new MarriageChurchSnapshot(room, new CharacterId(72), Encoding.UTF8.GetBytes(new string('婚', 12)),
            "bride"u8.ToArray(), new(2, 12, 300, 350));
        var frame = Assert.Single(new WeddingChurchPreparationEncoder().Encode(new WeddingChurchPreparationNotification(snapshot)));
        Assert.Equal(94, frame.Length); Assert.Equal(0, frame[0]); Assert.Equal(0x03D4, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        var body = frame[15..];
        Assert.Equal(72u, BinaryPrimitives.ReadUInt32LittleEndian(body));
        Assert.Equal(12u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(4)));
        Assert.Equal(1700000300u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(8)));
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(12)));
        Assert.Equal(0, body[16]); Assert.Equal(0, body[46]); Assert.Equal(0, body[76]);
        Assert.Equal(new string('婚', 9), new UTF8Encoding(false, true).GetString(body.AsSpan(17, 27)));
        Assert.Equal("bride"u8.ToArray(), body[47..52]);
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(77)));
    }
    [Fact]
    public void Wedding_begin_letters_and_remaining_quota_match_native_readers()
    {
        var room = new MarriageChurchRoom(13, Guid.NewGuid(), 1, 2, 12, DateTimeOffset.UnixEpoch, DateTimeOffset.FromUnixTimeSeconds(1700000300));
        var church = new MarriageChurchSnapshot(room, new CharacterId(72), "groom"u8.ToArray(), "bride"u8.ToArray(), new(2, 12, 300, 350));
        var avatar = Assert.Single(new WeddingAvatarEncoder().Encode(new WeddingAvatarNotification(church)))[15..];
        Assert.Equal(67, avatar.Length); Assert.Equal(0, avatar[0]);
        Assert.Equal("groom"u8.ToArray(), avatar[2..7]); Assert.Equal("bride"u8.ToArray(), avatar[32..37]);
        Assert.Equal(12u, BinaryPrimitives.ReadUInt32LittleEndian(avatar.AsSpan(62)));
        var begin = Assert.Single(new WeddingBeginEncoder().Encode(new WeddingBeginNotification(church)))[15..];
        Assert.Equal(68, begin.Length); Assert.Equal(72u, BinaryPrimitives.ReadUInt32LittleEndian(begin));
        Assert.Equal(12u, BinaryPrimitives.ReadUInt32LittleEndian(begin.AsSpan(4)));
        Assert.Equal("groom"u8.ToArray(), begin[8..13]); Assert.Equal("bride"u8.ToArray(), begin[38..43]);
        var letter = Assert.Single(new WeddingLetterEncoder().Encode(new WeddingLetterNotification(church, 72, true)))[15..];
        Assert.Equal(78, letter.Length); Assert.Equal(1, letter[77]);
        Assert.Equal(Convert.FromHexString("01000000000A00"), Assert.Single(new WeddingLetterAckEncoder().Encode(new WeddingLetterAck(0, 10)))[15..]);
        Assert.Equal(Convert.FromHexString("010000000048000000"), Assert.Single(new WeddingGiftAckEncoder().Encode(new WeddingGiftAck(0, 72)))[15..]);
        Assert.Equal(Convert.FromHexString("02490000004A000000"), Assert.Single(new WeddingInvitedGuestsEncoder().Encode(new WeddingInvitedGuestsNotification([73,74])))[15..]);
    }
    [Fact]
    public void Invitation_and_blessing_decoders_keep_their_distinct_native_layouts()
    {
        var request = new byte[22]; request[14] = 1;
        var letter = Assert.IsType<WeddingLetterRequest>(new WeddingLetterDecoder().Decode(GameMessageMetadata.Empty, request));
        Assert.Equal(1, letter.Mode); Assert.Equal(0u, letter.Target);
        var blessing = new byte[15]; BinaryPrimitives.WriteUInt32LittleEndian(blessing,72);
        BinaryPrimitives.WriteInt32LittleEndian(blessing.AsSpan(4),10000); BinaryPrimitives.WriteInt32LittleEndian(blessing.AsSpan(8),3);
        "yes"u8.CopyTo(blessing.AsSpan(12));
        var gift = Assert.IsType<WeddingGiftRequest>(new WeddingGiftDecoder().Decode(GameMessageMetadata.Empty, blessing));
        Assert.Equal(72u,gift.Key); Assert.Equal(10000,gift.Gold); Assert.Equal("yes",gift.Text);
        Assert.Throws<InvalidDataException>(() => new WeddingGiftDecoder().Decode(GameMessageMetadata.Empty, blessing[..14]));
        Assert.Throws<InvalidDataException>(() => new WeddingLetterDecoder().Decode(GameMessageMetadata.Empty, request[..21]));
    }

}
