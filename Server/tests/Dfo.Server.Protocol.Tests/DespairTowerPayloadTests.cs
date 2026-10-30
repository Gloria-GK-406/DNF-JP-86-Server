using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game;
using Dfo.Server.Protocol.Game.DungeonSelection;

namespace Dfo.Server.Protocol.Tests;

[Trait("Category", "Unit")]
public sealed class DespairTowerPayloadTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(100)]
    public void Selection_updates_only_the_floor_field(int floor)
    {
        var original = DungeonSelectionPayloadCodec.EncodeEnterSelectDungeon(new ushort[] { 17 });
        var body = DungeonSelectionPayloadCodec.EncodeEnterSelectDungeon(new ushort[] { 17 }, (ushort)floor);
        Assert.Equal(37, body.Length);
        Assert.Equal((ushort)floor, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(18)));
        body[18] = original[18]; body[19] = original[19];
        Assert.Equal(original, body);
    }

    [Fact]
    public void Clear_reward_uses_time_floor_and_ten_signed_item_pairs()
    {
        var body = DespairTowerPayloadCodec.Reward(new(100, 1234, [new(3314, 1)], []));
        Assert.Equal(87, body.Length);
        Assert.Equal(1234u, BinaryPrimitives.ReadUInt32LittleEndian(body));
        Assert.Equal((ushort)100, BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(4)));
        Assert.Equal(10, body[6]);
        Assert.Equal(3314, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(7)));
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(11)));
        Assert.Equal(-1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(15)));
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(19)));
    }
    [Fact]
    public void Dynamic_APC_prefix_and_tail_remain_length_delimited()
    {
        var character = new ActiveCharacterState(new(new(71), 0, [65, 66], 0, 1), new(85, 0),
            new(156, 0), new(0, 0, 0, 0, 0), DateTimeOffset.UnixEpoch);
        var body = DespairTowerPayloadCodec.Apc(new(10, character, [], []));
        Assert.Equal(114, body.Length);
        Assert.Equal(10, body[0]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1)));
        Assert.Equal(new byte[] { 65, 66, 85, 0, 1 }, body[5..10]);
        Assert.All(body[10..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void APC_appearance_and_creature_bytes_stay_fixed()
    {
        var character = new ActiveCharacterState(new(new(71), 0, [65, 66], 3, 1), new(85, 0),
            new(156, 0), new(0, 0, 0, 0, 0), DateTimeOffset.UnixEpoch);
        var core = Dfo.Server.Data.Inventory.StackableItemCore.Create(1, 1000003, 1);
        Dfo.Server.Data.Inventory.InventoryItemState[] equipment = [
            new(null, 0, core with { ItemId = 1010 }), new(null, 7, core with { ItemId = 305419896, Attribute = 0x35 }),
            new(null, 12, core with { ItemId = 1012 }), new(null, 13, core with { ItemId = 1013 }),
            new(null, 25, core with { ItemId = 7777, ItemKind = 5 })];
        var body = DespairTowerPayloadCodec.Apc(new(20, character,
            Dfo.Server.Application.Characters.DrawnAppearance.Of(equipment), equipment));
        Assert.Equal("140200000041425503010000000000000000F2030000000000000000000000000000000000000000000000000000785634120000000000000000F4030000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000611E0000",
            Convert.ToHexString(body));
    }
}
