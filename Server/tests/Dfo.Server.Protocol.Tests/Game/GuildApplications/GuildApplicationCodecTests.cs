using System.Buffers.Binary;
using Dfo.Server.Data.Guilds;
using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.GuildApplications;

namespace Dfo.Server.Protocol.Tests.Game.GuildApplications;

public sealed class GuildApplicationCodecTests
{
    [Fact]
    public void Captured_application_decodes_two_strict_length_prefixed_strings()
    {
        byte[] body = Convert.FromHexString("0C000000E6B58BE8AF95E5B7A5E4BC9A0400000031313131");
        var decoder = new GuildApplyDecoder();
        var request = Assert.IsType<GuildApplicationRequest>(decoder.Decode(GameMessageMetadata.Empty, body));
        Assert.True(request.Valid);
        Assert.Equal("测试工会", request.GuildName);
        Assert.Equal("1111", request.Message);
        Assert.False(Assert.IsType<GuildApplicationRequest>(
            decoder.Decode(GameMessageMetadata.Empty, [..body, 0])).Valid);
        body[4] = 0xFF;
        Assert.False(Assert.IsType<GuildApplicationRequest>(decoder.Decode(GameMessageMetadata.Empty, body)).Valid);
    }

    [Fact]
    public void Application_status_and_approval_match_their_distinct_envelopes()
    {
        var pending = new GuildPendingApplication(new(new(2), new(15), "Hi", DateTimeOffset.UnixEpoch), "Iris");
        var statusEncoder = new GuildApplicationStatusEncoder();
        var frame = Assert.Single(statusEncoder.Encode(new GuildApplicationStatusMessage(pending)));
        Assert.Equal(0, frame[0]);
        Assert.Equal(0x0134, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal("010200000004000000497269730200000048690000000000000000", Convert.ToHexString(frame[15..]));
        Assert.Equal("00", Convert.ToHexString(Assert.Single(statusEncoder.Encode(new GuildApplicationStatusMessage(null)))[15..]));

        var applyEncoder = new GuildApplyReplyEncoder();
        Assert.Equal("0102000000040000004972697302000000486900000000",
            Convert.ToHexString(Assert.Single(applyEncoder.Encode(new GuildApplyReply(pending)))[15..]));

        var approveEncoder = new GuildApproveReplyEncoder();
        Assert.Equal("010F000000", Convert.ToHexString(Assert.Single(approveEncoder.Encode(new GuildApproveReply(15, true)))[15..]));
        Assert.Equal("00000F000000", Convert.ToHexString(Assert.Single(approveEncoder.Encode(new GuildApproveReply(15, false)))[15..]));
    }

    [Fact]
    public void List_serializes_identity_and_clock_age_in_seconds()
    {
        var clock = new ApplicationClock();
        var encoder = new GuildApplicationListReplyEncoder(clock);
        var application = new GuildApplication(new(2), new(15), "Hi", DateTimeOffset.UnixEpoch);
        var frame = Assert.Single(encoder.Encode(new GuildApplicationListReply([new(application, "Applicant"u8.ToArray(), 2, 3, 30)])));
        Assert.Equal(1, frame[0]);
        Assert.Equal(0x0160, BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(1)));
        Assert.Equal("01010000000F000000090000004170706C6963616E7402031E0002000000486980510100",
            Convert.ToHexString(frame[15..]));
    }

    private sealed class ApplicationClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddDays(1);
    }
}
