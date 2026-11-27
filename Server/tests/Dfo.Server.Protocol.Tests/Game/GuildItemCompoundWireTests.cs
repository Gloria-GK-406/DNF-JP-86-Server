using System.Buffers.Binary;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.GuildItemCompound;

namespace Dfo.Server.Protocol.Tests.Game;

public sealed class GuildItemCompoundWireTests
{
    [Fact]
    public void Gem_compound_keeps_three_units_from_the_same_wire_slot()
    {
        var request = Decode(0x0340, 3, 2, [49, 49, 49, -1]);
        Assert.Equal(ClientMessageType.GameCompoundGuardianGem, request.MessageType);
        var input = Assert.IsType<GuildCompoundInput>(request.Input);
        Assert.Equal(GuildCompoundKind.GuardianGem, input.Kind);
        Assert.Equal(2, input.Rarity);
        Assert.Equal(3, input.Materials.Count);
        Assert.All(input.Materials, slot => Assert.Equal(new BusinessSlot(BusinessInventorySpace.GuardianGem, 0), slot));
    }

    [Theory]
    [InlineData(0x0340, 2, 49, 0)]
    [InlineData(0x0358, 2, 0, 49)]
    [InlineData(0x0358, 3, 1, 2)]
    [InlineData(0x0358, 2, 1, -2)]
    public void Compound_rejects_invalid_counts_and_cross_band_slots(ushort opcode, int count, int first, int second) =>
        Assert.Null(Decode(opcode, count, 2, [first, second, -1, -1]).Input);

    [Theory]
    [InlineData(0x0340, ServerMessageType.GameCompoundGuardianGem, 90002)]
    [InlineData(0x0358, ServerMessageType.GameCompoundGuildMedal, 100380044)]
    public void Compound_receipt_has_only_the_result_item_after_acceptance(ushort opcode, ServerMessageType type, int itemId)
    {
        var message = new GuildItemCompoundResponse(opcode, itemId);
        Assert.Equal(type, message.MessageType);
        var raw = Assert.Single(new GuildItemCompoundEncoder().Encode(message));
        Assert.Equal(20, raw.Length);
        Assert.Equal(1, raw[15]);
        Assert.Equal(itemId, BinaryPrimitives.ReadInt32LittleEndian(raw.AsSpan(16)));
    }

    [Theory]
    [InlineData(0x0340, BusinessInventorySpace.GuardianGem, 51, 13, 90122)]
    [InlineData(0x0358, BusinessInventorySpace.GuildMedal, 2, 12, 100380061)]
    public void Compound_clears_the_consumed_slot_before_installing_its_replacement(
        ushort opcode, BusinessInventorySpace space, short wireSlot, byte kind, int itemId)
    {
        var slot = A21InventoryLayout.ToBusinessSlot(new(InventoryContainerType.GuildMedal, wireSlot));
        Assert.Equal(space, slot.Space);
        var snapshot = ItemSnapshot.FromCore(StackableItemCore.Create(kind, itemId, 1), kind == 13);
        var receipt = new GuildCompoundReceipt(itemId, [new(slot, snapshot, ReplacesConsumedSlot: true)]);

        var messages = GameGuildItemCompoundCommandHandler.SuccessMessages(opcode, receipt, DateTimeOffset.UnixEpoch);
        Assert.Collection(messages,
            message => Assert.IsType<GuildItemCompoundResponse>(message),
            message => AssertDelta(Assert.IsType<InventoryDeltaMessage>(message), -1),
            message => AssertDelta(Assert.IsType<InventoryDeltaMessage>(message), itemId));

        void AssertDelta(InventoryDeltaMessage message, int template)
        {
            var body = message.EncodeBody();
            Assert.Equal(38, body[0]);
            Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(1)));
            Assert.Equal(wireSlot, BinaryPrimitives.ReadInt16LittleEndian(body.AsSpan(3)));
            Assert.Equal(template, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(5)));
        }
    }

    private static GuildItemCompoundRequest Decode(ushort opcode, int count, int rarity, int[] slots)
    {
        var body = new byte[38];
        body.AsSpan(0, 14).Fill(0xB7);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(14), count);
        BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(18), rarity);
        for (var index = 0; index < slots.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(22 + index * 4), slots[index]);
        return Assert.IsType<GuildItemCompoundRequest>(new GuildItemCompoundDecoder().Decode(new(1, opcode, 0, 1), body));
    }
}
