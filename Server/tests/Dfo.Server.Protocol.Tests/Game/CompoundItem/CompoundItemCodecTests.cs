using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CompoundItem;

namespace Dfo.Server.Protocol.Tests.Game.CompoundItem;
[Trait("Category", "Unit")]
public sealed class CompoundItemCodecTests
{
    [Theory]
    [InlineData("650000000001000001")] [InlineData("65000000000100")]
    public void Real_request_and_the_other_static_builder_decode_without_guessing_a_slot_width(string hex)
    {
        var request = Assert.IsType<CompoundItemRequest>(new CompoundItemDecoder().Decode(GameMessageMetadata.Empty, Convert.FromHexString(hex)));
        Assert.True(request.Valid); Assert.Equal(101, request.Source); Assert.Equal((byte)0, request.SourceMode); Assert.Equal((ushort)1, request.Count);
    }
    [Theory]
    [InlineData("650000000001000000")] [InlineData("650000000001000101")]
    [InlineData("65000000000100000100")] [InlineData("6500000000")]
    public void Truncated_extended_or_unknown_tail_requests_are_invalid(string hex) =>
        Assert.False(Assert.IsType<CompoundItemRequest>(new CompoundItemDecoder().Decode(GameMessageMetadata.Empty, Convert.FromHexString(hex))).Valid);
    [Fact]
    public void Failure_ack_has_no_success_deltas() => Assert.Equal(new byte[] { 0, 0x16 }, Body(new CompoundItemResponse(null, 0x16)));
    [Fact]
    public void Multi_reward_ack_carries_each_full_row_and_all_physical_and_virtual_costs()
    {
        var session = SelectedSessionIdentity.Create(new(1), new(2), new("recipe"), 1, 10011);
        var a = new BusinessSlot(BusinessInventorySpace.Consumable, 0); var b = new BusinessSlot(BusinessInventorySpace.Consumable, 1);
        // All unused core fields are zero; the lock sentinel verifies the final byte is after the opaque pair.
        var core = new InventoryItemCoreState(2, 1286, 1, 0, 0, 0, 0, 0, 0, 0, -1, new(0, 0, 0, 0, 0), new(0, 0, 0, 0, 0), 0, 0, 0, 0, 0,
            new(0, 0, 0), new(0, 0, 0), new(0, 0, 0), 0, 0, 0, new(0, 0, 0), 0, 0, 0, 0, 0, 0, 0, 0, 0, 9);
        var inventory = new OpeningInventoryReceipt(session, [new(a, null, ItemSnapshot.FromCore(core, true)), new(b, null, ItemSnapshot.FromCore(core with { ItemId = 1285 }, true))], [],
            [new(a, 1286, 1, 0, 0, 0), new(b, 1285, 2, 0, 0, 0)], DateTimeOffset.UnixEpoch);
        var receipt = new RecipeCraftReceipt(inventory, [new(new(BusinessInventorySpace.Consumable, 36), 1287, 1),
            new(new(BusinessInventorySpace.Material, 19), 3244, 25), new(new(BusinessInventorySpace.SharedMaterial, 5), 3262, 100)]);
        var body = Body(new(receipt)); Assert.Equal(114, body.Length);
        Assert.Equal(Convert.FromHexString("010300650001000000008C00190000000067016400000002"), body[..24]);
        Assert.Equal((byte)9, body[68]); Assert.Equal((byte)9, body[113]);
        Assert.Equal(new byte[12], body[56..68]); Assert.Equal(new byte[12], body[101..113]);
        Assert.Equal(1286, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(27)));
        Assert.Equal(1285, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(72)));
    }
    private static byte[] Body(CompoundItemResponse response)
    {
        var frame = Assert.Single(new CompoundItemEncoder().Encode(response));
        Assert.Equal((byte)1, frame[0]); Assert.Equal((ushort)0x19, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        return frame[15..];
    }
}
