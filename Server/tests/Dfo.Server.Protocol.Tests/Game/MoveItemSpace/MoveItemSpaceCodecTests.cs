using Dfo.Server.Data.Networking;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.MoveItemSpace;

namespace Dfo.Server.Protocol.Tests.Game.MoveItemSpace;

/// <summary>
/// The request behind every inventory drag, and behind the quick-slot bar.
/// </summary>
/// <remarks>
/// The body below is the live one, captured on 2026-08-18. It arrived unprompted, one frame after
/// the tutorial reward acknowledgement told the client the potion had landed in slot 65, and it is
/// the last frame in that capture: the client waits for the acknowledgement before it will accept
/// another drag.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class MoveItemSpaceCodecTests
{
    private static readonly byte[] ObservedBody =
    [
        0x00, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x41, 0x00, 0x1A, 0x21,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    /// <remarks>
    /// Both ends name list 0. The quick-slot bar is not its own container: it is slots 3 to 8 of
    /// the main list, so binding an item to a number key travels as a move inside one list.
    /// </remarks>
    [Fact]
    public void The_observed_request_names_the_main_list_at_both_ends()
    {
        var request = Decode(ObservedBody);

        Assert.Equal(InventoryContainerType.Main, request.SourceSlot.Space);
        Assert.Equal(InventoryContainerType.Main, request.DestinationSlot.Space);
    }

    /// <remarks>
    /// Slot 3 is the first quick slot and slot 65 is the first slot of the consumable band, which
    /// is where the reward had just been put. The instance value sits on the destination side --
    /// the end that holds the item -- so the request is the reversed form: the client is naming
    /// the slot it wants filled, not the slot it is emptying.
    /// </remarks>
    [Fact]
    public void The_observed_request_asks_for_the_potion_by_its_destination_end()
    {
        var request = Decode(ObservedBody);

        Assert.Equal(3, request.SourceSlot.Index);
        Assert.Equal(0, request.SourceInstanceValue);
        Assert.Equal(65, request.DestinationSlot.Index);
        Assert.Equal(8474, request.DestinationInstanceValue);
    }

    /// <remarks>
    /// Zero, for a drag the player did not split. The reference server reads it as "the whole
    /// stack" rather than as "nothing".
    /// </remarks>
    [Fact]
    public void A_plain_drag_carries_no_move_count()
    {
        Assert.Equal(0, Decode(ObservedBody).MoveCount);
    }

    /// <remarks>
    /// Fourteen bytes is the shortest the reference server will parse; the two fields past offset
    /// 14 are read only when the body carries them, so a shorter frame is a valid request rather
    /// than a truncated one.
    /// </remarks>
    [Fact]
    public void A_body_without_the_destination_instance_still_decodes()
    {
        var request = Decode(ObservedBody.AsSpan(0, 14).ToArray());

        Assert.Equal(65, request.DestinationSlot.Index);
        Assert.Equal(0, request.DestinationInstanceValue);
    }

    [Fact]
    public void A_body_shorter_than_the_closed_prefix_is_refused()
    {
        Assert.Throws<InvalidDataException>(() =>
            new MoveItemSpaceDecoder().Decode(GameMessageMetadata.Empty, new byte[13]));
    }

    /// <remarks>
    /// Eleven bytes: the success flag, then the request's own two ends with the moved count
    /// between them. The client matches the acknowledgement against what it sent, so both ends
    /// are echoed rather than restated as the server sees them.
    /// </remarks>
    [Fact]
    public void A_success_carries_both_ends_and_the_moved_count()
    {
        var request = Decode(ObservedBody);
        var response = MoveItemSpaceResponse.Success(
            request.SourceSlot, 1, request.DestinationSlot);

        Assert.Equal(
            [0x01, 0x00, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x41, 0x00],
            MoveItemSpacePayloadCodec.Encode(response));
    }

    /// <remarks>
    /// Four bytes, and no slots: a refusal names only the two lists, because the client redraws
    /// both from what it already holds.
    /// </remarks>
    [Fact]
    public void A_refusal_carries_the_code_and_the_two_lists()
    {
        var response = MoveItemSpaceResponse.Failure(
            Decode(ObservedBody).SourceSlot,
            Decode(ObservedBody).DestinationSlot,
            MoveItemSpaceResponse.Silent);

        Assert.Equal([0x00, 0x02, 0x00, 0x00], MoveItemSpacePayloadCodec.Encode(response));
    }

    private static MoveItemSpaceRequest Decode(byte[] body) =>
        (MoveItemSpaceRequest)new MoveItemSpaceDecoder().Decode(GameMessageMetadata.Empty, body)!;
}
