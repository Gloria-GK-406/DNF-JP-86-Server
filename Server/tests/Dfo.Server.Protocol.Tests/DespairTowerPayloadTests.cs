using System.Buffers.Binary;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests;

public sealed class DespairTowerPayloadTests
{
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
        var body = DespairTowerPayloadCodec.Apc(new(10, character, []));
        Assert.Equal(114, body.Length);
        Assert.Equal(10, body[0]);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(1)));
        Assert.Equal(new byte[] { 65, 66, 85, 0, 1 }, body[5..10]);
        Assert.All(body[10..], value => Assert.Equal(0, value));
    }
}
