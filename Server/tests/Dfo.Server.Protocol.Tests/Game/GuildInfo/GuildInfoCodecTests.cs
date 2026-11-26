using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.GuildInfo;

namespace Dfo.Server.Protocol.Tests.Game.GuildInfo;

public sealed class GuildInfoCodecTests
{
    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 11)]
    public void Panel_sends_the_viewers_settings_flag_and_the_channel_lookup_pair(bool isLeader, int channel)
    {
        var frame = Assert.Single(new GuildInfoEncoder().Encode(
            new GuildInfoMessage(new(new(2), "测试工会", "宣传", 1, channel), isLeader, 3)));
        Assert.Equal((byte)0, frame[0]);
        Assert.Equal((ushort)0x0046, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        var body = frame.AsSpan(15);
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(body);
        Assert.Equal("测试工会"u8.ToArray(), body.Slice(4, nameLength).ToArray());
        var prefix = body[(4 + nameLength)..];
        Assert.Equal(isLeader ? (byte)1 : (byte)0, prefix[0]);
        Assert.Equal((ushort)3, BinaryPrimitives.ReadUInt16LittleEndian(prefix[2..]));
        Assert.Equal(channel > 0 ? 1u : 0u, BinaryPrimitives.ReadUInt32LittleEndian(body[^16..]));
        Assert.Equal((uint)channel, BinaryPrimitives.ReadUInt32LittleEndian(body[^12..]));
    }
}
