using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.UseStackable;

namespace Dfo.Server.Protocol.Tests.Game.UseStackable;

/// <summary>
/// Drinking a potion.
/// </summary>
/// <remarks>
/// The body below is the live one, captured on 2026-08-18 from the tutorial: a potion used out of
/// quick slot 3. The static census could not close this layout -- the send path goes through a
/// virtual call whose payload depends on the item type -- but the prefix the reference server reads
/// matches the observed frame field for field.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class UseStackableCodecTests
{
    private static readonly byte[] ObservedBody =
    [
        0x03, 0x00, 0x00, 0xF5, 0x62, 0x00, 0x00, 0x1A,
        0x21, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    /// <remarks>
    /// Slot 3 of list 0: the first quick slot, which is where the potion had been moved. The item
    /// identifier is the potion's own, so the client is naming what it believes it is using rather
    /// than leaving the server to look the slot up.
    /// </remarks>
    [Fact]
    public void The_observed_request_names_the_quick_slot_and_the_potion()
    {
        var request = Decode(ObservedBody);

        Assert.Equal(new GameSlot(InventoryContainerType.Main, 3), request.Slot);
        Assert.Equal(8474, request.ItemCode);
    }

    /// <remarks>
    /// Nonzero, and not the item identifier. Nothing yet says what it counts; the reference server
    /// treats it as opaque and echoes it back, so the acknowledgement carries it unchanged.
    /// </remarks>
    [Fact]
    public void The_instance_value_is_carried_without_being_interpreted()
    {
        Assert.Equal(25333, Decode(ObservedBody).InstanceValue);
    }

    /// <remarks>
    /// Seven bytes is the shortest the reference server will parse. The item identifier past
    /// offset 7 is read only when the body carries it.
    /// </remarks>
    [Fact]
    public void A_body_without_the_item_identifier_still_decodes()
    {
        var request = Decode(ObservedBody.AsSpan(0, 7).ToArray());

        Assert.Equal(3, request.Slot.Index);
        Assert.Equal(0, request.ItemCode);
    }

    [Fact]
    public void A_body_shorter_than_the_prefix_is_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            new UseStackableDecoder().Decode(GameMessageMetadata.Empty, new byte[6]));
    }

    /// <remarks>
    /// Twelve bytes, and no remaining count. The handler publishes the final slot separately
    /// through NOTI 000E; the acknowledgement itself preserves the observed layout.
    /// </remarks>
    [Fact]
    public void A_success_names_the_slot_it_came_from()
    {
        var request = Decode(ObservedBody);
        var response = UseStackableResponse.Success(
            request.Slot, request.InstanceValue, 8474);

        Assert.Equal(
            [0x01, 0x03, 0x00, 0x00, 0xF5, 0x62, 0x00, 0x00, 0x1A, 0x21, 0x00, 0x00],
            UseStackablePayloadCodec.Encode(response));
    }

    /// <remarks>
    /// Eleven bytes, and no slot: the reference builder writes the error code where the success
    /// writes the slot, so the two bodies differ in length as well as in content.
    /// </remarks>
    [Fact]
    public void A_refusal_replaces_the_slot_with_the_code()
    {
        var response = UseStackableResponse.Failure(
            Decode(ObservedBody).Slot,
            Decode(ObservedBody).InstanceValue,
            Decode(ObservedBody).ItemCode,
            UseStackableResponse.NotConsumable);

        Assert.Equal(
            [0x00, 0x17, 0x00, 0xF5, 0x62, 0x00, 0x00, 0x1A, 0x21, 0x00, 0x00],
            UseStackablePayloadCodec.Encode(response));
    }

    private static UseStackableRequest Decode(byte[] body) =>
        (UseStackableRequest)new UseStackableDecoder().Decode(GameMessageMetadata.Empty, body)!;
}
