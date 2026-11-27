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
        var timestamp = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        var entry = new GuildContributionEntry("捐赠者"u8.ToArray(), 10, timestamp, GuildContributionSource.GoldDonation);
        var attendance = new GuildContributionEntry("会长"u8.ToArray(), 100, timestamp, GuildContributionSource.LeaderAttendance);
        var frame = Assert.Single(new GuildDonationHistoryEncoder().Encode(
            new GuildDonationHistoryReply(ServerMessageType.GameGuildDonationHistory, [entry, attendance])));
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
        Assert.Equal(100, BinaryPrimitives.ReadInt32LittleEndian(payload[(45 + 36)..]));
        Assert.Equal(6, BinaryPrimitives.ReadInt32LittleEndian(payload[(45 + 40)..]));
        Assert.True(payload[89..].IndexOfAnyExcept((byte)0) < 0);
    }

    [Fact]
    public void Gm_history_window_reads_a_count_and_its_own_row_layout()
    {
        var entry = new GuildContributionEntry("捐赠者"u8.ToArray(), 50, DateTimeOffset.FromUnixTimeSeconds(1700000000),
            GuildContributionSource.GoldDonation);
        var frame = Assert.Single(new GuildDonationHistoryEncoder().Encode(
            new GuildDonationHistoryReply(ServerMessageType.GameGuildMileageHistory, [entry])));
        Assert.Equal((byte)1, frame[0]);
        Assert.Equal((ushort)0x02EA, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        var payload = frame.AsSpan(15);
        Assert.Equal(49, payload.Length);
        Assert.Equal((byte)1, payload[0]);
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(payload[1..]));
        var row = payload[5..];
        Assert.Equal(1700000000, BinaryPrimitives.ReadInt32LittleEndian(row));
        Assert.Equal("捐赠者"u8.ToArray(), row.Slice(4, 9).ToArray());
        Assert.True(row.Slice(13, 21).IndexOfAnyExcept((byte)0) < 0);
        Assert.Equal((byte)4, row[34]);
        Assert.True(row[35..40].IndexOfAnyExcept((byte)0) < 0);
        Assert.Equal(50, BinaryPrimitives.ReadInt32LittleEndian(row[40..]));
    }

    [Fact]
    public void Request_is_empty_and_refusal_does_not_send_the_rows()
    {
        var decoder = new GuildDonationHistoryDecoder();
        Assert.True(Assert.IsType<GuildDonationHistoryRequest>(decoder.Decode(GameMessageMetadata.Empty, [])).IsEmpty);
        Assert.False(Assert.IsType<GuildDonationHistoryRequest>(decoder.Decode(GameMessageMetadata.Empty, [0])).IsEmpty);
        var encoder = new GuildDonationHistoryEncoder();
        var refused = Assert.Single(encoder.Encode(new GuildDonationHistoryReply(ServerMessageType.GameGuildDonationHistory)));
        Assert.Equal(new byte[] { 0, 0 }, refused[15..]);
        var empty = Assert.Single(encoder.Encode(new GuildDonationHistoryReply(ServerMessageType.GameGuildDonationHistory, [])));
        Assert.Equal(2201, empty.Length - 15);
        Assert.Equal((byte)1, empty[15]);
        Assert.True(empty.AsSpan(16).IndexOfAnyExcept((byte)0) < 0);
    }

    [Fact]
    public void Gm_history_accepts_only_the_empty_request_and_counts_zero_rows()
    {
        var decoder = new GuildMileageHistoryDecoder();
        var query = Assert.IsType<GuildDonationHistoryRequest>(decoder.Decode(GameMessageMetadata.Empty, []));
        Assert.Equal(ClientMessageType.GameGuildMileageHistory, query.MessageType);
        Assert.True(query.IsEmpty);
        Assert.False(Assert.IsType<GuildDonationHistoryRequest>(decoder.Decode(GameMessageMetadata.Empty, [0])).IsEmpty);
        var encoder = new GuildDonationHistoryEncoder();
        var empty = Assert.Single(encoder.Encode(new GuildDonationHistoryReply(ServerMessageType.GameGuildMileageHistory, [])));
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0 }, empty[15..]);
        var refused = Assert.Single(encoder.Encode(new GuildDonationHistoryReply(ServerMessageType.GameGuildMileageHistory)));
        Assert.Equal(new byte[] { 0, 0 }, refused[15..]);
    }
}
