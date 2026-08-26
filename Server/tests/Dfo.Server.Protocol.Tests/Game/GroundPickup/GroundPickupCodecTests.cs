using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GroundPickup;

namespace Dfo.Server.Protocol.Tests.Game.GroundPickup;

/// <summary>
/// Picking a dropped piece up off a room's floor, and the two frames that answer it.
/// </summary>
/// <remarks>
/// The request is a live one: the tutorial's second room on 2026-08-26, the box broken and the
/// shoulder on the ground, the player standing on it. The answers are the reference builder's --
/// a one-byte acknowledgement and an eighteen-byte notification.
/// </remarks>
public sealed class GroundPickupCodecTests
{
    /// <remarks>
    /// The captured body, byte for byte. Only the first two are read, which is what the reference
    /// server reads: the piece is named by the number this server gave it when it described the
    /// room, and the rest is where the player and the item are standing.
    /// </remarks>
    [Fact]
    public void The_captured_request_names_the_piece_by_the_number_the_room_gave_it()
    {
        byte[] body =
        [
            0x01, 0x00, 0x00, 0x00, 0xE0, 0x02, 0xD5, 0x00, 0xC6, 0x7A,
            0xF4, 0x02, 0xC8, 0x00, 0x57, 0x24, 0x15, 0x50,
        ];

        var request = (GetItemRequest)new GetItemDecoder().Decode(GameMessageMetadata.Empty, body)!;

        Assert.Equal(1, request.GroundDropNumber);
    }

    [Fact]
    public void A_body_shorter_than_the_pieces_number_is_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            new GetItemDecoder().Decode(GameMessageMetadata.Empty, new byte[1]));
    }

    /// <remarks>
    /// One byte for yes and two for a full bag, which are the only two answers the reference
    /// server sends under this type. Everything else it leaves unanswered.
    /// </remarks>
    [Fact]
    public void The_acknowledgement_is_one_byte_and_the_only_refusal_is_the_full_bag()
    {
        Assert.Equal(
            [0x01], GroundPickupPayloadCodec.EncodeResponse(new GetItemResponse(Accepted: true)));
        Assert.Equal(
            [0x00, 0x04],
            GroundPickupPayloadCodec.EncodeResponse(new GetItemResponse(Accepted: false)));
    }

    /// <remarks>
    /// Eighteen bytes, and the picker named twice: once as who took it and once as who holds it.
    /// The two are the same actor for a pick-up and stop being the same the moment a party member
    /// is handed something, which is why the frame carries both.
    /// </remarks>
    [Fact]
    public void The_notification_says_which_piece_went_into_which_slot()
    {
        var body = GroundPickupPayloadCodec.EncodeNotification(
            new GroundPickupNotification(GroundDropNumber: 1, PickerActorId: 0x1234, DestinationSlot: 9));

        Assert.Equal(18, body.Length);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(body));
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2)));
        Assert.Equal(1, body[4]);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(9)));
        Assert.Equal(0x1234, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(13)));
        Assert.Equal(9, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(15)));
        Assert.Equal(0, body[17]);
    }
}
