using Dfo.Server.Data.Characters;
using Dfo.Server.Protocol.Game.StateSynchronization;

namespace Dfo.Server.Protocol.Tests.Game.StateSynchronization;

[Trait("Category", "Unit")]
public sealed class AchievementWireTests
{
    [Fact]
    public void Achievement_list_matches_native_u16_counter_reader_without_shift()
    {
        var body = StateSynchronizationPayloadCodec.EncodeAchievements([
            new(6532, 10000, 0, 0, 0), new(4303, 30, 2, 1, 7)]);
        Assert.Equal(28, body.Length);
        using var reader = new BinaryReader(new MemoryStream(body));
        Assert.Equal(2, reader.ReadInt32());
        Assert.Equal(6532, reader.ReadInt32());
        Assert.Equal(10000, reader.ReadUInt16());
        Assert.Equal(0, reader.ReadUInt16()); Assert.Equal(0, reader.ReadUInt16()); Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(4303, reader.ReadInt32());
        Assert.Equal(30, reader.ReadUInt16()); Assert.Equal(2, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16()); Assert.Equal(7, reader.ReadUInt16());
    }
}
