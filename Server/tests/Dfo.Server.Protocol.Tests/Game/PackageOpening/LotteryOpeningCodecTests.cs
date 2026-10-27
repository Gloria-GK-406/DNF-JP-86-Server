using Dfo.Server.Data.Networking;
using Dfo.Server.Protocol.Game.LotteryOpening;

namespace Dfo.Server.Protocol.Tests.Game.PackageOpening;

[Trait("Category", "Unit")]
public sealed class LotteryOpeningCodecTests
{
    [Fact]
    public void Captured_start_identifies_phase_and_main_slot()
    {
        var request = Assert.IsType<LotteryOpeningRequest>(new LotteryOpeningDecoder().Decode(
            GameMessageMetadata.Empty, Convert.FromHexString("00004100")));
        Assert.Equal((ushort)0, request.Mode);
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
    public void Failure_uses_only_result_and_reason_without_a_success_payload()
    {
        Assert.Equal(new byte[] {0, 0}, LotteryOpeningEncoder.Encode(new(null)));
    }
}
