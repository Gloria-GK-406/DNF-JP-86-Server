using System.Buffers.Binary;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

/// <summary>
/// Where USERINFO subtype 1 carries the opened extra equipment slots (#308).
/// </summary>
/// <remarks>
/// A21 VA 013E6370 reads the length-prefixed 88-byte stat block, then the slot byte on its own,
/// then the equipment count. The block starts at body offset 28 (subtype, version, routing id,
/// thirteen reserved bytes, character id, experience, block length), so the slot byte is at 116.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class ExtraEquipmentSlotWireTests
{
    private const int BlockLengthOffset = 24;
    private const int BlockOffset = 28;
    private const int BlockLength = 88;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Subtype_one_carries_the_slot_byte_right_after_the_stat_block(int slots)
    {
        var source = SelectCharacter.SelectCharacterPayloadTestData.CreateComplete();
        var publicState = source.CharacterEntry.Public with
        {
            Extras = source.CharacterEntry.Public.Extras with { ExtraEquipmentSlotState = slots },
        };
        var core = StackableItemCore.Create(8, 42609, 1234);
        var body = StateSynchronizationPayloadCodec.EncodeExtendedUserState(source.Character,
            publicState, source.CharacterEntry.Skills,
            [new(new(987), 3, core, new(0, 321, new byte[30], 17, 29, 0))],
            new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(BlockLength, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(BlockLengthOffset)));
        // The block closes with u32 100 and two zero bytes; the slot byte is never inside it.
        Assert.Equal(100u, BinaryPrimitives.ReadUInt32LittleEndian(body.AsSpan(BlockOffset + BlockLength - 6)));
        Assert.Equal(0, body[BlockOffset + BlockLength - 2]);
        Assert.Equal(0, body[BlockOffset + BlockLength - 1]);
        Assert.Equal(slots, body[BlockOffset + BlockLength]);
        Assert.Equal(1, body[BlockOffset + BlockLength + 1]);
        Assert.Equal(3, body[BlockOffset + BlockLength + 2]);
    }
}
