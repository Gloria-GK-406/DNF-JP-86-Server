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
[Trait("Category", "Unit")]
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

    /// <summary>
    /// The money form of the pick-up notification, against a captured frame.
    /// </summary>
    /// <remarks>
    /// <c>packet_log.upstream-killexp-20260827.txt</c> line 454: piece 1, user 3, 38 gold -- and
    /// that same run's kill frame shows actor 38088 dropping exactly 38 gold as piece 1, so the
    /// two halves of the exchange agree.
    /// <para>
    /// The whole 117-byte body is transcribed. Its last 107 bytes are identical in a second,
    /// independent capture that paid 131 to a different user, which is what makes them a constant
    /// rather than something this server invented a structure for.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_money_form_is_the_captured_frame()
    {
        var body = GroundPickupPayloadCodec.EncodeGoldNotification(
            new GroundGoldPickupNotification(1, 3, 38));

        Assert.Equal(117, body.Length);
        Assert.Equal(Bytes(
            "01 00 03 00 00 01 26 00 00 00 01 00 00 00 00 00 00 00 00 01 00 00 00 00 01 00 00 00 "
                + "00 00 00 00 00 01 00 00 00 00 01 00 00 00 00 00 00 00 00 01 00 00 00 00 01 00 "
                + "00 00 00 00 00 00 00 01 00 00 00 00 01 00 00 00 00 00 00 00 00 01 00 00 00 00 "
                + "01 00 00 00 00 00 00 00 00 01 00 00 00 00 01 00 00 00 00 00 00 00 00 01 00 00 "
                + "00 00 01 00 00 00 00 00 00 00 00"),
            body);
    }

    /// <summary>
    /// The second capture, which differs in every field that is not the tail.
    /// </summary>
    /// <remarks>
    /// <c>packet_log.reference-20260823.txt</c> line 550: piece 2, user 1, 131 gold. Its kill
    /// frame paid 131 under piece 2. Two captures agreeing on 107 bytes while disagreeing on the
    /// other ten is what separates a constant from a coincidence.
    /// </remarks>
    [Fact]
    public void A_second_capture_shares_the_tail_and_nothing_else()
    {
        var first = GroundPickupPayloadCodec.EncodeGoldNotification(
            new GroundGoldPickupNotification(1, 3, 38));
        var second = GroundPickupPayloadCodec.EncodeGoldNotification(
            new GroundGoldPickupNotification(2, 1, 131));

        Assert.Equal(Bytes("02 00 01 00 00 01 83 00 00 00"), second[..10]);
        Assert.Equal(first[10..], second[10..]);
    }

    /// <summary>
    /// The byte that tells the client money from an item.
    /// </summary>
    /// <remarks>
    /// An item writes one at <c>+4</c> and money writes zero. Both captures agree, and it is the
    /// only field the two forms share a position for and disagree about.
    /// </remarks>
    [Fact]
    public void The_fifth_byte_is_what_tells_the_two_forms_apart()
    {
        var money = GroundPickupPayloadCodec.EncodeGoldNotification(
            new GroundGoldPickupNotification(1, 3, 38));
        var item = GroundPickupPayloadCodec.EncodeNotification(
            new GroundPickupNotification(1, 3, 9));

        Assert.Equal(0x00, money[4]);
        Assert.Equal(0x01, item[4]);
    }

    private static byte[] Bytes(string hex) =>
        [.. hex.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => Convert.ToByte(pair, 16))];
}
