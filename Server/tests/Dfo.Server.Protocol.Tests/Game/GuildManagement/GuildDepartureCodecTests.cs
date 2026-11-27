using System.Buffers.Binary;
using Dfo.Server.Protocol.Game.GuildManagement;

namespace Dfo.Server.Protocol.Tests.Game.GuildManagement;

public sealed class GuildDepartureCodecTests
{
    [Fact]
    public void Only_the_kicked_members_notification_carries_a_reason()
    {
        var encoder = new GuildDepartureEncoder();
        var left = Assert.Single(encoder.Encode(new GuildDepartureMessage(GuildDepartureReason.Left)));
        var kicked = Assert.Single(encoder.Encode(new GuildDepartureMessage(GuildDepartureReason.Kicked)));
        var disbanded = Assert.Single(encoder.Encode(new GuildDepartureMessage(GuildDepartureReason.Disbanded)));

        Assert.Equal(0x0097, BinaryPrimitives.ReadUInt16LittleEndian(left.AsSpan(1)));
        Assert.Equal(0x0097, BinaryPrimitives.ReadUInt16LittleEndian(kicked.AsSpan(1)));
        Assert.Equal(0x003D, BinaryPrimitives.ReadUInt16LittleEndian(disbanded.AsSpan(1)));
        Assert.Empty(left[15..]);
        Assert.Equal(new byte[] { 0x02 }, kicked[15..]);
        Assert.Empty(disbanded[15..]);
    }
}
