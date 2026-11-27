using System.Buffers.Binary;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildAttendance;

namespace Dfo.Server.Protocol.Tests.Game.GuildAttendance;

public sealed class GuildAttendanceCodecTests
{
    [Fact]
    public void Empty_request_and_successful_reply_use_native_attendance_shape()
    {
        var decoder = new GuildAttendanceDetailDecoder();
        Assert.True(Assert.IsType<GuildAttendanceDetailRequest>(decoder.Decode(GameMessageMetadata.Empty, [])).IsEmpty);
        Assert.False(Assert.IsType<GuildAttendanceDetailRequest>(decoder.Decode(GameMessageMetadata.Empty, [1])).IsEmpty);

        var encoder = new GuildAttendanceDetailEncoder();
        var packet = Assert.Single(encoder.Encode(new GuildAttendanceDetailReply(["A"u8.ToArray(), "Bo"u8.ToArray()])));
        Assert.Equal(1, packet[0]);
        Assert.Equal(0x02E9, BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(1)));
        Assert.Equal(new byte[] { 1, 2, 0, 1, 0, 0, 0, 65, 2, 0, 0, 0, 66, 111 }, packet[15..]);
        Assert.Equal(new byte[] { 1, 0, 0 }, Assert.Single(encoder.Encode(new GuildAttendanceDetailReply([])))[15..]);
        Assert.Equal(new byte[] { 0, 0 }, Assert.Single(encoder.Encode(new GuildAttendanceDetailReply()))[15..]);
    }
}
