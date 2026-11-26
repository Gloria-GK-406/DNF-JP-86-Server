using System.Buffers.Binary;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildDonationHistory;

namespace Dfo.Server.Protocol.Tests.Game.GuildDonationHistory;

public sealed class GuildDonationHistoryCodecTests
{
    [Fact]
    public void Donation_history_uses_actual_rows_and_zeroes_the_unused_slots()
    {
        var entry = new GuildDonationEntry("捐赠者"u8.ToArray(), 10000, 10, DateTimeOffset.FromUnixTimeSeconds(1700000000));
        var frame = Assert.Single(new GuildDonationHistoryEncoder().Encode(new GuildDonationHistoryReply([entry])));
        Assert.Equal((byte)1, frame[0]);
        Assert.Equal((ushort)0x0328, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        var payload = frame.AsSpan(15);
        Assert.Equal(2201, payload.Length);
        Assert.Equal((byte)1, payload[0]);
        var row = payload[1..45];
        Assert.Equal(1700000000, BinaryPrimitives.ReadInt32LittleEndian(row));
        Assert.Equal("捐赠者"u8.ToArray(), row.Slice(4, 9).ToArray());
        Assert.True(row.Slice(13, 23).IndexOfAnyExcept((byte)0) < 0);
        Assert.Equal(10, BinaryPrimitives.ReadInt32LittleEndian(row[36..]));
        Assert.Equal(3, BinaryPrimitives.ReadInt32LittleEndian(row[40..]));
        Assert.True(payload[45..].IndexOfAnyExcept((byte)0) < 0);
    }

    [Fact]
    public void Request_is_empty_and_refusal_does_not_send_the_rows()
    {
        var decoder = new GuildDonationHistoryDecoder();
        Assert.True(Assert.IsType<GuildDonationHistoryRequest>(decoder.Decode(GameMessageMetadata.Empty, [])).IsEmpty);
        Assert.False(Assert.IsType<GuildDonationHistoryRequest>(decoder.Decode(GameMessageMetadata.Empty, [0])).IsEmpty);
        var encoder = new GuildDonationHistoryEncoder();
        var refused = Assert.Single(encoder.Encode(new GuildDonationHistoryReply()));
        Assert.Equal(new byte[] { 0, 0 }, refused[15..]);
        var empty = Assert.Single(encoder.Encode(new GuildDonationHistoryReply([])));
        Assert.Equal(2201, empty.Length - 15);
        Assert.Equal((byte)1, empty[15]);
        Assert.True(empty.AsSpan(16).IndexOfAnyExcept((byte)0) < 0);
    }
}
