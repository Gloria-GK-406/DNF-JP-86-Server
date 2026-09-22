using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.LotteryOpening;

namespace Dfo.Server.Protocol.Tests.Game.PackageOpening;

public sealed class LotteryOpeningCodecTests
{
    [Fact]
    public void Captured_start_identifies_phase_and_main_slot()
    {
        var request = Assert.IsType<LotteryOpeningRequest>(new LotteryOpeningDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("00004100")));
        Assert.Equal((ushort)0, request.Phase);
        Assert.Equal((short)65, request.Slot);
    }

    [Theory]
    [InlineData("")]
    [InlineData("000041")]
    [InlineData("0000410000")]
    [InlineData("02004100")]
    [InlineData("0000FFFF")]
    [InlineData("00000100")]
    public void Malformed_or_virtual_slot_requests_are_rejected(string hex) =>
        Assert.Throws<InvalidDataException>(() => new LotteryOpeningDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString(hex)));

    [Fact]
    public void Upstream_start_and_failure_keep_the_thirteen_byte_layout()
    {
        Assert.Equal(Convert.FromHexString("01FFFF00000000000000000000"),
            LotteryOpeningEncoder.Encode(new(new(true))));
        Assert.Equal(Convert.FromHexString("00FFFF00000000000000000000"),
            LotteryOpeningEncoder.Encode(new(null)));
    }
}
